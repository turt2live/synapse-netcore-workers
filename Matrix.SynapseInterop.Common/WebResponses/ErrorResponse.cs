using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Common.WebResponses
{
    public class ErrorResponse
    {
        [JsonProperty("errcode")]
        public string ErrorCode { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        public ErrorResponse(string errorCode, string error = null)
        {
            ErrorCode = errorCode;
            Error = error;
        }
    }
}
