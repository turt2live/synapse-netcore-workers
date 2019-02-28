using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
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
            resp.ContentType = "application/json";

            resp.Write(JObject.FromObject(new
            {
                TestAppservice = new
                {
                    enabled = true,
                    as_token = "sample_as_token",
                    hs_token = "sample_hs_token",
                    url = "http://localhost:9000",
                    sender_localpart = "_example",
                    namespaces = new
                    {
                        users = new object[]
                        {
                            new {exclusive = true, regex = "@_example.*"}
                        },
                        aliases = new object[0],
                        rooms = new object[0]
                    }
                }
            }));

            return true;
        }

        private bool OnUpsertAppservice(KestrelRoutableContext ctx,
                                        KestrelRoutableRequest req,
                                        KestrelRoutableResponse resp
        )
        {
            resp.ContentType = "application/json";

            resp.Write(JObject.FromObject(new
            {
                TestAppservice = new
                {
                    enabled = true,
                    id = req.Parameters["appserviceId"],
                    //body = req.Body,
                    as_token = "sample_as_token",
                    hs_token = "sample_hs_token",
                    url = "http://localhost:9000",
                    sender_localpart = "_example",
                    namespaces = new
                    {
                        users = new object[]
                        {
                            new {exclusive = true, regex = "@_example.*"}
                        },
                        aliases = new object[0],
                        rooms = new object[0]
                    }
                }
            }));

            return true;
        }
    }
}
