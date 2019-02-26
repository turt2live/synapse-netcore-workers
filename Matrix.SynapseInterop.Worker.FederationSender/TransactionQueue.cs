using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        public TransactionQueue(string serverName, string connectionString, SigningKey key)
        {
            client = new FederationClient(serverName, key);
            userPresence = new Dictionary<string, PresenceState>();
            destOngoingTrans = new Dictionary<string, Task>();
            destPendingTransactions = new Dictionary<string, Transaction>();
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
            
            Transaction transaction = getOrCreateTransactionForDest(obj.destination);

            transaction.edus.Add(obj);
            AttemptTransaction(obj.destination);
        }

        public void SendEdu(EduEvent ev, string key)
        {
            // TODO: We need to only send one event at a time with the same key.
            // but here we are ignoring it, because demo.
            SendEdu(ev);
        }

        public void SendDeviceMessages(string destination)
        {
            
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
            Console.WriteLine("Processing new PDU events..");
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
            var roomEvents = events.GroupBy(e => e.RoomId);
            foreach (var item in roomEvents)
            {
                var hosts = await GetHostsInRoom(item.Key);
                foreach (var roomEvent in item)
                {
                    // TODO: Support send_on_bahalf_of?
                    if (!IsMineId(roomEvent.Sender))
                    {
                        continue;
                    }

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

            destOngoingTrans.Add(destination, AttemptNewTransaction(destination));
        }

        private async Task AttemptNewTransaction(string destination)
        {
            Transaction currentTransaction;
            while (true)
            {
                if (!destPendingTransactions.TryGetValue(destination, out currentTransaction))
                {
                    Console.WriteLine($"No more transactions for {destination}");
                    break;
                }

                destPendingTransactions.Remove(destination);
                try
                {
                    await client.SendTransaction(currentTransaction);
                }
                catch (Exception ex)
                {
                    //TODO: Backoff, retry.
                    Console.WriteLine("Transaction {0} failed: {1}", currentTransaction.transaction_id, ex);
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
                };
                destPendingTransactions.Add(dest, transaction);
            }

            return transaction;
        }
    }
}
