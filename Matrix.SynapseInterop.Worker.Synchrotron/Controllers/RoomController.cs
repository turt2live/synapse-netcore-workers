using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Common.WebResponses;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Microsoft.AspNetCore.Cors;
using Routable;
using Routable.Kestrel;

namespace Matrix.SynapseInterop.Worker.Synchrotron.Controllers
{
    public class RoomController : KestrelRouting
    {
        private readonly MessagesHandler _messagesHandler;

        public RoomController(
            RoutableOptions<KestrelRoutableContext, KestrelRoutableRequest, KestrelRoutableResponse> options, MessagesHandler msgHandler
        ) : base(options)
        {
            // Split these to a room controller.

            _messagesHandler = msgHandler;
            
            Add(_ => _.Get().Path(new Regex("/_matrix/client/r0/rooms/(?<roomId>.*)/messages")).TryAsync(OnRoomMessages));
            
            Add(_ => _.Get().Path(new Regex("/_matrix/client/r0/rooms/(?<roomId>.*)/context/(?<eventId>.*)")).TryAsync(OnRoomContext));
            
            Add(_ => _.Get().Path(new Regex("/_matrix/client/r0/rooms/(?<roomId>.*)/members")).TryAsync(OnRoomMembers));
        }

        private async Task<bool> OnRoomContext(KestrelRoutableContext context, KestrelRoutableRequest req, KestrelRoutableResponse res)
        {
            User user;

            try
            {
                user = ControllerUtils.GetUserForRequest(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ErrorException("M_FORBIDDEN", ex.Message);
            }
            
            var roomId = req.Parameters["roomId"] as string;
            var eventId = req.Parameters["eventId"] as string;
            var sLimit = req.Query.ContainsKey("limit") ? req.Query["limit"][0] : null;

            if (!int.TryParse(sLimit, out var limit))
            {
                limit = 10;
            }
            
            res.WriteJson(await _messagesHandler.GetRoomContext(user, roomId, eventId, limit));
            
            return true;
        }

        private async Task<bool> OnRoomMessages(KestrelRoutableContext context,
                                                KestrelRoutableRequest req,
                                                KestrelRoutableResponse res
        )
        {
            User user;

            try
            {
                user = ControllerUtils.GetUserForRequest(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ErrorException("M_FORBIDDEN", ex.Message);
            }
            
            var roomId = req.Parameters["roomId"] as string;
            var from = req.Query.ContainsKey("from") ? req.Query["from"][0] : null;
            var to = req.Query.ContainsKey("to") ? req.Query["to"][0] : null;
            var dir = req.Query.ContainsKey("dir") ? req.Query["dir"][0] : null;
            var sLimit = req.Query.ContainsKey("limit") ? req.Query["limit"][0] : null;

            if (dir == null)
            {
                var err = new ErrorResponse("M_UNKNOWN", "Missing 'dir'");
                res.Status = (int) HttpStatusCode.BadRequest;
                res.WriteJson(err);
            }

            if (!int.TryParse(sLimit, out var limit))
            {
                limit = 10;
            }

            res.WriteJson(await _messagesHandler.OnRoomMessages(user, roomId, from, dir, limit, to));

            return true;
        }

        private async Task<bool> OnRoomMembers(KestrelRoutableContext context,
                                               KestrelRoutableRequest req,
                                               KestrelRoutableResponse res
        )
        {
            User user;

            try
            {
                user = ControllerUtils.GetUserForRequest(req);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ErrorException("M_FORBIDDEN", ex.Message);
            }
            
            var roomId = req.Parameters["roomId"] as string;
            // https://github.com/matrix-org/matrix-doc/issues/1945
            var at = req.Query.ContainsKey("at") ? req.Query["at"][0] : null;
            var notMembership = req.Query.ContainsKey("not_membership") ? req.Query["not_membership"][0] : null;
            
            res.WriteJson(await _messagesHandler.GetRoomMembers(roomId, user, at, notMembership));
            return true;
        }
    }
}
