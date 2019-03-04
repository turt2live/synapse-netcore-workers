using System.Linq;
using System.Text.RegularExpressions;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Common.WebResponses;
using Matrix.SynapseInterop.Database.WorkerModels;
using Matrix.SynapseInterop.Worker.AppserviceSender.Dto;
using Microsoft.EntityFrameworkCore;
using Routable;
using Routable.Kestrel;

namespace Matrix.SynapseInterop.Worker.AppserviceSender.Controllers
{
    public sealed class AppserviceAdminRouting : KestrelRouting
    {
        public AppserviceAdminRouting(
            RoutableOptions<KestrelRoutableContext, KestrelRoutableRequest, KestrelRoutableResponse> options
        ) : base(options)
        {
            Add(_ => _.Get("/_matrix/admin/r0/appservices").Try(OnListAppservices));

            Add(_ => _.Put().Path(new Regex("/_matrix/admin/r0/appservices/(?<appserviceId>.*)"))
                      .Try(OnUpsertAppservice));
        }

        private bool OnListAppservices(KestrelRoutableContext ctx,
                                       KestrelRoutableRequest req,
                                       KestrelRoutableResponse resp
        )
        {
            using (var db = new AppserviceDb())
            {
                var appservices = db.Appservices
                                    .Include(a => a.Namespaces)
                                    .ToDictionary(a => a.Id, a => new AppserviceDto(a));

                resp.WriteJson(appservices);
            }

            return true;
        }

        private bool OnUpsertAppservice(KestrelRoutableContext ctx,
                                        KestrelRoutableRequest req,
                                        KestrelRoutableResponse resp
        )
        {
            var appserviceId = req.Parameters["appserviceId"].ToString();
            var dto = req.AsJson<AppserviceDto>();

            using (var db = new AppserviceDb())
            {
                // Find homeservers using the AS token
                var hasAsToken = db.Appservices.Any(a => a.Id != appserviceId && a.AppserviceToken == dto.AsToken);

                if (hasAsToken)
                {
                    resp.Status = 400;
                    resp.WriteJson(new ErrorResponse(ErrorCodes.BAD_REQUEST, "as_token in use"));
                    return true;
                }

                var appservice = db.Appservices.Include(a => a.Namespaces)
                                   .SingleOrDefault(a => a.Id == appserviceId);

                if (appservice != null)
                {
                    appservice.Enabled = dto.Enabled;
                    appservice.AppserviceToken = dto.AsToken;
                    appservice.HomeserverToken = dto.HsToken;
                    appservice.Url = dto.Url;
                    appservice.SenderLocalpart = dto.SenderLocalpart;

                    appservice.ClearNamespaces();

                    dto.Namespaces?.Users?
                       .ForEach(ns => appservice.AddNamespace(AppserviceNamespace.NS_USERS, ns.Exclusive, ns.Regex));

                    dto.Namespaces?.Aliases?
                       .ForEach(ns => appservice.AddNamespace(AppserviceNamespace.NS_ALIASES, ns.Exclusive, ns.Regex));

                    dto.Namespaces?.Rooms?
                       .ForEach(ns => appservice.AddNamespace(AppserviceNamespace.NS_ROOMS, ns.Exclusive, ns.Regex));
                }
                else
                {
                    appservice = new Appservice(appserviceId);
                    appservice.Enabled = dto.Enabled;
                    appservice.AppserviceToken = dto.AsToken;
                    appservice.HomeserverToken = dto.HsToken;
                    appservice.Url = dto.Url;
                    appservice.SenderLocalpart = dto.SenderLocalpart;

                    dto.Namespaces?.Users?
                       .ForEach(ns => appservice.AddNamespace(AppserviceNamespace.NS_USERS, ns.Exclusive, ns.Regex));

                    dto.Namespaces?.Aliases?
                       .ForEach(ns => appservice.AddNamespace(AppserviceNamespace.NS_ALIASES, ns.Exclusive, ns.Regex));

                    dto.Namespaces?.Rooms?
                       .ForEach(ns => appservice.AddNamespace(AppserviceNamespace.NS_ROOMS, ns.Exclusive, ns.Regex));

                    db.Appservices.Add(appservice);
                }

                db.SaveChanges();
                resp.WriteJson(new AppserviceDto(appservice));
            }

            return true;
        }
    }
}
