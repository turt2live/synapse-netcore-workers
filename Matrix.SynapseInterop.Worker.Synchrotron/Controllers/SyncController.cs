using System.Text.RegularExpressions;
using Routable;
using Routable.Kestrel;

namespace Matrix.SynapseInterop.Worker.Synchrotron.Controllers
{
    public class SyncController : KestrelRouting
    {
        public AppserviceAdminRouting(
            RoutableOptions<KestrelRoutableContext, KestrelRoutableRequest, KestrelRoutableResponse> options
        ) : base(options)
        {
            Add(_ => _.Get("/_matrix/admin/r0/appservices").Try(OnListAppservices));

            Add(_ => _.Put().Path(new Regex("/_matrix/admin/r0/appservices/(?<appserviceId>.*)"))
                      .Try(OnUpsertAppservice));
        }

    }
}
