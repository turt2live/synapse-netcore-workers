using System;
using System.Net;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Common.WebResponses;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Routable.Kestrel;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    public static class ControllerUtils
    {
        public static User GetUserForRequest(KestrelRoutableRequest req, out AccessToken token)
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

        public static User GetUserForRequest(KestrelRoutableRequest req)
        {
            return GetUserForRequest(req, out _);
        }

        public static bool RouteGone(KestrelRoutableContext context, KestrelRoutableRequest req, KestrelRoutableResponse res)
        {
            res.Status = (int) HttpStatusCode.Gone;
            var part = req.Uri.LocalPath;
            res.WriteJson(new ErrorResponse(error: $"'{part}' is no longer supported", errorCode: "M_UNKNOWN"));
            return true;
        }
    }
}
