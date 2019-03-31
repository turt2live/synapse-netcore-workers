using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common.MatrixUtils;
using Matrix.SynapseInterop.Common.WebResponses;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Newtonsoft.Json.Linq;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public class MessagesHandler
    {
        private CachedMatrixRoomSet _roomSet;

        public MessagesHandler(CachedMatrixRoomSet roomSet)
        {
            this._roomSet = roomSet;
        }

        public async Task<RoomInitialSyncResponse.PaginationChunk> OnRoomMessages(User user, string roomId, string sFrom, string dir, int limit, string sTo = null)
        {
            int from, to;

            try
            {
                from = GetStreamId(sFrom);
                to = sTo != null ? GetStreamId(sTo) : -1;
            }
            catch
            {
                throw new ErrorException("M_UNRECOGNIZED", "Could not decode 'from' or 'to' token", HttpStatusCode.BadRequest);
            }
            
            var forward = dir == "f";

            using (var db = new SynapseDbContext())
            {
                var eStream = forward ? db.Events.OrderBy((s) => s.StreamOrdering) : db.Events.OrderByDescending((s) => s.StreamOrdering);

                var events = eStream
                            .Where(ev => ev.RoomId == roomId &&
                                         (forward ? ev.StreamOrdering <= to : ev.StreamOrdering >= to) &&
                                         (forward ? ev.StreamOrdering > from : ev.StreamOrdering < from)).Take(limit)
                            .Select((ev) => new EventJsonSet(ev, null));

                var response = new RoomInitialSyncResponse.PaginationChunk();
                response.Chunk = new List<SyncResponse.SyncRoomEvent>();

                events = forward ? events.OrderBy((e) => e.StreamOrdering) : events.OrderByDescending((e) => e.StreamOrdering);
                
                // Check if a user can access this event (yeah, ikr).
                var room = _roomSet.GetRoom(roomId);
                var acceptedEvents = new List<EventJsonSet>();

                foreach (var ev in events)
                {
                    if (!await room.CanUserSeeEvent(ev.EventId, user))
                    {
                        continue;
                    }

                    acceptedEvents.Add(ev);

                    response.Chunk.Add(await BuildEvent(ev, room));
                }

                if (acceptedEvents.Count == 0)
                {
                    response.Start = sFrom;
                    response.End = sFrom;
                    return response;
                }
                
                response.Start = GetPrevBatch(acceptedEvents.ToArray(), forward);
                response.End = GetPrevBatch(acceptedEvents.ToArray(), !forward);
                return response;
            }
        }

        private int GetStreamId(string key)
        {
            return int.Parse(key.Substring("x_".Length));
        }

        public string GetPrevBatch(IEnumerable<EventJsonSet> events, bool first = true)
        {
            var streamId = events.OrderBy((e => e.StreamOrdering)).Select(e => e.StreamOrdering);
            var id = first ? streamId.First() : streamId.Last();
            return GetPrevBatch(id, first);
        }
        
        public string GetPrevBatch(int id, bool first = true)
        {
            var k = first ? "f" : "l";
            return $"{k}_{id}";
        }

        public async Task<RoomInitialSyncResponse.PaginationChunk> GetRoomMembers(string roomId, User user, string at, string notMembership)
        {
            var res = new RoomInitialSyncResponse.PaginationChunk {Chunk = new List<SyncResponse.SyncRoomEvent>()};
            var room = _roomSet.GetRoom(roomId, true);

            if (!room.Membership.Contains(user.Name))
            {
                throw new ErrorException("M_FORBIDDEN", "User not in room", HttpStatusCode.Forbidden);
            }
            
            room.PopulateStateCache();
            
            // at is apparently a sync token
            // TODO: Ignoring at because I really don't know what is for in this context.
            // TODO: Handle state at the point the user has left.
            // TODO: Check the user is actually in the room!
            
            foreach (var ev in room.GetCurrentState.Where(e => e.Type == "m.room.member"))
            {
                var content = await ev.GetContent();
                var eventContents = content["content"] as JObject;

                if (eventContents == null || (string)eventContents["membership"] == notMembership)
                {
                    continue;
                }

                res.Chunk.Add(await BuildEvent(ev, room));
            }

            return res;
        }

        public async Task<RoomContextResponse> GetRoomContext(User user, string roomId, string eventId, int limit)
        {
            var res = new RoomContextResponse();
            var room = _roomSet.GetRoom(roomId, true);

            if (!room.Membership.Contains(user.Name))
            {
                throw new ErrorException("M_FORBIDDEN", "User not in room", HttpStatusCode.Forbidden);
            }

            var ev = room.GetEvent(eventId);

            if (ev == null)
            {
                throw new ErrorException("M_NOT_FOUND", "Event not found", HttpStatusCode.NotFound);
            }
            
            res.Event = await BuildEvent(ev, room);
            res.EventsAfter = new List<SyncResponse.SyncStateEvent>();
            res.EventsBefore = new List<SyncResponse.SyncStateEvent>();
            
            var lastEv = eventId;
            int startToken = ev.StreamOrdering;
            int endToken = ev.StreamOrdering;

            if (limit > 0)
            {
                var after = room.GetEventsAt(eventId, limit, true).ToList();
                var before = room.GetEventsAt(eventId, limit, false).ToList();

                startToken = after.LastOrDefault()?.StreamOrdering ?? startToken;
                endToken = before.LastOrDefault()?.StreamOrdering ?? endToken;
                
                lastEv = after.LastOrDefault()?.EventId ?? lastEv;

                foreach (var t in after.Select(e => BuildEvent(e, room)))
                {
                    res.EventsAfter.Add(await t);
                }
                
                foreach (var t in before.Select(e => BuildEvent(e, room)))
                {
                    res.EventsBefore.Add(await t);
                }
            }

            res.State = new List<SyncResponse.SyncStateEvent>();

            foreach (var stateEv in room.GetStateAtEvent(lastEv))
            {
                res.State.Add(await BuildEvent(stateEv, room));
            }

            res.Start = GetPrevBatch(startToken, true);
            res.End = GetPrevBatch(endToken, false);

            return res;
        }

        private async Task<SyncResponse.SyncStateEvent> BuildEvent(EventJsonSet ev, CachedMatrixRoom room)
        {
            var content = await ev.GetContent();
            
            var prevStateEv = content["unsigned"]?["replaces_state"]?.Value<string>();

            var prevContent = prevStateEv != null ? (JObject) (await room.GetEvent(prevStateEv).GetContent())["content"] : null;
            var stateKey = content.ContainsKey("state_key") ? content["state_key"].Value<string>() : null;

            return new SyncResponse.SyncStateEvent
            {
                Sender = ev.Sender,
                OriginServerTs = content["origin_server_ts"].Value<long>(),
                Unsigned = content["unsigned"] as JObject,
                Type = ev.Type,
                Content = content["content"] as JObject,
                EventId = ev.EventId,
                StateKey = stateKey,
                PrevContent = prevContent,
            };
        }
    }
}
