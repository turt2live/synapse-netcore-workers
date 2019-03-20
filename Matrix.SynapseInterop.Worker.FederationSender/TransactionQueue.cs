using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Common.MatrixUtils;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Matrix.SynapseInterop.Replication.Structures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class TransactionQueue
    {
        private const int MAX_PDUS_PER_TRANSACTION = 50;
        private const int MAX_EDUS_PER_TRANSACTION = 100;
        // If a room has more hosts than MAX_HOSTS_FOR_PRESENCE, ignore that room.
        private const int MaxHostsForPresence = 40;
        private readonly TimeSpan minDelayBetweenTxns = TimeSpan.FromMilliseconds(150);
        private static readonly ILogger log = Log.ForContext<TransactionQueue>();
        private readonly Backoff _backoff;
        private readonly FederationClient _client;
        private readonly string _connString;
        private readonly Dictionary<string, long> _destLastDeviceListStreamId;
        private readonly Dictionary<string, long> _destLastDeviceMsgStreamId;
        private readonly Dictionary<string, Task> _destOngoingTrans;
        private readonly Dictionary<string, DateTime> _destLastTxnTime;
        private readonly ConcurrentDictionary<string, LinkedList<Transaction>> _destPendingTransactions;
        private readonly CachedMatrixRoomSet _roomCache;
        private readonly string _serverName;

        private readonly Dictionary<string, PresenceState> _userPresence;
        private Task _eventsProcessing;
        private int _lastEventPoke;
        private Task _presenceProcessing;
        private SigningKey _signingKey;
        private ulong _txnId;

        public TransactionQueue(string serverName,
                                string connectionString,
                                SigningKey key,
                                IConfigurationSection clientConfig
        )
        {
            _client = new FederationClient(serverName, key, clientConfig);
            _userPresence = new Dictionary<string, PresenceState>();
            _destOngoingTrans = new Dictionary<string, Task>();
            _destPendingTransactions = new ConcurrentDictionary<string, LinkedList<Transaction>>();
            _destLastDeviceMsgStreamId = new Dictionary<string, long>();
            _destLastDeviceListStreamId = new Dictionary<string, long>();
            _presenceProcessing = Task.CompletedTask;
            _eventsProcessing = Task.CompletedTask;
            _serverName = serverName;
            _txnId = (ulong) (DateTime.Now - new DateTime(1970, 1, 1)).TotalMilliseconds;
            _connString = connectionString;
            _lastEventPoke = -1;
            _signingKey = key;
            _backoff = new Backoff();
            _roomCache = new CachedMatrixRoomSet();
            _destLastTxnTime = new Dictionary<string, DateTime>();
        }
    
        public bool OnEventUpdate(string streamPos)
        {
            _lastEventPoke = Math.Max(int.Parse(streamPos), _lastEventPoke);

            if (!_eventsProcessing.IsCompleted) return false;

            log.Debug("Running ProcessPendingEvents");
            var timer = WorkerMetrics.FunctionTimer("ProcessPendingEvents");

            _eventsProcessing = ProcessPendingEvents().ContinueWith(t =>
            {
                var duration = timer.ObserveDuration().TotalMilliseconds;
                timer.Dispose();
                log.Debug("Ran ProcessPendingEvents {duration}ms", duration);
                if (t.IsFaulted) log.Error("Failed to process events: {Exception}", t.Exception);
            });

            return true;
        }

        public void SendPresence(List<PresenceState> presenceSet)
        {
            foreach (var presence in presenceSet)
            {
                log.Debug("Got new presence for {userId} = {state} ({last_active})",
                          presence.user_id,
                          presence.state,
                          presence.last_active_ts);

                // Only send presence about our own users.
                if (!IsMineId(presence.user_id))
                {
                    continue;
                }

                if (!_userPresence.TryAdd(presence.user_id, presence))
                {
                    _userPresence.Remove(presence.user_id);
                    _userPresence.Add(presence.user_id, presence);
                }
            }

            if (!_presenceProcessing.IsCompleted) return;

            _presenceProcessing = Task.Run(() => { ProcessPendingPresence(); });
        }

        public void SendEdu(EduEvent ev)
        {
            if (ev.destination == _serverName || _backoff.HostIsDown(ev.destination)) return;
            log.Debug("Sending EDU {edu_type} to {destination}", ev.edu_type, ev.destination);

            // Prod device messages if we've not seen this destination before.
            if (!_destLastDeviceMsgStreamId.ContainsKey(ev.destination)) SendDeviceMessages(ev.destination);

            var transaction = GetOrCreateTransactionForDest(ev.destination);

            transaction.edus.Add(ev);
            AttemptTransaction(ev.destination);
        }

        public void SendEdu(EduEvent ev, string key)
        {
            if (ev.destination == _serverName || _backoff.HostIsDown(ev.destination)) return;

            var transaction = GetOrCreateTransactionForDest(ev.destination);
            var existingItem = transaction.edus.FindIndex(edu => edu.InternalKey == key);

            if (existingItem >= 0) transaction.edus.RemoveAt(existingItem);

            ev.InternalKey = key;
            SendEdu(ev);
        }

        public void SendDeviceMessages(string destination)
        {
            if (_serverName == destination) return; // Obviously.

            if (_backoff.HostIsDown(destination))
            {
                log.Warning("NOT sending device messages to {destination}. Destination is DOWN.", destination);
                return;
            }

            // Fetch messages for destination
            var messages = GetNewDeviceMessages(destination);

            if (messages.Item1.Count == 0 && messages.Item2.Count == 0) return;

            log.Debug("Sending device messages to {destination} ({d2d},{dlu})",
                      destination,
                      messages.Item1.Count,
                      messages.Item2.Count);

            var transaction = GetOrCreateTransactionForDest(destination);

            messages.Item1.ForEach(message =>
            {
                // If we go over the limit, go to the next transaction
                if (transaction.edus.Count == MAX_EDUS_PER_TRANSACTION)
                {
                    transaction = GetOrCreateTransactionForDest(destination);
                }

                transaction.edus.Add(new EduEvent
                {
                    destination = destination,
                    content = JObject.Parse(message.MessagesJson),
                    edu_type = "m.direct_to_device",
                    origin = _serverName,
                    StreamId = message.StreamId
                });
            });

            messages.Item2.ForEach(list =>
            {
                // If we go over the limit, go to the next transaction
                if (transaction.edus.Count == MAX_EDUS_PER_TRANSACTION)
                {
                    transaction = GetOrCreateTransactionForDest(destination);
                }

                transaction.edus.Add(new EduEvent
                {
                    destination = destination,
                    content = JObject.FromObject(list),
                    edu_type = "m.device_list_update",
                    origin = _serverName,
                    StreamId = list.stream_id
                });
            });

            AttemptTransaction(destination);
        }

        private Tuple<List<DeviceFederationOutbox>, List<DeviceContentSet>> GetNewDeviceMessages(string destination)
        {
            var lastMsgId = _destLastDeviceMsgStreamId.GetValueOrDefault(destination, 0);
            var lastListId = _destLastDeviceListStreamId.GetValueOrDefault(destination, 0);

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

        private void ProcessPendingPresence()
        {
            using (WorkerMetrics.FunctionTimer("ProcessPendingPresence"))
            {
                log.Debug("Running ProcessPendingPresence");
                var presenceSet = _userPresence.Values.ToList();
                _userPresence.Clear();
                var hostsAndState = GetInterestedRemotes(presenceSet);
                var i = 0;
                var edus = 0;

                foreach (var hostState in hostsAndState)
                {
                    i++;
                    log.Debug("Processing presence {i}/{total}", i, hostsAndState.Count);
                    var formattedPresence = FormatPresenceContent(hostState.Value);

                    foreach (var host in hostState.Key)
                    {
                        var transaction = GetOrCreateTransactionForDest(host);

                        transaction.edus.Add(new EduEvent
                        {
                            destination = host,
                            origin = _serverName,
                            edu_type = "m.presence",
                            content = formattedPresence
                        });

                        edus++;
                    }
                }

                // Do this seperate from the above to batch presence together
                foreach (var hostState in hostsAndState)
                foreach (var host in hostState.Key)
                {
                    AttemptTransaction(host);
                }

                log.Debug("Finished ProcessPendingPresence. Queued {edus} EDUs", edus);
            }
        }

        private async Task ProcessPendingEvents()
        {
            List<EventJsonSet> events;
            var top = _lastEventPoke;
            int last;

            // Get the set of events we need to process.
            using (var db = new SynapseDbContext(_connString))
            {
                last = (await db.FederationStreamPosition.SingleAsync(m => m.Type == "events")).StreamId;
                events = db.GetAllNewEventsStream(last, top, MAX_PDUS_PER_TRANSACTION).ToList();
            }

            if (!events.Any())
            {
                log.Debug("No new events to handle");
                return;
            }

            if (events.Count == MAX_PDUS_PER_TRANSACTION)
            {
                log.Warning("More than {Max} events behind", MAX_PDUS_PER_TRANSACTION);
                top = events.Last().StreamOrdering;
            }

            log.Information("Processing from {last} to {top}", last, top);
            var hostsToSendTo = new HashSet<string>();
            
            // Invalidate any caches if we see a membership event of any kind.
            foreach (var memberEv in events.Where(e => e.Type == "m.room.member"))
            {
                if (_roomCache.InvalidateRoom(memberEv.RoomId))
                    log.Debug("Invalidated cache for {roomId}", memberEv.RoomId);
            }
            
            // Skip any events that didn't come from us.
            foreach (var item in events.Where(e => IsMineId(e.Sender)).GroupBy(e => e.RoomId))
            {   
                //TODO: I guess we need to fetch the destinations for each event in a room, because someone may have got banned in between.
                var hosts = GetHostsInRoom(item.Key);

                if (hosts.Count == 0)
                {
                    continue;
                }
                
                hostsToSendTo.UnionWith(hosts);

                foreach (var roomEvent in item)
                {
                    // TODO: Support send_on_bahalf_of?

                    IPduEvent pduEv;

                    JObject content = await roomEvent.GetContent();
                    
                    // NOTE: This is an event format version, not room version.
                    if (roomEvent.Version == 1)
                        pduEv = new PduEventV1
                        {
                            event_id = roomEvent.EventId
                        };
                    else // Default to latest event format version.
                        pduEv = new PduEventV2();

                    pduEv.content = content["content"] as JObject;
                    pduEv.origin = _serverName;
                    pduEv.depth = (long) content["depth"];
                    pduEv.auth_events = content["auth_events"];
                    pduEv.prev_events = content["prev_events"];
                    pduEv.origin_server_ts = (long) content["origin_server_ts"];

                    if (content.ContainsKey("redacts")) pduEv.redacts = (string) content["redacts"];

                    pduEv.room_id = roomEvent.RoomId;
                    pduEv.sender = roomEvent.Sender;
                    pduEv.prev_state = content["prev_state"];

                    if (content.ContainsKey("state_key"))
                        pduEv.state_key = (string) content["state_key"];

                    pduEv.type = (string) content["type"];
                    pduEv.unsigned = (JObject) content["unsigned"];
                    pduEv.hashes = content["hashes"];

                    pduEv.signatures = new Dictionary<string, Dictionary<string, string>>();

                    foreach (var sigHosts in (JObject) content["signatures"])
                    {
                        pduEv.signatures.Add(sigHosts.Key, new Dictionary<string, string>());

                        foreach (var sigs in (JObject) sigHosts.Value)
                            pduEv.signatures[sigHosts.Key].Add(sigs.Key, sigs.Value.Value<string>());
                    }
                    
                    HashSet<string> hostsToSend = new HashSet<string>(hosts);

                    if (pduEv.type == "m.room.member" && !string.IsNullOrEmpty(pduEv.state_key))
                    {
                        var dest = pduEv.state_key.Split(":");

                        if (dest.Length > 1)
                        {
                            // If this is a member event, ensure that the destination gets it
                            hostsToSend.Add(dest[1]);
                        }
                    }

                    foreach (var host in hostsToSend)
                    {
                        var transaction = GetOrCreateTransactionForDest(host);
                        transaction.pdus.Add(pduEv);
                    }
                }
            }
            
            // We are handling this elsewhere.
#pragma warning disable 4014
            hostsToSendTo.ForEach(h => AttemptNewTransaction(h));
#pragma warning restore 4014

            using (var db = new SynapseDbContext(_connString))
            {
                log.Debug("Saving position {top} to DB", top);
                var streamPos = db.FederationStreamPosition.First(e => e.Type == "events");
                streamPos.StreamId = top;
                db.SaveChanges();
            }

            // Still behind?
            if (top < _lastEventPoke)
            {
                log.Information("Calling ProcessPendingEvents again because we are still behind {top} < {_lastEventPoke}", top, _lastEventPoke);
                await ProcessPendingEvents();
            }
        }

        private void AttemptTransaction(string destination)
        {
            // Lock here to avoid racing.
            lock (this)
            {
                var now = DateTime.Now;

                if (_destLastTxnTime.TryGetValue(destination, out var lastTime))
                {
                    var diff = now - lastTime;

                    if (diff < minDelayBetweenTxns)
                    {
                        var delay = minDelayBetweenTxns - diff;
                        log.Debug("Delaying task for {delay}ms", delay.TotalMilliseconds);
                        Task.Delay(delay).ContinueWith(_ => AttemptTransaction(destination));
                        return;
                    }
                }

                if (_destOngoingTrans.ContainsKey(destination))
                {
                    if (!_destOngoingTrans[destination].IsCompleted) return;
                    _destOngoingTrans.Remove(destination);
                }

                _destLastTxnTime.Remove(destination);
                _destLastTxnTime.Add(destination, now);
                var t = AttemptNewTransaction(destination);
                _destOngoingTrans.Add(destination, t);
            }
        }

        private async Task AttemptNewTransaction(string destination)
        {
            bool retry = false;
            Transaction currentTransaction = default(Transaction); // To make it happy a

            // Once a transaction is popped, it is sealed and can no longer hold events.
            while (retry || TryPopTransaction(destination, out currentTransaction))
            {
                retry = false;

                using (WorkerMetrics.TransactionDurationTimer())
                {
                    try
                    {
                        WorkerMetrics.IncOngoingTransactions();

                        try
                        {
                            await _client.SendTransaction(currentTransaction);
                            WorkerMetrics.IncTransactionsSent(true, destination);
                            ClearDeviceMessages(currentTransaction);
                        }
                        finally
                        {
                            WorkerMetrics.DecOngoingTransactions();
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Warning("Transaction {txnId} {destination} failed: {message}",
                                    currentTransaction.TxnId, destination, ex.Message);

                        WorkerMetrics.IncTransactionsSent(false, destination);

                        var isDown = _backoff.MarkHostIfDown(destination, ex);

                        if (isDown)
                        {
                            lock (_destPendingTransactions)
                            {
                                log.Warning("{destination} marked as DOWN", destination);
                                _destPendingTransactions[destination].Clear();
                                return;
                            }
                        }

                        var ts = _backoff.GetBackoffForException(destination, ex);

                        // Some transactions cannot be retried.
                        if (ts != TimeSpan.Zero)
                        {
                            log.Information("Retrying txn {txnId} in {secs}s",
                                            currentTransaction.TxnId, ts.TotalSeconds);

                            await Task.Delay((int) ts.TotalMilliseconds);
                            retry = true;
                            continue;
                        }

                        Log.Warning("NOT retrying {txnId} for {destination}", currentTransaction.TxnId, destination);
                    }
                }

                if (_backoff.ClearBackoff(destination))
                    log.Information("{destination} has come back online", destination);

                WorkerMetrics.IncTransactionEventsSent("pdu", destination, currentTransaction.pdus.Count);
                WorkerMetrics.IncTransactionEventsSent("edu", destination, currentTransaction.edus.Count);
            }
        }

        private void ClearDeviceMessages(Transaction transaction)
        {
            var deviceMsgs = transaction.edus.Where(m => m.edu_type == "m.direct_to_device").ToList()
                                        .ConvertAll(m => m.StreamId);

            var deviceLists = transaction.edus.Where(m => m.edu_type == "m.device_list_update").ToList()
                                         .ConvertAll(m => Tuple.Create(m.StreamId, (string) m.content["user_id"]));

            if (!deviceLists.Any() && !deviceMsgs.Any())
            {
                // Optimise early.
                return;
            }

            using (var db = new SynapseDbContext(_connString))
            {
                if (deviceMsgs.Count != 0)
                {
                    _destLastDeviceMsgStreamId[transaction.Destination] = deviceMsgs.Max();
                    var deviceMsgEntries = db.DeviceFederationOutboxes.Where(m => deviceMsgs.Contains(m.StreamId));

                    if (deviceMsgEntries.Any())
                    {
                        db.DeviceFederationOutboxes.RemoveRange(deviceMsgEntries);
                        db.SaveChanges();
                    }
                    else
                    {
                        log.Warning("No messages to delete in outbox, despite sending messages in this txn");
                    }
                }

                if (deviceLists.Count == 0) return;

                _destLastDeviceListStreamId[transaction.Destination] = deviceLists.Max(e => e.Item1);

                var deviceListEntries = db.DeviceListsOutboundPokes
                                          .Where(m =>
                                                     deviceLists.FindIndex(e => e.Item1 == m.StreamId &&
                                                                                e.Item2 == m.UserId) >= 0);

                if (deviceListEntries.Any())
                {
                    foreach (var msg in deviceListEntries) msg.Sent = true;

                    db.SaveChanges();
                }
                else
                {
                    log.Warning("No device lists to mark as sent, despite sending lists in this txn");
                }
            }
        }

        private bool IsMineId(string id)
        {
            return id.Split(":")[1] == _serverName;
        }

        private JObject FormatPresenceContent(PresenceState state)
        {
            var now = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var obj = new JObject();
            obj.Add("presence", state.state);
            obj.Add("user_id", state.user_id);

            if (state.last_active_ts != 0) obj.Add("last_active_ago", (long) Math.Round(now - state.last_active_ts));

            if (state.status_msg != null && state.state != "offline") obj.Add("status_msg", state.status_msg);

            if (state.state == "online") obj.Add("currently_active", state.currently_active);

            return obj;
        }

        /// <summary>
        ///     Get a set of remote hosts interested in this presence.
        /// </summary>
        private Dictionary<string[], PresenceState> GetInterestedRemotes(List<PresenceState> presenceSet)
        {
            var dict = new Dictionary<string[], PresenceState>();

            using (WorkerMetrics.FunctionTimer("GetInterestedRemotes"))
            {
                // Get the list of rooms shared by these users.
                // We are intentionally skipping presence lists here.
                foreach (var presence in presenceSet)
                {
                    var hosts = new HashSet<string>();

                    foreach (var room in _roomCache.GetJoinedRoomsForUser(presence.user_id))
                    {
                        if (room.Hosts.Length > MaxHostsForPresence)
                        {
                            // Don't include rooms with losts of hosts, because it slows shit down.
                            // TODO: This is not very nice, but things like HQ exist. Fundamentally this
                            // is a bug with presence, and we should really fix presence. This is the middle ground
                            // between turning it all off, and rampant presence.
                            continue;
                        }
    
                        hosts.UnionWith(room.Hosts);
                    }

                    // Never include ourselves
                    hosts.Remove(_serverName);
                    // Don't include dead hosts.
                    hosts.RemoveWhere(_backoff.HostIsDown);
                    // Now get the hosts for that room.
                    dict.Add(hosts.ToArray(), presence);
                }

                return dict;
            }
        }

        private HashSet<string> GetHostsInRoom(string roomId)
        {
            using (WorkerMetrics.FunctionTimer("GetHostsInRoom"))
            {
                var hosts = new HashSet<string>(_roomCache.GetRoom(roomId).Hosts);
                // Never include ourselves
                hosts.Remove(_serverName);
                // Don't include dead hosts.
                hosts.RemoveWhere(_backoff.HostIsDown);
                return hosts;
            }
        }

        private long GetTs()
        {
            return (long) (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }

        private Transaction GetOrCreateTransactionForDest(string dest)
        {
            LinkedList<Transaction> list;

            if (!_destPendingTransactions.ContainsKey(dest))
            {
                list = new LinkedList<Transaction>();

                if (!_destPendingTransactions.TryAdd(dest, list))
                {
                    list = _destPendingTransactions[dest];
                }
            }
            else
            {
                list = _destPendingTransactions[dest];
            }

            if (list.Count > 0)
            {
                var value = list.Last.Value;

                // If there is still room in the transaction.
                if (value.pdus.Count < MAX_PDUS_PER_TRANSACTION && value.edus.Count < MAX_EDUS_PER_TRANSACTION)
                {
                    return value;
                }

                log.Debug("{host} has gone over a PDU/EDU transaction limit, creating a new transaction", dest);
            }
            
            _txnId++;

            var transaction = new Transaction
            {
                edus = new List<EduEvent>(),
                pdus = new List<IPduEvent>(),
                origin = _serverName,
                origin_server_ts = GetTs(),
                TxnId = _txnId.ToString(),
                Destination = dest
            };

            list.AddLast(transaction);

            return transaction;
        }

        private bool TryPopTransaction(string dest, out Transaction t)
        {
            if (_destPendingTransactions.ContainsKey(dest) && _destPendingTransactions[dest].Count > 0)
            {
                t = _destPendingTransactions[dest].First();
                _destPendingTransactions[dest].RemoveFirst();
                return true;
            }

            t = default(Transaction);
            return false;
        }
    }
}
