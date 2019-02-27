using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.Models;
using Matrix.SynapseInterop.Replication.Structures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class TransactionQueue
    {
        private const int MAX_PDUS_PER_TRANSACTION = 50;
        private const int MAX_EDUS_PER_TRANSACTION = 100;
        private const int MAX_BACKOFF_SECS = 60*60*24;
        private FederationClient client;
        private Dictionary<string, PresenceState> userPresence;
        private Task presenceProcessing;
        private Task eventsProcessing;
        private Dictionary<string, Task> destOngoingTrans;
        private Dictionary<string, Transaction> destPendingTransactions;
        private SigningKey signingKey;
        private string serverName;
        private string connString;
        private int txnId;
        private int lastEventPoke;

        private Dictionary<string, long> destLastDeviceMsgStreamId;
        private Dictionary<string, long> destLastDeviceListStreamId;

        public TransactionQueue(string serverName, string connectionString, SigningKey key)
        {
            client = new FederationClient(serverName, key);
            userPresence = new Dictionary<string, PresenceState>();
            destOngoingTrans = new Dictionary<string, Task>();
            destPendingTransactions = new Dictionary<string, Transaction>();
            destLastDeviceMsgStreamId = new Dictionary<string, long>();
            destLastDeviceListStreamId = new Dictionary<string, long>();
            presenceProcessing = Task.CompletedTask;
            eventsProcessing = Task.CompletedTask;
            this.serverName = serverName;
            txnId = (int) (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            connString = connectionString;
            lastEventPoke = -1;
            signingKey = key;
        }

        public void OnEventUpdate(string stream_pos)
        {
            lastEventPoke = Math.Max(int.Parse(stream_pos), lastEventPoke);
            if (!eventsProcessing.IsCompleted)
            {
                return;
            }
            Console.WriteLine("Poking ProcessPendingEvents");
            eventsProcessing = ProcessPendingEvents().ContinueWith((t) =>
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
                    if (!userPresence.TryAdd(presence.user_id, presence))
                    {
                        userPresence.Remove(presence.user_id);
                        userPresence.Add(presence.user_id, presence);
                    }
                }
            }

            if (!presenceProcessing.IsCompleted)
            {
                return;
            }
            presenceProcessing = ProcessPendingPresence();
        }
        
        public void SendEdu(EduEvent obj)
        {
            if (obj.destination == serverName)
            {
                return;
            }
            
            // Prod device messages if we've not seen this destination before.
            if (!destLastDeviceMsgStreamId.ContainsKey(obj.destination))
            {
                SendDeviceMessages(obj.destination);
            }
            
            Transaction transaction = getOrCreateTransactionForDest(obj.destination);

            transaction.edus.Add(obj);
            AttemptTransaction(obj.destination);
        }

        public void SendEdu(EduEvent ev, string key)
        {
            Transaction transaction = getOrCreateTransactionForDest(ev.destination);
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
            if (serverName == destination)
            {
                return; // Obviously.
            }
            // Fetch messages for destination
            var messages = GetNewDeviceMessages(destination);
            if (messages.Item1.Count == 0)
            {
                return;
            }
            var transaction = getOrCreateTransactionForDest(destination);
            messages.Item1.ForEach(message =>
            {
                transaction.edus.Add(new EduEvent
                {
                    destination = destination,
                    content = JObject.Parse(message.MessagesJson),
                    edu_type = "m.direct_to_device",
                    origin = serverName,
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
                    origin = serverName,
                    StreamId = list.stream_id,
                });
            });
            AttemptTransaction(destination);
        }

        private Tuple<List<DeviceFederationOutbox>, List<DeviceContentSet>> GetNewDeviceMessages(string destination)
        {
            long lastMsgId = destLastDeviceMsgStreamId.GetValueOrDefault(destination, 0);
            long lastListId = destLastDeviceListStreamId.GetValueOrDefault(destination, 0);
            using (var db = new SynapseDbContext(connString))
            {
                var messages = db.DeviceFederationOutboxes.Where(message =>
                    message.Destination == destination && message.StreamId > lastMsgId
                ).OrderBy(message => message.StreamId).Take(MAX_EDUS_PER_TRANSACTION).ToList();

                var resLists = db.GetNewDevicesForDestination(destination, MAX_EDUS_PER_TRANSACTION);
                return Tuple.Create(messages, resLists);
            }

        }

        private async Task ProcessPendingPresence()
        {
            var presenceSet = userPresence.Values.ToList();
            userPresence.Clear();
            var hostsAndState = await GetInterestedRemotes(presenceSet);
            foreach (var hostState in hostsAndState)
            {
                var formattedPresence = FormatPresenceContent(hostState.Value);
                foreach (var host in hostState.Key)
                {
                    Console.WriteLine($"Sending presence to {host}");
                    // TODO: Handle case where we have over 100 EDUs
                    Transaction transaction = getOrCreateTransactionForDest(host);

                    transaction.edus.Add(new EduEvent
                    {
                        destination = host,
                        origin = serverName,
                        edu_type = "m.presence",
                        content = formattedPresence,
                    });
                    AttemptTransaction(host);
                }
            }
            
        }

        private async Task ProcessPendingEvents()
        {
            List<EventJsonSet> events;
            int top = lastEventPoke;
            int last;
            // Get the set of events we need to process.
            using (var db = new SynapseDbContext(connString))
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
                    if (roomEvent.Version <= 2)
                    {
                        pduEv = new PduEventV1()
                        {
                            event_id = roomEvent.EventId
                        };
                    } else
                    {
                        pduEv = new PduEventV3();
                    }

                    pduEv.content = roomEvent.Content["content"] as JObject;
                    pduEv.origin = serverName;
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
                    pduEv.type = (string)roomEvent.Content["type"];
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
                        Transaction transaction = getOrCreateTransactionForDest(host);
                        transaction.pdus.Add(pduEv);
                        AttemptTransaction(host);
                    }
                }
            }

            using (var db = new SynapseDbContext(connString))
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
            if (destOngoingTrans.ContainsKey(destination))
            {
                if (!destOngoingTrans[destination].IsCompleted)
                {
                    // Already ongoing.
                    return;
                }
                destOngoingTrans.Remove(destination);
            }
            // This might race, it's okay to fail.
            destOngoingTrans.TryAdd(destination, AttemptNewTransaction(destination));
        }

        private async Task AttemptNewTransaction(string destination)
        {
            Transaction currentTransaction;
            if (!destPendingTransactions.TryGetValue(destination, out currentTransaction))
            {
                Console.WriteLine($"No more transactions for {destination}");
                return;
            }
            while (true)
            {
                using (WorkerMetrics.TransactionDurationTimer(destination))
                {
                    destPendingTransactions.Remove(destination);
                    try
                    {
                        await client.SendTransaction(currentTransaction);
                        WorkerMetrics.IncTransactionsSent(true, destination);
                    }
                    catch (Exception ex)
                    {
                        destPendingTransactions.Add(destination, currentTransaction);
                        currentTransaction.BackoffSecs *= 2;
                        Console.WriteLine("Transaction {0} failed: {1}. Backing off for {2}secs", currentTransaction.transaction_id, ex, currentTransaction.BackoffSecs);
                        WorkerMetrics.IncTransactionsSent(false, destination);
                        await Task.Delay(currentTransaction.BackoffSecs * 1000);
                        continue;
                    }
                    
                    // Remove any device messages 
                    ClearDeviceMessages(currentTransaction);
                }
                if (!destPendingTransactions.TryGetValue(destination, out currentTransaction))
                {
                    Console.WriteLine($"No more transactions for {destination}");
                    break;
                }
            }

        }

        private void ClearDeviceMessages(Transaction transaction)
        {
            var deviceMsgs = transaction.edus.Where(m => m.edu_type == "m.direct_to_device").ToList().ConvertAll(m => m.StreamId);
            var deviceLists = transaction.edus.Where(m => m.edu_type == "m.device_list_update").ToList()
                .ConvertAll(m => Tuple.Create(m.StreamId, (string)m.content["user_id"]));
            using (var db = new SynapseDbContext(connString))
            {
                if (deviceMsgs.Count != 0)
                {
                    destLastDeviceMsgStreamId[transaction.destination] = deviceMsgs.Max();
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

                destLastDeviceListStreamId[transaction.destination] = deviceLists.Max(e => e.Item1);
                var deviceListEntries = db.DeviceListsOutboundPokes
                    .Where(m => 
                        deviceLists.FindIndex(e => e.Item1 == m.StreamId && e.Item2 == m.UserId) >= 0);
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
            return id.Split(":")[1] == serverName;
        }

        private JObject FormatPresenceContent(PresenceState state)
        {
            var now = (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var obj = new JObject();
            obj.Add("presence", state.state);
            obj.Add("user_id", state.user_id);
            if (state.last_active_ts != 0)
            {
                obj.Add("last_active_ago", now - state.last_active_ts);
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
            using (var db = new SynapseDbContext(connString))
            {
                // Get the list of rooms shared by these users.
                // We are intentionally skipping presence lists here.
                foreach (var presence in presenceSet)
                {
                    var membershipList = await db.RoomMemberships.Where(m =>
                        m.Membership == "join" &&
                        m.UserId == presence.user_id).ToListAsync();
                    var hosts = new HashSet<string>();
                    // XXX: This is NOT the way to do this, but functions well enough
                    // for a demo.
                    foreach (var roomId in membershipList.ConvertAll(m => m.RoomId))
                    {
                        await db.RoomMemberships.Where(m => m.RoomId == roomId).ForEachAsync((m) =>
                            hosts.Add(m.UserId.Split(":")[1])
                        );
                    }
                    // Never include ourselves
                    hosts.Remove(serverName);
                    // Now get the hosts for that room.
                    dict.Add(hosts.ToArray() ,presence);
                }
            }
            return dict;
        }

        private async Task<HashSet<string>> GetHostsInRoom(string roomId)
        {
            HashSet<string> hosts = new HashSet<string>();
            using (var db = new SynapseDbContext(connString))
            {
                await db.RoomMemberships.Where(m => m.RoomId == roomId).ForEachAsync((m) =>
                    hosts.Add(m.UserId.Split(":")[1])
                );
            }
            hosts.Remove(serverName);
            return hosts;
        }

        private long getTs()
        {
            return (long) (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }

        private Transaction getOrCreateTransactionForDest(string dest)
        {
            if (!destPendingTransactions.TryGetValue(dest, out var transaction))
            {
                txnId++;
                transaction = new Transaction
                {
                    edus = new List<EduEvent>(),
                    pdus = new List<IPduEvent>(),
                    origin = serverName,
                    origin_server_ts = getTs(),
                    transaction_id = txnId.ToString(),
                    destination = dest,
                    BackoffSecs = 5
                };
                destPendingTransactions.Add(dest, transaction);
            }

            return transaction;
        }
    }
}
