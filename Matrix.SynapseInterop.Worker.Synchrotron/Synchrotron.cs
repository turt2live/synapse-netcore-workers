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
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class Synchrotron
    {
        private static readonly TimeSpan MAX_TIMEOUT = TimeSpan.FromMinutes(3);
        private ILogger _log = Log.ForContext<Synchrotron>();
        private SynapseReplication _synapseReplication;
        private CachedMatrixRoomSet _roomSet;
        private ReplicationStream<EventStreamRow> _eventStream;
        private AutoResetEvent onNewEvent;

        private ConcurrentDictionary<string, Task<SyncResponse>> _ongoingSyncs;
        // Ongoing Syncs.

        public Synchrotron()
        {
            _ongoingSyncs = new ConcurrentDictionary<string, Task<SyncResponse>>();
            onNewEvent = new AutoResetEvent(false);
        }

        public void Start(IConfiguration _config)
        {
            _log.Information("Starting Synchrotron");
            _roomSet = new CachedMatrixRoomSet();

            _synapseReplication = new SynapseReplication();
            _synapseReplication.ClientName = "NetCoreSynchrotron";

            var synapseConfig = _config.GetSection("Synapse");
            var host = synapseConfig.GetValue<string>("replicationHost");
            var port = synapseConfig.GetValue<int>("replicationPort");

            _synapseReplication.Connect(host, port).Wait();

            _eventStream = _synapseReplication.BindStream<EventStreamRow>();
            _eventStream.DataRow += EventStreamOnDataRow;
            _eventStream.PositionUpdate += EventStreamOnPositionUpdate;
        }

        public Task<SyncResponse> BuildSyncResponse(User user, string since, TimeSpan timeout)
        {
            _log.Information("Building sync for {user_id} {since}", user.Name, since);

            TimeSpan waitTime = timeout != TimeSpan.Zero ? timeout : MAX_TIMEOUT;
            Task<SyncResponse> ongoingSync;
            string syncKey = GetSyncKey(user, since);
            // TODO: Support filter
            SyncFilter filter = new SyncFilter();
            
            if (!_ongoingSyncs.TryGetValue(syncKey, out ongoingSync))
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
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception)
            {
                _ongoingSyncs.Remove(syncKey, out _);
                ongoingSync.Dispose();
                throw;
            }

            if (!_ongoingSyncs.TryRemove(syncKey, out _))
            {
                _log.Warning("Failed to remove sync from ongoingSyncs after completion");
            }

            return ongoingSync;
        }

        private async Task<SyncResponse> RunInitialSync(User user, SyncFilter filter)
        {
            using (var t = WorkerMetrics.FunctionTimer("RunInitialSync"))
            {
                // Get all rooms the user is joined/invited/leave to.
                SyncResponse response = new SyncResponse();
                List<RoomMembership> members;

                using (var db = new SynapseDbContext())
                {
                    members = db.GetMembershipForUser(user.Name).ToList();

                    _log.Information("Syncing {room_count} rooms", members.Count);
                
                    // Fetch a bunch of events from the db before we go any further.
                    _roomSet.FetchLatestEventsForRooms(members.Select(m => m.RoomId), filter.EventsToFetch);
                    members.Select(m => m.RoomId).ForEach(r => _roomSet.GetRoom(r).PopulateStateCache());
                    var roomTasks = members.Select((membership => InitalSyncRoom(membership, filter, response)));

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

        private async Task InitalSyncRoom(RoomMembership rMember, SyncFilter filter, SyncResponse response)
        {
            _log.Debug("InitalSyncRoom {room_id}", rMember.RoomId);

            using (var db = new SynapseDbContext())
            {
                // This would have been cached earlier
                var room = _roomSet.GetRoom(rMember.RoomId);
                List<RoomAccountData> accountData = null;
                IEnumerable<EventJsonSet> latestEvents = null;
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
                    latestEvents = room.GetLatestEvents(filter.EventsToFetch);
                    currentState = room.GetCurrentState;

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
                    latestEvents = room.GetPreviousEvents(rMember.EventId, filter.EventsToFetch);
                    currentState = room.GetStateAtEvent(rMember.EventId);
                }

                try
                {
                    await response.BuildRoomResponse(rMember.RoomId,
                                                     rMember.Membership,
                                                     latestEvents,
                                                     currentState,
                                                     accountData);
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
            using (var t = WorkerMetrics.FunctionTimer("RunSync"))
            {
                using (var db = new SynapseDbContext())
                {
                    SyncResponse response = new SyncResponse();
                    response.MaxEventStreamId = tokens[0];
                    response.MaxAccountDataId = tokens[1];

                    while (response.Empty)
                    {
                        var rooms = db.GetMembershipForUser(user.Name).Where((e) => e.Membership == "join").Select((m) => m.RoomId).ToHashSet();
                        // Get all rooms the user is joined/invited/leave to.
                        var sinceEvents = tokens[0];
                        var toEvents = int.Parse(_eventStream.CurrentPosition);

                        foreach (var ev in db.GetAllNewEventsStream(sinceEvents, toEvents).GroupBy(ev => ev.RoomId))
                        {
                            if (rooms.Contains(ev.Key))
                            {
                                await response.BuildRoomResponse(ev.Key, "join", ev, new EventJsonSet[0], new List<RoomAccountData>());
                            }
                        }

                        if (response.Empty)
                        {
                            _log.Debug("Sync is empty, waiting for another event");
                            // We need to retry again.
                            onNewEvent.WaitOne();
                        }
                        else
                        {
                            _log.Debug("Incremental sync has events, replying to client");
                        }
                    }

                    _log.Information("Synced all rooms for {user_id} {time}ms", user.Name, t.ObserveDuration().TotalMilliseconds);
                    response.Finish();
                    return response;
                }
            }
        }

        private void EventStreamOnDataRow(object sender, EventStreamRow e)
        {
            // Fetch event in advance.
            _roomSet.GetRoom(e.RoomId, false).GetEvent(e.EventId);
        }

        private void EventStreamOnPositionUpdate(object sender, string e)
        {
            _log.Information("Got event position update");
            onNewEvent.Set();
        }

        private string GetSyncKey(User user, string since)
        {
            return "{user.Name}:{since ?? \"INITIAL\"}";
        }
    }
}
