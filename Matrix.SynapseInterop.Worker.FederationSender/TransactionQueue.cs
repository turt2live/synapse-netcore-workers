using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Transactions;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.Models;
using Matrix.SynapseInterop.Replication.Structures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class TransactionQueue
    {
        private const int MAX_PDUS_PER_TRANSACTION = 50;
        private const int MAX_EDUS_PER_TRANSACTION = 100;
        private const int MAX_BACKOFF_SECS = 60 * 60 * 24;
        private readonly FederationClient _client;
        private Task _presenceProcessing;
        private Task _eventsProcessing;
        private SigningKey _signingKey;
        private readonly string _serverName;
        private readonly string _connString;
        private int _txnId;
        private int _lastEventPoke;
        private object attemptTransactionLock;

        private readonly Dictionary<string, PresenceState> _userPresence;
        private readonly Dictionary<string, Task> _destOngoingTrans;
        private readonly Dictionary<string, Transaction> _destPendingTransactions;
        private readonly Dictionary<string, long> _destLastDeviceMsgStreamId;
        private readonly Dictionary<string, long> _destLastDeviceListStreamId;

        public TransactionQueue(string serverName, string connectionString, SigningKey key, IConfigurationSection clientConfig)
        {
            _client = new FederationClient(serverName, key, clientConfig);
            _userPresence = new Dictionary<string, PresenceState>();
            _destOngoingTrans = new Dictionary<string, Task>();
            _destPendingTransactions = new Dictionary<string, Transaction>();
            _destLastDeviceMsgStreamId = new Dictionary<string, long>();
            _destLastDeviceListStreamId = new Dictionary<string, long>();
            _presenceProcessing = Task.CompletedTask;
            _eventsProcessing = Task.CompletedTask;
            _serverName = serverName;
            _txnId = (int) (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            _connString = connectionString;
            _lastEventPoke = -1;
            _signingKey = key;
        }

        public void OnEventUpdate(string streamPos)
        {
            _lastEventPoke = Math.Max(int.Parse(streamPos), _lastEventPoke);

            if (!_eventsProcessing.IsCompleted)
            {
                return;
            }

            Console.WriteLine("Poking ProcessPendingEvents");

            _eventsProcessing = ProcessPendingEvents().ContinueWith((t) =>
            {
                if (t.IsFaulted)
                {
                    Console.WriteLine("Failed to process events: {0}", t.Exception);
                }
            });
        }

        public void SendPresence(List<PresenceState> presenceSet)
        {
            foreach (var presence in presenceSet)
            {
                // Only send presence about our own users.
                if (IsMineId(presence.user_id))
                {
                    if (!_userPresence.TryAdd(presence.user_id, presence))
                    {
                        _userPresence.Remove(presence.user_id);
                        _userPresence.Add(presence.user_id, presence);
                    }
                }
            }

            if (!_presenceProcessing.IsCompleted)
            {
                return;
            }

            _presenceProcessing = ProcessPendingPresence();
        }

        public void SendEdu(EduEvent obj)
        {
            if (obj.destination == _serverName)
            {
                return;
            }

            // Prod device messages if we've not seen this destination before.
            if (!_destLastDeviceMsgStreamId.ContainsKey(obj.destination))
            {
                SendDeviceMessages(obj.destination);
            }

            Transaction transaction = GetOrCreateTransactionForDest(obj.destination);

            transaction.edus.Add(obj);
            AttemptTransaction(obj.destination);
        }

        public void SendEdu(EduEvent ev, string key)
        {
            Transaction transaction = GetOrCreateTransactionForDest(ev.destination);
            var existingItem = transaction.edus.FindIndex(edu => edu.InternalKey == key);

            if (existingItem >= 0)
            {
                transaction.edus.RemoveAt(existingItem);
            }

            ev.InternalKey = key;
            SendEdu(ev);
        }

        public void SendDeviceMessages(string destination)
        {
            if (_serverName == destination)
            {
                return; // Obviously.
            }

            // Fetch messages for destination
            var messages = GetNewDeviceMessages(destination);

            if (messages.Item1.Count == 0)
            {
                return;
            }

            var transaction = GetOrCreateTransactionForDest(destination);

            messages.Item1.ForEach(message =>
            {
                transaction.edus.Add(new EduEvent
                {
                    destination = destination,
                    content = JObject.Parse(message.MessagesJson),
                    edu_type = "m.direct_to_device",
                    origin = _serverName,
                    StreamId = message.StreamId,
                });
            });

            messages.Item2.ForEach(list =>
            {
                transaction.edus.Add(new EduEvent
                {
                    destination = destination,
                    content = JObject.FromObject(list),
                    edu_type = "m.device_list_update",
                    origin = _serverName,
                    StreamId = list.stream_id,
                });
            });

            AttemptTransaction(destination);
        }

        private Tuple<List<DeviceFederationOutbox>, List<DeviceContentSet>> GetNewDeviceMessages(string destination)
        {
            long lastMsgId = _destLastDeviceMsgStreamId.GetValueOrDefault(destination, 0);
            long lastListId = _destLastDeviceListStreamId.GetValueOrDefault(destination, 0);

            using (var db = new SynapseDbContext(_connString))
            {
                var messages = db
                              .DeviceFederationOutboxes
                              .Where(message =>
                                         message.Destination == destination &&
                                         message.StreamId > lastMsgId)
                              .OrderBy(message => message.StreamId).Take(MAX_EDUS_PER_TRANSACTION).ToList();

                var resLists = db.GetNewDevicesForDestination(destination, MAX_EDUS_PER_TRANSACTION);
                return Tuple.Create(messages, resLists);
            }
        }

        private async Task ProcessPendingPresence()
        {
            var presenceSet = _userPresence.Values.ToList();
            _userPresence.Clear();
            var hostsAndState = await GetInterestedRemotes(presenceSet);

            foreach (var hostState in hostsAndState)
            {
                var formattedPresence = FormatPresenceContent(hostState.Value);

                foreach (var host in hostState.Key)
                {
                    Console.WriteLine($"Sending presence to {host}");
                    // TODO: Handle case where we have over 100 EDUs
                    Transaction transaction = GetOrCreateTransactionForDest(host);

                    transaction.edus.Add(new EduEvent
                    {
                        destination = host,
                        origin = _serverName,
                        edu_type = "m.presence",
                        content = formattedPresence,
                    });
                }
            }
            
            // Do this seperate from the above to batch presence together
            foreach (var hostState in hostsAndState)
            {
                foreach (var host in hostState.Key)
                {
                    AttemptTransaction(host);
                }
            }
        }

        private async Task ProcessPendingEvents()
        {
            List<EventJsonSet> events;
            int top = _lastEventPoke;
            int last;

            // Get the set of events we need to process.
            using (var db = new SynapseDbContext(_connString))
            {
                last = (await db.FederationStreamPosition.SingleAsync((m) => m.Type == "events")).StreamId;
                events = db.GetAllNewEventsStream(last, top, MAX_PDUS_PER_TRANSACTION);
            }

            if (events.Count == 0)
            {
                Console.WriteLine("No new events to handle");
                return;
            }

            if (events.Count == MAX_PDUS_PER_TRANSACTION)
            {
                Console.WriteLine($"WARN: more than {MAX_PDUS_PER_TRANSACTION} events behind");
                top = events.Last().StreamOrdering;
            }

            Console.WriteLine($"Processing from {last} to {top}");
            // Skip any events that didn't come from us.
            var roomEvents = events.SkipWhile(e => !IsMineId(e.Sender)).GroupBy(e => e.RoomId);

            foreach (var item in roomEvents)
            {
                var hosts = await GetHostsInRoom(item.Key);

                foreach (var roomEvent in item)
                {
                    // TODO: Support send_on_bahalf_of?

                    IPduEvent pduEv;

                    // NOTE: This is an event format version, not room version.
                    if (roomEvent.Version == 1)
                    {
                        pduEv = new PduEventV1()
                        {
                            event_id = roomEvent.EventId
                        };
                    }
                    else // Default to latest event format version.
                    {
                        pduEv = new PduEventV2();
                    }

                    pduEv.content = roomEvent.Content["content"] as JObject;
                    pduEv.origin = _serverName;
                    pduEv.depth = (long) roomEvent.Content["depth"];
                    pduEv.auth_events = roomEvent.Content["auth_events"];
                    pduEv.prev_events = roomEvent.Content["prev_events"];
                    pduEv.origin_server_ts = (long) roomEvent.Content["origin_server_ts"];

                    if (roomEvent.Content.ContainsKey("redacts"))
                    {
                        pduEv.redacts = (string) roomEvent.Content["redacts"];
                    }

                    pduEv.room_id = roomEvent.RoomId;
                    pduEv.sender = roomEvent.Sender;
                    pduEv.prev_state = roomEvent.Content["prev_state"];

                    if (roomEvent.Content.ContainsKey("state_key"))
                    {
                        pduEv.state_key = (string) roomEvent.Content["state_key"];
                    }

                    pduEv.type = (string) roomEvent.Content["type"];
                    pduEv.unsigned = (JObject) roomEvent.Content["unsigned"];
                    pduEv.hashes = roomEvent.Content["hashes"];

                    pduEv.signatures = new Dictionary<string, Dictionary<string, string>>();

                    foreach (var sigHosts in (JObject) roomEvent.Content["signatures"])
                    {
                        pduEv.signatures.Add(sigHosts.Key, new Dictionary<string, string>());

                        foreach (var sigs in (JObject) sigHosts.Value)
                        {
                            pduEv.signatures[sigHosts.Key].Add(sigs.Key, sigs.Value.Value<string>());
                        }
                    }

                    //TODO: I guess we need to fetch the destinations for each event in a room, because someone may have got banned in between.
                    foreach (var host in hosts)
                    {
                        Transaction transaction = GetOrCreateTransactionForDest(host);
                        transaction.pdus.Add(pduEv);
                        AttemptTransaction(host);
                    }
                }
            }

            using (var db = new SynapseDbContext(_connString))
            {
                Console.WriteLine($"Saving position {top} to DB");
                var streamPos = db.FederationStreamPosition.First((e) => e.Type == "events");
                streamPos.StreamId = top;
                db.SaveChanges();
            }

            // Still behind?
            if (events.Count == MAX_PDUS_PER_TRANSACTION)
            {
                Console.WriteLine("Calling ProcessPendingEvents again because we are still behind");
                await ProcessPendingEvents();
            }
        }

        private void AttemptTransaction(string destination)
        {
            // Lock here to avoid racing.
            lock (attemptTransactionLock)
            {
                if (_destOngoingTrans.ContainsKey(destination))
                {
                    if (!_destOngoingTrans[destination].IsCompleted)
                    {
                        // Already ongoing.
                        return;
                    }

                    _destOngoingTrans.Remove(destination);
                }

                _destOngoingTrans.Add(destination, AttemptNewTransaction(destination));
            }
        }

        private async Task AttemptNewTransaction(string destination)
        {
            Transaction currentTransaction;
            Random random = new Random();
            if (!_destPendingTransactions.TryGetValue(destination, out currentTransaction))
            {
                Console.WriteLine($"No more transactions for {destination}");
                return;
            }

            while (true)
            {
                WorkerMetrics.IncOngoingTransactions();

                using (WorkerMetrics.TransactionDurationTimer(destination))
                {
                    _destPendingTransactions.Remove(destination);

                    try
                    {
                        await _client.SendTransaction(currentTransaction);
                        ClearDeviceMessages(currentTransaction);
                        WorkerMetrics.DecOngoingTransactions();
                        WorkerMetrics.IncTransactionsSent(true, destination);
                        WorkerMetrics.IncTransactionEventsSent("pdu", destination, currentTransaction.pdus.Count);
                        WorkerMetrics.IncTransactionEventsSent("edu", destination, currentTransaction.edus.Count);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (ex.InnerException is SocketException)
                        {
                            // Really backoff a socket exception hard, because the host probably doesn't exist
                            currentTransaction.BackoffSecs *= 10;
                        }

                        throw;
                    }
                    catch (Exception ex)
                    {
                        // XXX: Let's not retry presence.
                        bool retry = currentTransaction.pdus.Count > 0 ||
                                     !currentTransaction.edus.TrueForAll(ev => ev.edu_type == "m.presence");

                        WorkerMetrics.DecOngoingTransactions();

                        Console.WriteLine("Transaction {0} {1} failed: {2}",
                                          currentTransaction.transaction_id, destination, ex);

                        WorkerMetrics.IncTransactionsSent(false, destination);

                        if (retry)
                        {
                            _destPendingTransactions.Add(destination, currentTransaction);
                            currentTransaction.BackoffSecs *= 2;
                            // Add some randomness to the backoff
                            currentTransaction.BackoffSecs = (int) Math.Ceiling(currentTransaction.BackoffSecs * random.NextDouble() + 0.5);

                            Console.WriteLine("Retrying txn {0} in {2}",
                                              currentTransaction.transaction_id, currentTransaction.BackoffSecs);

                            await Task.Delay(Math.Min(currentTransaction.BackoffSecs * 1000, MAX_BACKOFF_SECS));
                            continue;
                        }
                    }
                }

                if (!_destPendingTransactions.TryGetValue(destination, out currentTransaction))
                {
                    Console.WriteLine($"No more transactions for {destination}");
                    break;
                }
            }

        }

        private void ClearDeviceMessages(Transaction transaction)
        {
            var deviceMsgs = transaction.edus.Where(m => m.edu_type == "m.direct_to_device").ToList()
                                        .ConvertAll(m => m.StreamId);

            var deviceLists = transaction.edus.Where(m => m.edu_type == "m.device_list_update").ToList()
                                         .ConvertAll(m => Tuple.Create(m.StreamId, (string) m.content["user_id"]));

            using (var db = new SynapseDbContext(_connString))
            {
                if (deviceMsgs.Count != 0)
                {
                    _destLastDeviceMsgStreamId[transaction.destination] = deviceMsgs.Max();
                    var deviceMsgEntries = db.DeviceFederationOutboxes.Where(m => deviceMsgs.Contains(m.StreamId));

                    if (deviceMsgEntries.Any())
                    {
                        db.DeviceFederationOutboxes.RemoveRange(deviceMsgEntries);
                        db.SaveChanges();
                    }
                    else
                    {
                        Console.WriteLine("WARN: No messages to delete in outbox, despite sending messages in this txn");
                    }
                }

                if (deviceLists.Count == 0) return;

                _destLastDeviceListStreamId[transaction.destination] = deviceLists.Max(e => e.Item1);

                var deviceListEntries = db.DeviceListsOutboundPokes
                                          .Where(m =>
                                                     deviceLists.FindIndex(e => e.Item1 == m.StreamId &&
                                                                                e.Item2 == m.UserId) >= 0);

                if (deviceListEntries.Any())
                {
                    foreach (var msg in deviceListEntries)
                    {
                        msg.Sent = true;
                    }

                    db.SaveChanges();
                }
                else
                {
                    Console.WriteLine("WARN: No device lists to mark as sent, despite sending lists in this txn");
                }
            }
        }

        private bool IsMineId(string id)
        {
            return id.Split(":")[1] == _serverName;
        }

        private JObject FormatPresenceContent(PresenceState state)
        {
            var now = (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var obj = new JObject();
            obj.Add("presence", state.state);
            obj.Add("user_id", state.user_id);

            if (state.last_active_ts != 0)
            {
                obj.Add("last_active_ago", (int) Math.Round(now - state.last_active_ts));
            }

            if (state.status_msg != null && state.state != "offline")
            {
                obj.Add("status_msg", state.status_msg);
            }

            if (state.state == "online")
            {
                obj.Add("currently_active", state.currently_active);
            }

            return obj;
        }

        /// <summary>
        /// Get a set of remote hosts interested in this presence.
        /// </summary>
        private async Task<Dictionary<string[], PresenceState>> GetInterestedRemotes(List<PresenceState> presenceSet)
        {
            var dict = new Dictionary<string[], PresenceState>();

            using (var db = new SynapseDbContext(_connString))
            {
                // Get the list of rooms shared by these users.
                // We are intentionally skipping presence lists here.
                foreach (var presence in presenceSet)
                {
                    var membershipList = await db
                                              .RoomMemberships
                                              .Where(m =>
                                                         m.Membership == "join" &&
                                                         m.UserId == presence.user_id).ToListAsync();

                    var hosts = new HashSet<string>();

                    // XXX: This is NOT the way to do this, but functions well enough
                    // for a demo.
                    foreach (var roomId in membershipList.ConvertAll(m => m.RoomId))
                    {
                        await db.RoomMemberships
                                .Where(m => m.RoomId == roomId)
                                .ForEachAsync(m =>
                                                  hosts.Add(m.UserId
                                                             .Split(":")
                                                              [1]));
                    }

                    // Never include ourselves
                    hosts.Remove(_serverName);
                    // Now get the hosts for that room.
                    dict.Add(hosts.ToArray(), presence);
                }
            }

            return dict;
        }

        private async Task<HashSet<string>> GetHostsInRoom(string roomId)
        {
            HashSet<string> hosts = new HashSet<string>();

            using (var db = new SynapseDbContext(_connString))
            {
                await db.RoomMemberships.Where(m => m.RoomId == roomId)
                        .ForEachAsync((m) =>
                                          hosts.Add(m.UserId.Split(":")[1]));
            }

            hosts.Remove(_serverName);
            return hosts;
        }

        private long GetTs()
        {
            return (long) (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }

        private Transaction GetOrCreateTransactionForDest(string dest)
        {
            if (!_destPendingTransactions.TryGetValue(dest, out var transaction))
            {
                _txnId++;

                transaction = new Transaction
                {
                    edus = new List<EduEvent>(),
                    pdus = new List<IPduEvent>(),
                    origin = _serverName,
                    origin_server_ts = GetTs(),
                    transaction_id = _txnId.ToString(),
                    destination = dest,
                    BackoffSecs = 2
                };

                _destPendingTransactions.Add(dest, transaction);
            }

            return transaction;
        }
    }
}
