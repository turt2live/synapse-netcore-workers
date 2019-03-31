using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Common.MatrixUtils;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Core;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class Synchrotron
    {
        private static readonly TimeSpan MAX_TIMEOUT = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan ONLINE_TIMER = TimeSpan.FromSeconds(30);
        private ILogger _log = Log.ForContext<Synchrotron>();
        private SynapseReplication _synapseReplication;
        private CachedMatrixRoomSet _roomSet;
        private ReplicationStream<EventStreamRow> _eventStream;
        private ReplicationStream<TypingStreamRow> _typingStream;
        private ReplicationStream<AccountDataStreamRow> _accountDataStream;
        private ReplicationStream<ToDeviceStreamRow> _toDeviceStream;
        private ReplicationStream<DeviceListsStreamRow> _deviceListStream;
        private ReplicationStream<PresenceStreamRow> _presenceStream;
        private ReplicationStream<ReceiptStreamRow> _receiptStream;
        private readonly AutoResetEvent _onNewEvent;
        private Dictionary<string, HashSet<string>> _roomTypingSet; // roomId -> userIds
        private readonly List<AccountDataStreamRow> _queuedAccountData;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _usersGoingOffline;
        private int TypingCounter = 0;
        private int MaxDeviceId = 0;
        private readonly List<DeviceInboxItem> _deviceInbox;
        private readonly Dictionary<string, List<PresenceStreamRow>> _presenceToSend; // userId -> [presence]
        private readonly Dictionary<string, List<ReceiptStreamRow>> _receiptsToSend; // userId -> [receiept]
        private readonly MessagesHandler _messagesHandler;

        private readonly ConcurrentDictionary<string, Task<SyncResponse>> _ongoingSyncs;
        // Ongoing Syncs.

        public Synchrotron(MessagesHandler messages, CachedMatrixRoomSet roomSet)
        {
            _roomSet = roomSet;
            _queuedAccountData = new List<AccountDataStreamRow>();
            _presenceToSend = new Dictionary<string, List<PresenceStreamRow>>();
            _receiptsToSend = new Dictionary<string, List<ReceiptStreamRow>>();
            _ongoingSyncs = new ConcurrentDictionary<string, Task<SyncResponse>>();
            _usersGoingOffline = new ConcurrentDictionary<string, CancellationTokenSource>();
            _onNewEvent = new AutoResetEvent(false);
            _deviceInbox = new List<DeviceInboxItem>();
            _messagesHandler = messages;
        }

        public void Start(IConfiguration config)
        {
            _log.Information("Starting Synchrotron");
            _roomTypingSet = new Dictionary<string, HashSet<string>>();

            _synapseReplication = new SynapseReplication {ClientName = "NetCoreSynchrotron"};

            var synapseConfig = config.GetSection("Synapse");
            var host = synapseConfig.GetValue<string>("replicationHost");
            var port = synapseConfig.GetValue<int>("replicationPort");

            _synapseReplication.Connect(host, port).Wait();

            _eventStream = _synapseReplication.BindStream<EventStreamRow>();
            _eventStream.DataRow += EventStreamOnDataRow;
            _eventStream.PositionUpdate += StreamOnPositionUpdate<EventStreamRow>;

            _typingStream = _synapseReplication.BindStream<TypingStreamRow>();
            _typingStream.DataRow += TypingStreamOnDataRow;
            _typingStream.PositionUpdate += StreamOnPositionUpdate<TypingStreamRow>;

            _accountDataStream = _synapseReplication.BindStream<AccountDataStreamRow>();
            _accountDataStream.DataRow += AccountDataOnDataRow;
            _accountDataStream.PositionUpdate += StreamOnPositionUpdate<AccountDataStreamRow>;

            _toDeviceStream = _synapseReplication.BindStream<ToDeviceStreamRow>();
            _toDeviceStream.DataRow += ToDeviceOnDataRow;
            _toDeviceStream.PositionUpdate += StreamOnPositionUpdate<ToDeviceStreamRow>;

            _deviceListStream = _synapseReplication.BindStream<DeviceListsStreamRow>();
            _deviceListStream.DataRow += DeviceListOnDataRow;
            _deviceListStream.PositionUpdate += StreamOnPositionUpdate<DeviceListsStreamRow>;

            _presenceStream = _synapseReplication.BindStream<PresenceStreamRow>();
            _presenceStream.DataRow += PresenceStreamOnRow;
            _presenceStream.PositionUpdate += StreamOnPositionUpdate<PresenceStreamRow>;

            _receiptStream = _synapseReplication.BindStream<ReceiptStreamRow>();
            _receiptStream.DataRow += ReceiptStreamOnDataRow;
            _receiptStream.PositionUpdate += StreamOnPositionUpdate<ReceiptStreamRow>;

            using (var db = new SynapseDbContext())
            {
                MaxDeviceId = db.DeviceMaxStreamId.First().StreamId;
            }
        }

        public async Task<RoomInitialSyncResponse> BuildRoomInitialSync(User user, string roomId)
        {
            using (WorkerMetrics.FunctionTimer("BuildRoomInitialSync"))
            {
                var response = new RoomInitialSyncResponse {RoomId = roomId};

                using (var db = new SynapseDbContext())
                {
                    var room = _roomSet.GetRoom(roomId, false);

                    var membership = await db.GetMembershipForUser(user.Name)
                                             .Where((m) => m.RoomId == roomId)
                                             .Select((m) => m.Membership)
                                             .FirstOrDefaultAsync() ?? "leave";

                    response.Membership = membership;

                    response.AccountData =
                        await db.RoomAccountData
                                .Where(rad => rad.UserId == user.Name && rad.RoomId == roomId)
                                .Select(rad => new SyncResponse.SyncSimpleEvent
                                 {
                                     Type = rad.Type,
                                     Content = JObject.Parse(rad.Content),
                                 }).ToListAsync();

                    response.State = db.CrntRoomState
                                       .Where((rS) => rS.RoomId == roomId).AsEnumerable()
                                       .Select((rS) =>
                                        {
                                            var t = room.GetEvent(rS.EventId).GetContent();
                                            t.Wait();
                                            var content = t.Result; 

                                            return new SyncResponse.SyncStateEvent
                                            {
                                                Content = content["content"] as JObject,
                                                EventId = rS.EventId,
                                                OriginServerTs = content["origin_server_ts"].Value<long>(),
                                                Sender = content["sender"].Value<string>(),
                                                Unsigned = content["unsigned"] as JObject ?? new JObject(),
                                                StateKey = rS.StateKey,
                                                Type = rS.Type,
                                                // TODO: prev_content
                                            };
                                        }).ToList();
                }

                return response;
            }
        }

        public Task<SyncResponse> BuildSyncResponse(User user, string since, TimeSpan timeout, SyncFilter filter)
        {
            _log.Information("Building sync for {user_id} {since}", user.Name, since);
            
            TimeSpan waitTime = timeout != TimeSpan.Zero ? timeout : MAX_TIMEOUT;
            string syncKey = GetSyncKey(user, since, filter);
            // TODO: Support filter
            
            if (!UserHasSyncsPending(user))
                MarkComingOnline(user.Name);
            
            if (!_ongoingSyncs.TryGetValue(syncKey, out var ongoingSync))
            {
                if (since == null)
                {
                    // This is an initial sync
                    ongoingSync = RunInitialSync(user, filter);
                }
                else
                {
                    // Parse sync.
                    var tokens = SyncResponse.ParseSinceToken(since);
                    ongoingSync = RunSync(user, filter, tokens);
                }

                if (!_ongoingSyncs.TryAdd(syncKey, ongoingSync))
                {
                    _log.Warning("Failed to add sync to ongoingSyncs.");
                }
            }

            try
            {
                ongoingSync.Wait(waitTime);
            }
            finally
            {
                if (!_ongoingSyncs.TryRemove(syncKey, out _))
                {
                    _log.Warning("Failed to remove sync from ongoingSyncs after completion");
                }

                if (!UserHasSyncsPending(user))
                    MarkGoingOffline(user.Name);
            }

            return ongoingSync;
        }

        private async Task<SyncResponse> RunInitialSync(User user, SyncFilter filter)
        {
            using (var t = WorkerMetrics.FunctionTimer("RunInitialSync"))
            {
                // Get all rooms the user is joined/invited/leave to.
                SyncResponse response = new SyncResponse();
                response.DeviceListId = MaxDeviceId;
                response.TypingCounter = TypingCounter;

                using (var db = new SynapseDbContext())
                {
                    var members = db.GetMembershipForUser(user.Name).ToList();

                    _log.Information("Syncing {room_count} rooms", members.Count);
                
                    // Fetch a bunch of events from the db before we go any further.
                    _roomSet.FetchLatestEventsForRooms(members.Select(m => m.RoomId), filter.EventsToFetch);
                    members.Select(m => m.RoomId).ForEach(r => _roomSet.GetRoom(r).PopulateStateCache());
                    var roomTasks = members.Select((membership => InitialSyncRoom(membership, filter, response)));

                    await db.AccountData.Where(aD => aD.UserId == user.Name)
                            .ForEachAsync(aD => response.AddAccountData(aD));

                    try
                    {
                        await Task.WhenAll(roomTasks);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Failed to build room response: {ex}", ex);
                        throw;
                    }

                    _log.Information("Synced all rooms for {user_id} {time}ms", user.Name, t.ObserveDuration().TotalMilliseconds);
                    response.Finish();
                    return response;
                }
            }
        }

        private async Task InitialSyncRoom(RoomMembership rMember, SyncFilter filter, SyncResponse response)
        {
            _log.Debug("InitalSyncRoom {room_id}", rMember.RoomId);

            using (var db = new SynapseDbContext())
            {
                // This would have been cached earlier
                var room = _roomSet.GetRoom(rMember.RoomId);
                List<RoomAccountData> accountData = null;
                List<EventJsonSet> latestEvents = null;
                IEnumerable<EventJsonSet> currentState = null;

                if (rMember.Membership != "invite")
                {
                    accountData = await db
                                       .RoomAccountData
                                       .Where(rad => rad.UserId == rMember.UserId && rad.RoomId == rMember.RoomId)
                                       .ToListAsync();
                }

                if (rMember.Membership == "join")
                {
                    latestEvents = room.GetLatestEvents(filter.EventsToFetch).ToList();

                    var notifs = await db
                                      .EventPushSummary
                                      .Where(eps => eps.UserId == rMember.UserId && eps.RoomId == rMember.RoomId)
                                      .FirstOrDefaultAsync();

                    response.SetNotifCount(rMember.RoomId, notifs);
                }
                else if (rMember.Membership == "invite")
                {
                    currentState = room.GetStateAtEvent(rMember.EventId);
                }
                else if (rMember.Membership == "leave" || rMember.Membership == "ban" && rMember.Forgotten == 0)
                {
                    // We want to only get events *before* the user left.
                    latestEvents = room.GetEventsAt(rMember.EventId, filter.EventsToFetch, false).ToList();
                }

                var firstEv = latestEvents?.OrderBy(s => s.StreamOrdering).FirstOrDefault()?.EventId;

                if (firstEv != null)
                {
                    // Get the state before the current timeline, not including the first event.
                    currentState = room.GetStateAtEvent(firstEv).SkipWhile((e) => e.EventId == firstEv);
                }

                try
                {
                    var prevtoken = _messagesHandler.GetPrevBatch(latestEvents);
                    
                    await response.BuildRoomResponse(rMember.RoomId,
                                                     rMember.Membership,
                                                     latestEvents,
                                                     currentState,
                                                     accountData,
                                                     prevtoken);
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to build room response {room_id} {ex}", rMember.RoomId, ex);
                    throw;
                }
            }
        }

        private async Task<SyncResponse> RunSync(User user, SyncFilter filter, int[] tokens)
        {
            //FIXME: This function is huuuuge.
            using (var db = new SynapseDbContext())
            {
                SyncResponse response = new SyncResponse
                {
                    MaxEventStreamId = tokens[0],
                    MaxAccountDataId = tokens[1],
                    // The typing counter is not kept between restarts, so ensure that the typing counter of a sync
                    // is never higher than our counter.
                    TypingCounter = Math.Min(tokens[2], TypingCounter),
                    DeviceListId = tokens[3],
                };
                    
                while (response.Empty)
                {
                    // This is a slightly incorrect timer, but it means it won't time the wait block.
                    using (var t = WorkerMetrics.FunctionTimer("RunSync"))
                    {  
                        var membership = db.GetMembershipForUser(user.Name);
                        var joinedRooms = membership.Where(e => e.Membership == "join").Select((m) => m.RoomId).ToHashSet();
                        // Get all rooms the user is joined/invited/leave to.
                        var sinceEvents = tokens[0];
                        var toEvents = int.Parse(_eventStream.CurrentPosition);
                            
                        var pendingAccountData = _queuedAccountData.Where(aD => aD.UserId == user.Name).ToList();
                        var roomAccountData = new Dictionary<string, List<RoomAccountData>>();
    
                        if (pendingAccountData.Count > 0)
                        {
                            foreach (var a in pendingAccountData)
                            {
                                if (a.RoomId == null)
                                {
                                    response.AddAccountData(new AccountData()
                                    {
                                        Type = a.DataType,
                                        Content = a.Data.ToString(),
                                        UserId = a.UserId,
                                    });
                                }
                                else
                                {
                                    if (!roomAccountData.TryGetValue(a.RoomId, out var aDlist))
                                    {
                                        aDlist = new List<RoomAccountData>();
                                        roomAccountData.Add(a.RoomId, aDlist);
                                    }
    
                                    aDlist.Add(new RoomAccountData()
                                    {
                                        Type = a.DataType,
                                        Content = a.Data.ToString(),
                                        UserId = a.UserId,
                                        RoomId = a.RoomId,
                                    });
                                }
    
                                _queuedAccountData.Remove(a);
                            }
                                
                            response.MaxAccountDataId = int.Parse(_accountDataStream.CurrentPosition);
                        }
    
                        List<string> newlyJoinedRooms = new List<string>();
                        List<string> newlyJoinedUsers = new List<string>();
                        List<string> newlyLeftRooms = new List<string>();
                        List<string> newlyLeftUsers = new List<string>();
    
                        foreach (var ev in db.GetAllNewEventsStream(sinceEvents, toEvents).GroupBy(ev => ev.RoomId))
                        {
                            foreach (var member in ev.Where((even => even.Type == "m.room.membership")))
                            {
                                var content = await member.GetContent();
                                var userMembership = content["content"]["membership"].Value<string>();
                                var stateKey = content["state_key"].Value<string>();
                                var isMe = stateKey == user.Name;
    
                                if (userMembership == "join")
                                {
                                    if (isMe)
                                    {
                                        newlyJoinedRooms.Add(member.RoomId);
                                    }
                                    else
                                    {
                                        newlyJoinedUsers.Add(stateKey);
                                    }
                                } else if (userMembership == "leave")
                                {
                                    if (isMe)
                                    {
                                        newlyLeftRooms.Add(member.RoomId);
                                    }
                                    else
                                    {
                                        newlyLeftUsers.Add(stateKey);
                                    }
                                }
                            }
                                
                            // Check to see if we have left/been invited to a room.
                            var currentMembership = membership.FirstOrDefault(m => m.RoomId == ev.Key);
    
                            if (currentMembership == null)
                            {
                                // We aren't in the room, and have never been.
                                continue;
                            }
    
                            IEnumerable<EventJsonSet> fullState = null;
                                
                            if (filter.FullState)
                            {
                                var r = _roomSet.GetRoom(ev.Key);
                                r.PopulateStateCache();
                                fullState = r.GetCurrentState;
                            }
                                
                            if (currentMembership.Membership != "join")
                            {
                                // Current membership is leave/invite/ban, and we have got an event for the room.
                                // If the event for the membership is in the stream, then we can show it.
                                var streamEv = ev.FirstOrDefault(e => e.EventId == currentMembership.EventId);
    
                                if (streamEv == null)
                                {
                                    // Event stream didn't contain the membership event, therefore we ignore this room.
                                    continue;
                                }
    
                                var state = fullState ?? _roomSet.GetRoom(ev.Key, false).GetStateAtEvent(currentMembership.EventId);
    
                                if (currentMembership.Membership == "invite")
                                {
                                    await response.BuildRoomResponse(ev.Key, "invite", null, state, null);
                                }
                                else
                                {
                                    var prevEvents = ev.Where((e) => e.StreamOrdering <= streamEv.StreamOrdering);
                                    await response.BuildRoomResponse(ev.Key, currentMembership.Membership, prevEvents, state, roomAccountData.GetValueOrDefault(ev.Key));
                                    roomAccountData.Remove(ev.Key);
                                }
                            }
                            else
                            {
                                //TODO: State. State is the state content between since <-> start of timeline.
                                var state = fullState ?? new EventJsonSet[0];
                                await response.BuildRoomResponse(ev.Key, "join", ev, state, roomAccountData.GetValueOrDefault(ev.Key), _messagesHandler.GetPrevBatch(ev.ToList()));
                                roomAccountData.Remove(ev.Key);
                            }
                        }
    
                        GetChangedAndLeftUsers(newlyJoinedRooms, newlyJoinedUsers, newlyLeftRooms, newlyJoinedUsers, response, user);
    
                        if (response.TypingCounter < TypingCounter)
                        {
                            // Typing
                            var typingSetSet = _roomTypingSet.Where(r => joinedRooms.Contains(r.Key)).ToArray();
    
                            foreach (var typingSet in typingSetSet)
                            {
                                _log.Debug("Sending typing for {user_id} => {room_id}} containing {count} entries", user.Name, typingSet.Key, typingSet.Value.Count);
                                response.AddTyping(typingSet.Key, typingSet.Value);
                            }
    
                            response.TypingCounter = TypingCounter;
                        }
                            
                        // Presence
                        var presenceSet = _presenceToSend[user.Name].ToArray();
                            
                        foreach (var presence in presenceSet)
                        {
                            response.AddPresence(presence);
                        }
                            
                        _presenceToSend[user.Name].RemoveAll(p => presenceSet.Contains(p));
                            
                        // Receipts
                            
                        var receiptSet = _receiptsToSend[user.Name].ToArray();
                            
                        foreach (var receipt in receiptSet)
                        {
                            response.AddReceipt(receipt);
                        }
                            
                        _receiptsToSend[user.Name].RemoveAll(r => receiptSet.Contains(r));
                            
                        // To Device
    
                        var deviceMsgSet = _deviceInbox.Where((u) => u.UserId == user.Name).ToArray();
    
                        foreach (var msg in deviceMsgSet)
                        {
                            response.AddDeviceMsg(msg);
                        }
                            
                        _deviceInbox.RemoveAll(r => deviceMsgSet.Contains(r));
                        _log.Information("Synced all rooms for {user_id} {time}ms", user.Name, t.ObserveDuration().TotalMilliseconds);
                    }
                        
                    // Tags
                        
                    if (response.Empty)
                    {
                        // FIXME: This is going to cause a thundering herd effect for lots of users on one synchrotron
                        // because we will wake up all pending syncs for any incoming bit of data. The hope is
                        // that with enough caching power, this makes little difference.
                        _log.Debug("Sync is empty, waiting for another event");
                        // We need to retry again.
                        _onNewEvent.WaitOne();
                        _log.Debug("Sync got woken up");
                    }
                    else
                    {
                        // Fetch one time keys if the response will be non-empty.
                        var keys = db.E2EOneTimeKeys
                                     .Where((u) => u.UserId == user.Name && u.DeviceId == filter.DeviceId);

                        response.SetOneTimeKeysCount(keys.Count(u => u.Algorithm == "curve25519"),
                                                     keys.Count(u => u.Algorithm == "signed_curve25519"));

                        _log.Debug("Incremental sync has events, replying to client");
                    }
                }

                response.Finish();
                return response;
            }
        }

        private bool GetChangedAndLeftUsers(List<string> newlyJoinedRooms,
                                            List<string> newlyJoinedUsers,
                                            List<string> newlyLeftRooms,
                                            List<string> newlyLeftUsers,
                                            SyncResponse sync,
                                            User user
        )
        {
            var changed = new HashSet<string>(newlyJoinedUsers);
            var left = new HashSet<string>(newlyLeftUsers);

            using (var db = new SynapseDbContext())
            {
                var getChangedUsers = db.DeviceListsStream.Where(d => d.StreamId > sync.DeviceListId).ToArray();

                foreach (var changedUser in getChangedUsers)
                {
                    // Add any users who have changed their device set and we share a room with them.
                    if (db.UsersWhoShareRooms.Any((u) =>
                                                      (u.OtherUserId == changedUser.UserId && u.UserId == user.Name) ||
                                                      (u.UserId == changedUser.UserId && u.OtherUserId == user.Name)))
                    {
                        changed.Add(changedUser.UserId);
                    }
                }
                
                foreach (var roomId in newlyJoinedRooms)
                {
                    _roomSet.GetRoom(roomId, true).Membership.ForEach(m => changed.Add(m));
                }
            
                foreach (var roomId in newlyLeftRooms)
                {
                    _roomSet.GetRoom(roomId, true).Membership.ForEach(m => left.Add(m));
                }
                
                // Check for "left" that we definitely don't share any other rooms with them.
                left.RemoveWhere((l) => db.UsersWhoShareRooms.Any((u) =>
                                                                      (u.OtherUserId == l && u.UserId == user.Name) ||
                                                                      (u.UserId == l && u.OtherUserId == user.Name)));

                if (getChangedUsers.Length > 0)
                    sync.DeviceListId = getChangedUsers.OrderByDescending(u => u.StreamId).Select(u => u.StreamId).First();
            }
            
            sync.SetDeviceListChanges(changed, left);
            return changed.Count + left.Count > 0;
        }

        private void MarkComingOnline(string userId)
        {
            if (!_usersGoingOffline.TryRemove(userId, out var t))
            {
                _log.Information("{user_id} is coming online", userId);
                
                // Start tracking presence & receipts to send to this user.
                if (!_presenceToSend.ContainsKey(userId))
                    _presenceToSend.Add(userId, new List<PresenceStreamRow>());
                
                if (!_receiptsToSend.ContainsKey(userId))
                    _receiptsToSend.Add(userId, new List<ReceiptStreamRow>());
                
                _synapseReplication.SendUserSync(userId, true, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                return;
            }

            // Cancel offline request.
            t.Cancel();
        }

        private void MarkGoingOffline(string userId)
        {
            if (_usersGoingOffline.ContainsKey(userId))
            {
                return;
            }

            var time = DateTime.Now;
            var cts = new CancellationTokenSource();

            Task.Delay(ONLINE_TIMER, cts.Token).ContinueWith((cwT) =>
            {
                if (cwT.IsCompletedSuccessfully)
                {
                    _log.Information("{user_id} is going offline", userId);
                    // Stop tracking presence to send to this user.
                    // _presenceToSend.Remove(userId);
                    // _receiptsToSend.Remove(userId);
                    _synapseReplication.SendUserSync(userId, false, new DateTimeOffset(time).ToUnixTimeMilliseconds().ToString());
                }
            }, cts.Token);

            _usersGoingOffline.TryAdd(userId, cts);
        }
        
        private void TypingStreamOnDataRow(object sender, TypingStreamRow e)
        {
            // Ensure that we do this sequentually.
            lock (_roomTypingSet)
            {
                _roomTypingSet.Remove(e.RoomId);
                _roomTypingSet.Add(e.RoomId, new HashSet<string>(e.UserIds));
                TypingCounter++;
            }

            _onNewEvent.Set();
        }

        private void EventStreamOnDataRow(object sender, EventStreamRow e)
        {
            // Fetch event in advance.
            _roomSet.GetRoom(e.RoomId, false).GetEvent(e.EventId);

            if (e.EventType == "m.room.member")
            {
                _roomSet.InvalidateRoom(e.RoomId);
            }
            
            _onNewEvent.Set();
        }

        private void AccountDataOnDataRow(object sender, AccountDataStreamRow e)
        {
            _queuedAccountData.Add(e);
            _onNewEvent.Set();
        }
        
        private void DeviceListOnDataRow(object sender, DeviceListsStreamRow e)
        {
            // We aren't interested in the content, but this should check to see if any new devices have appeared for the user.
            _onNewEvent.Set();
        }

        private void ReceiptStreamOnDataRow(object sender, ReceiptStreamRow e)
        {
            _log.Information("Got presence for {user_id} => {room_id}", e.UserId, e.RoomId);
            var modified = false;

            // Determine which users should receive this receipt
            foreach (var room in _roomSet.GetJoinedRoomsForUser(e.UserId, true))
            {
                foreach (var userId in _receiptsToSend.Keys.Where(userId => room.Membership.Contains(userId)))
                {
                    var list = _receiptsToSend[userId];
                    // Remove existing receipts for this user.
                    list.RemoveAll(u => u.UserId == e.UserId && u.RoomId == e.RoomId);
                    list.Add(e);
                    modified = true;
                }
            }

            if (modified)
                _onNewEvent.Set();
        }

        private void PresenceStreamOnRow(object sender, PresenceStreamRow e)
        {
            _log.Information("Got presence for {user_id}", e.UserId);
            var modified = true;
            
            // Determine which users should receive this presence
            _roomSet.GetJoinedRoomsForUser(e.UserId, true).ForEach(r =>
            {
                _presenceToSend.Keys.Where(userId => r.Membership.Contains(userId)).ForEach(userId =>
                {
                    var list = _presenceToSend[userId];
                    // Remove existing presence for this user.
                    list.RemoveAll(u => u.UserId == e.UserId);
                    list.Add(e);
                    modified = false;
                });
            });

            if (!modified)
                _onNewEvent.Set();
        }

        private void ToDeviceOnDataRow(object sender, ToDeviceStreamRow e)
        {
            using (var db = new SynapseDbContext())
            {
                _deviceInbox.AddRange(db.DeviceInbox.Where((i) => i.UserId == e.Entity && i.StreamId > MaxDeviceId));
            }

            MaxDeviceId = int.Parse(_toDeviceStream.CurrentPosition);
            _onNewEvent.Set();
        }

        private void StreamOnPositionUpdate<T>(object sender, string e) where T : IReplicationDataRow
        {
            _log.Information("Got {type} position update", ((ReplicationStream<T>) sender).StreamName);
        }

        private string GetSyncKey(User user, string since, SyncFilter filter)
        {
            return $"{user.Name}:{filter.GetHashCode()}:{since ?? "INITIAL"}";
        }

        private bool UserHasSyncsPending(User user)
        {
            return _ongoingSyncs.Keys.Any((k) => k.StartsWith(user.Name));
        }
    }
}
