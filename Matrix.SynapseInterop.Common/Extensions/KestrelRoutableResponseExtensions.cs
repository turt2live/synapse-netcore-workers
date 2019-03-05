using Newtonsoft.Json;
using Routable.Kestrel;

namespace Matrix.SynapseInterop.Common.Extensions
{
    public static class KestrelRoutableResponseExtensions
    {
        public static void WriteJson(this KestrelRoutableResponse response, object responseObject)
        {
            response.ContentType = "application/json";

            response.Write(JsonConvert.SerializeObject(responseObject));
        }
    }
}
