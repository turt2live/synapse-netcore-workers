using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Common.WebResponses;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Routable;
using Routable.Kestrel;
using Serilog;

namespace Matrix.SynapseInterop.Worker.Synchrotron.Controllers
{
    public class SyncController : KestrelRouting
    {
        private Synchrotron _sync;
        public SyncController(
            RoutableOptions<KestrelRoutableContext, KestrelRoutableRequest, KestrelRoutableResponse> options, Synchrotron sync
        ) : base(options)
        {
            Add(_ => _.Get("/_matrix/client/r0/sync").TryAsync(OnSync));
            _sync = sync;
        }

        private async Task<bool> OnSync(KestrelRoutableContext context, KestrelRoutableRequest req, KestrelRoutableResponse res)
        {
            User user;

            try
            {
                user = GetUserForRequest(req);
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
            int timeout = req.Query.ContainsKey("timeout") ? int.Parse(req.Query["timeout"][0]) : 0;

            // TODO: Support filter, full_state, set_presence
            try
            {
                var syncResponse = await _sync.BuildSyncResponse(user, since, TimeSpan.FromSeconds(timeout));
                res.WriteJson(syncResponse);
            }
            catch (TimeoutException ex)
            {
                var err = new ErrorResponse("M_UNKNOWN", "Sync timed out");
                res.Status = (int) HttpStatusCode.InternalServerError;
                res.WriteJson(err);
            }
            catch (Exception ex)
            {
                var err = new ErrorResponse("M_UNKNOWN", "Internal server error");
                res.Status = (int) HttpStatusCode.InternalServerError;
                res.WriteJson(err);
            }

            return true;
        }

        public User GetUserForRequest(KestrelRoutableRequest req)
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
                if (!db.GetUserForToken(accessToken, out var user))
                {
                    throw new UnauthorizedAccessException("Unknown access_token");
                }

                return user;
            }
        }
    }
}
