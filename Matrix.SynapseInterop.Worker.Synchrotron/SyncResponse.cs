using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Matrix.SynapseInterop.Replication.DataRows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class SyncResponse
    {
        [JsonIgnore]
        public bool IsReady = false;
        
        [JsonIgnore]
        public bool Empty => _rooms.Empty;
        
        [JsonIgnore]
        public long MaxEventStreamId = 0;
        
        [JsonIgnore]
        public long MaxAccountDataId = 0;
        
        [JsonIgnore]
        public long MaxDeviceId = 0;
        
        [JsonIgnore]
        public long TypingCounter = 0;
        
        [JsonProperty(PropertyName = "next_batch")]
        public string NextBatch;

        [JsonProperty(PropertyName = "rooms")]
        private SyncRoomSet _rooms = new SyncRoomSet();

        [JsonProperty(PropertyName = "presence")]
        private EventList<SyncEvent> _presence = new EventList<SyncEvent> {Events = new List<SyncEvent>()};

        [JsonProperty(PropertyName = "account_data")]
        private EventList<SyncSimpleEvent> _accountData = new EventList<SyncSimpleEvent> {Events = new List<SyncSimpleEvent>()};
        
        [JsonProperty(PropertyName = "to_device")]
        private EventList<SyncEvent> _toDevice = new EventList<SyncEvent> {Events = new List<SyncEvent>()};
        
        //TODO: Support device_lists, device_one_time_keys_count

        class SyncRoomSet
        {
            [JsonProperty(PropertyName = "join")]
            public Dictionary<string, JoinedRoom> Join = new Dictionary<string, JoinedRoom>();
            
            [JsonProperty(PropertyName = "invite")]
            public Dictionary<string, InvitedRoom> Invite = new Dictionary<string, InvitedRoom>();
            
            [JsonProperty(PropertyName = "leave")]
            public Dictionary<string, SyncRoom> Leave = new Dictionary<string, SyncRoom>();

            [JsonIgnore]
            public bool Empty => Join.Count + Invite.Count + Leave.Count == 0;
        }

        interface ISyncRoom { }

        class SyncRoom : ISyncRoom
        {
            [JsonProperty(PropertyName = "state")]
            public EventList<SyncStateEvent> State;

            [JsonProperty(PropertyName = "timeline")]
            public SyncTimeline Timeline = new SyncTimeline();

            [JsonProperty(PropertyName = "account_data")]
            public EventList<SyncSimpleEvent> AccountData;
        }

        struct EventList<T>
        {
            [JsonProperty(PropertyName = "events")]
            public List<T> Events;
        }

        public class SyncSimpleEvent
        {
            [JsonProperty(PropertyName = "type")]
            public string Type;
            
            [JsonProperty(PropertyName = "content")]
            public JObject Content;
        }
        
        public class SyncEvent: SyncSimpleEvent
        {
            [JsonProperty(PropertyName = "sender")]
            public string Sender;
        }

        public class SyncRoomEvent : SyncEvent
        {
            [JsonProperty(PropertyName = "event_id")]
            public string EventId;
            
            [JsonProperty(PropertyName = "origin_server_ts")]
            public long OriginServerTs;
            
            [JsonProperty(PropertyName = "unsigned")]
            public JObject Unsigned { get; set; }
        }

        public class SyncStateEvent : SyncRoomEvent
        {
            [JsonProperty(PropertyName = "state_key", NullValueHandling = NullValueHandling.Ignore)]
            public string StateKey;
            
            [JsonProperty(PropertyName = "prev_content", NullValueHandling = NullValueHandling.Ignore)]
            public JObject PrevContent;
        }

        class StrippedState : SyncEvent
        {
            [JsonProperty(PropertyName = "state_key")]
            public string StateKey;
        }

        class JoinedRoom : SyncRoom
        {
            [JsonProperty(PropertyName = "ephemeral")]
            public EventList<SyncSimpleEvent> Ephemeral = new EventList<SyncSimpleEvent> {Events = new List<SyncSimpleEvent>()};

            [JsonProperty(PropertyName = "unread_notifications")]
            public UnreadNotificationsCount UnreadNotifications;
        }

        class InvitedRoom : ISyncRoom
        {
            [JsonProperty(PropertyName = "invite_state")]
            public InvitedRoomState InvitedRoomState;
        }

        class InvitedRoomState
        {
            [JsonProperty(PropertyName = "events")]
            public List<StrippedState> Events = new List<StrippedState>();
        }

        class SyncTimeline
        {
            [JsonProperty(PropertyName = "events")]
            public List<SyncStateEvent> Events = new List<SyncStateEvent>();
            
            [JsonProperty(PropertyName = "limited")]
            public Boolean Limited;
            
            [JsonProperty(PropertyName = "prev_batch")]
            public string PrevBatch;
        }

        struct UnreadNotificationsCount
        {
            [JsonProperty(PropertyName = "highlight_count")]
            public int HighlightCount;
            
            [JsonProperty(PropertyName = "notification_count")]
            public int NotificationCount;
        }

        public void Finish()
        {
            var key = $"s{MaxEventStreamId},{MaxAccountDataId},{TypingCounter},{MaxDeviceId}";
            NextBatch = key;
            IsReady = true;
        }

        public static int[] ParseSinceToken(string since)
        {
            var t = since.Substring("s".Length).Split(",").Select(int.Parse).ToArray();
            var tokens = new int[4];
            t.CopyTo(tokens, 0);
            return tokens;
        }

        public void AddAccountData(AccountData data)
        {
            if (data.StreamId > MaxAccountDataId)
            {
                MaxAccountDataId = data.StreamId;
            }

            _accountData.Events.Add(new SyncSimpleEvent
            {
                Content = JObject.Parse(data.Content),
                Type = data.Type,
            });
        }

        private ISyncRoom GetRoomForMembership(string membership, string roomId)
        {
            lock (this)
            {
                ISyncRoom room;
                
                if (membership == "join")
                {
                    if (_rooms.Join.TryGetValue(roomId, out var joinedRoom))
                    {
                        return joinedRoom;
                    }
                
                    room = new JoinedRoom
                    {
                        State = new EventList<SyncStateEvent>
                        {
                            Events = new List<SyncStateEvent>()
                        }
                    };

                    _rooms.Join.Add(roomId, (JoinedRoom) room);
                }
                else if (membership == "leave")
                {
                    if (_rooms.Leave.TryGetValue(roomId, out var leftRoom))
                    {
                        return leftRoom;
                    }
                
                    room = new SyncRoom
                    {
                        State = new EventList<SyncStateEvent>
                        {
                            Events = new List<SyncStateEvent>()
                        }
                    };

                    _rooms.Leave.Add(roomId, (SyncRoom) room);
                    return room;
                }
                else if (membership == "invite")
                {
                    if (_rooms.Invite.TryGetValue(roomId, out var inviteRoom))
                    {
                        return inviteRoom;
                    }

                    room = new InvitedRoom
                    {
                        InvitedRoomState = new InvitedRoomState(),
                    };

                    _rooms.Invite.Add(roomId, (InvitedRoom) room);
                    return room;
                }
                else
                {
                    throw new Exception("Unknown membership");
                }
                
                return room;
            }
        }
        
        public async Task BuildRoomResponse(string roomId, string membership,
                                            IEnumerable<EventJsonSet> getLatestEvents,
                                            IEnumerable<EventJsonSet> roomGetCurrentState,
                                            List<RoomAccountData> roomAccountData)
        {
            if (membership == "ban")
            {
                // Treat bans and leaves the same.
                membership = "leave";
            }
            
            var room = GetRoomForMembership(membership, roomId);

            if (membership == "join" || membership == "leave")
            {
                SyncRoom syncRoom = (SyncRoom) room;
                
                foreach (var ev in roomGetCurrentState)
                {
                    var content = await ev.GetContent();
                    FormatUnsigned(content["unsigned"] as JObject);

                    SyncStateEvent syncEv = new SyncStateEvent
                    {
                        PrevContent = null,
                        StateKey = content["state_key"].Value<string>() ?? "",
                        Content = content["content"] as JObject,
                        EventId = ev.EventId,
                        Unsigned = content["unsigned"] as JObject,
                        OriginServerTs = content["origin_server_ts"].Value<long>(),
                        Sender = ev.Sender,
                        Type = ev.Type
                    };

                    syncRoom.State.Events.Add(syncEv);
                }

                foreach (var ev in getLatestEvents)
                {
                    var content = await ev.GetContent();
                    FormatUnsigned(content["unsigned"] as JObject);

                    var syncEv = new SyncStateEvent
                    {
                        EventId = ev.EventId,
                        Sender = ev.Sender,
                        Content = content["content"] as JObject,
                        Type = ev.Type,
                        Unsigned = content["unsigned"] as JObject,
                        OriginServerTs = content["origin_server_ts"].Value<long>()
                    };

                    if (content.ContainsKey("state_key"))
                    {
                        syncEv.StateKey = content["state_key"].Value<string>() ?? "";
                    }
                    
                    // TODO: prev_content - How do we get this?
                    syncRoom.Timeline.Events.Add(syncEv);

                    if (ev.StreamOrdering > MaxEventStreamId)
                    {
                        MaxEventStreamId = ev.StreamOrdering;
                    }
                }

                if (roomAccountData == null)
                {
                    return;
                }

                var accountData = roomAccountData.OrderByDescending(rad => rad.StreamId).FirstOrDefault();

                if (accountData != null)
                {
                    MaxAccountDataId = accountData.StreamId;
                }

                syncRoom.AccountData.Events = roomAccountData
                                             .Select(e =>
                                                         new SyncSimpleEvent
                                                         {
                                                             Content = JObject.Parse(e.Content),
                                                             Type = e.Type,
                                                         }).ToList();
            }
            else if (membership == "invite")
            {
                //TODO: Support invites
                var inviteRoom = (InvitedRoom) room;
                
                foreach (var ev in roomGetCurrentState)
                {
                    var content = await ev.GetContent();
                    FormatUnsigned(content["unsigned"] as JObject);

                    StrippedState syncEv = new StrippedState
                    {
                        StateKey = content["state_key"].Value<string>() ?? "",
                        Content = content["content"] as JObject,
                        Sender = ev.Sender,
                        Type = ev.Type,
                    };
                    
                    if (ev.StreamOrdering > MaxEventStreamId)
                    {
                        MaxEventStreamId = ev.StreamOrdering;
                    }

                    inviteRoom.InvitedRoomState.Events.Add(syncEv);
                }
            }
        }

        public void SetNotifCount(string roomId, EventPushSummary summary)
        {
            JoinedRoom room = (JoinedRoom) GetRoomForMembership("join", roomId);

            room.UnreadNotifications = new UnreadNotificationsCount
            {
                NotificationCount = summary?.NotifCount ?? 0,
                HighlightCount = 0 // TODO: Support highlight_count
            };
        }

        public void AddTyping(string roomId, IEnumerable<string> userIds)
        {
            JoinedRoom room = (JoinedRoom) GetRoomForMembership("join", roomId) as JoinedRoom;

            room.Ephemeral.Events.Add(new SyncSimpleEvent
            {
                Type = "m.typing",
                Content = JObject.FromObject(new { user_ids = userIds}),
            });
        }

        public void AddPresence(PresenceStreamRow presenceRow)
        {
            var lastActiveAgo = DateTime.Now - DateTimeOffset.FromUnixTimeMilliseconds(presenceRow.LastActiveTs);
            _presence.Events.Add(new SyncEvent
            {
                Sender = presenceRow.UserId,
                Content = JObject.FromObject(new
                {
                    last_active_ago = (long)lastActiveAgo.TotalMilliseconds,
                    status_msg = presenceRow.StatusMsg,
                    presence = presenceRow.State
                }),
                Type = "m.presence"
            });
        }

        private static void FormatUnsigned(JObject unsignedData)
        {
            if (unsignedData.TryGetValue("age_ts", out var age))
            {
                unsignedData.Remove("age_ts");
                unsignedData["age"] = age;
            }
        }
    }
}
