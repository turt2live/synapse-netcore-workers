using System.IO;
using Newtonsoft.Json;
using Routable.Kestrel;

namespace Matrix.SynapseInterop.Common.Extensions
{
    public static class KestrelRoutableRequestExtensions
    {
        public static T AsJson<T>(this KestrelRoutableRequest request) where T : class
        {
            using (var sr = new StreamReader(request.Body))
            {
                return JsonConvert.DeserializeObject<T>(sr.ReadToEnd());
            }
        }
    }
}
