using System.Linq;
using System.Text.RegularExpressions;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Database.WorkerModels;
using Matrix.SynapseInterop.Worker.AppserviceSender.Dto;
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
                var appservices = db.Appservices.ToDictionary(a => a.Id, a => new AppserviceDto(a));
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
                var appservice = db.Appservices.Find(appserviceId);

                if (appservice != null)
                {
                    appservice.Enabled = dto.Enabled;
                    appservice.AppserviceToken = dto.AsToken;
                    appservice.HomeserverToken = dto.HsToken;
                    appservice.Url = dto.Url;
                    appservice.SenderLocalpart = dto.SenderLocalpart;
                }
                else
                {
                    appservice = new Appservice(appserviceId);
                    appservice.Enabled = dto.Enabled;
                    appservice.AppserviceToken = dto.AsToken;
                    appservice.HomeserverToken = dto.HsToken;
                    appservice.Url = dto.Url;
                    appservice.SenderLocalpart = dto.SenderLocalpart;

                    db.Appservices.Add(appservice);
                }

                db.SaveChanges();
                resp.WriteJson(new AppserviceDto(appservice));
            }

            return true;
        }
    }
}
