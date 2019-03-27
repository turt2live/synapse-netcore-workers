using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Common.WebResponses;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Microsoft.EntityFrameworkCore.Internal;
using Newtonsoft.Json.Linq;
using Routable;
using Routable.Kestrel;
using Serilog;
using Routable;
using Routable.Kestrel;

namespace Matrix.SynapseInterop.Worker.Synchrotron.Controllers
{
    public class SyncController : KestrelRouting
    {
        private readonly Synchrotron _sync;

        public SyncController(
            RoutableOptions<KestrelRoutableContext, KestrelRoutableRequest, KestrelRoutableResponse> options, Synchrotron sync
        ) : base(options)
        {
            Add(_ => _.Get("/_matrix/client/r0/sync").TryAsync(OnSync));
            
            Add(_ => _.Get().Path(new Regex("/_matrix/client/r0/rooms/(?<roomId>.*)/initialSync")).TryAsync(OnRoomInitialSync));
            
            Add(_ => _.Method("/_matrix/client/r0/events").Try(Gone));

            Add(_ => _.Method("/_matrix/client/r0/initialSync").Try(Gone));
            
            _sync = sync;
        }

        private bool Gone(KestrelRoutableContext context, KestrelRoutableRequest req, KestrelRoutableResponse res)
        {
            res.Status = (int) HttpStatusCode.Gone;
            res.WriteJson(new ErrorResponse(error: "'events' is no longer supported", errorCode: "M_UNKNOWN"));
            return true;
        }

        private async Task<bool> OnSync(KestrelRoutableContext context, KestrelRoutableRequest req, KestrelRoutableResponse res)
        {
            User user;
            AccessToken accessToken;

            try
            {
                user = GetUserForRequest(req, out accessToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                var err = new ErrorResponse("M_FORBIDDEN", ex.Message);
                res.Status = (int) HttpStatusCode.Forbidden;
                res.WriteJson(err);
                return true;
            }

            if (user.AppserviceId != null)
            {
                var err = new ErrorResponse("M_FORBIDDEN", "Appservice users cannot sync");
                res.Status = (int) HttpStatusCode.Forbidden;
                res.WriteJson(err);
                return true;
            }
            
            // Get important parameters
            string since = req.Query.ContainsKey("since") ? req.Query["since"][0] : null;
            string filter = req.Query.ContainsKey("filter") ? req.Query["filter"][0] : null;
            int timeout = req.Query.ContainsKey("timeout") ? int.Parse(req.Query["timeout"][0]) : 0;

            // XXX: set_presence isn't supported because the Python synchrotron doesn't implement it either, and therefore
            // neither does synapse.
            SyncFilter syncFilter;

            if (filter == null)
            {
                syncFilter = SyncFilter.DefaultFilter;
            }
            else if (filter.StartsWith("{"))
            {
                syncFilter = SyncFilter.FromJSON(filter);
            }
            else
            {
                syncFilter = SyncFilter.FromDB(user, filter);
            }
            
            syncFilter.FullState = req.Query.ContainsKey("full_state") && bool.Parse(req.Query["full_state"][0]);
            syncFilter.DeviceId = accessToken.DeviceId;

            try
            {
                var syncResponse = await _sync.BuildSyncResponse(user,
                                                                 since,
                                                                 TimeSpan.FromSeconds(timeout),
                                                                 syncFilter);
                res.WriteJson(syncResponse);
            }
            catch (TimeoutException)
            {
                var err = new ErrorResponse("M_UNKNOWN", "Sync timed out");
                res.Status = (int) HttpStatusCode.InternalServerError;
                res.WriteJson(err);
            }
            catch (Exception)
            {
                var err = new ErrorResponse("M_UNKNOWN", "Internal server error");
                res.Status = (int) HttpStatusCode.InternalServerError;
                res.WriteJson(err);
            }

            return true;
        }

        private async Task<bool> OnRoomInitialSync(KestrelRoutableContext context, KestrelRoutableRequest req, KestrelRoutableResponse res)
        {
            User user;
            AccessToken accessToken;
            
            try
            {
                user = GetUserForRequest(req, out accessToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                var err = new ErrorResponse("M_FORBIDDEN", ex.Message);
                res.Status = (int) HttpStatusCode.Forbidden;
                res.WriteJson(err);
                return true;
            }

            var roomId = req.Parameters["roomId"] as string;
            var syncResponse = await _sync.BuildRoomInitialSync(user, roomId);

            res.WriteJson(syncResponse);

            return true;
        }

        private User GetUserForRequest(KestrelRoutableRequest req, out AccessToken token)
        {
            string accessToken;

            if (req.Query.TryGetValue("access_token", out var at))
            {
                accessToken = at[0];
            }
            else if (req.Headers.TryGetValue("Authorization", out var auth))
            {
                accessToken = auth[0].Substring("Bearer ".Length);
            }
            else
            {
                throw new UnauthorizedAccessException("Missing access_token");
            }

            using (var db = new SynapseDbContext())
            {
                if (!db.GetUserForToken(accessToken, out var user, out token))
                {
                    throw new UnauthorizedAccessException("Unknown access_token");
                }

                return user;
            }
        }
    }
}
