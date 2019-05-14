using Newtonsoft.Json;

namespace Matrix.SynapseInterop.Common.WebResponses
{
    public class ErrorResponse
    {
        [JsonProperty("errcode")]
        public string ErrorCode { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
        
        [JsonIgnore]
        public int HttpStatus { get; }

        public ErrorResponse(string errorCode, string error = null, int httpStatus = 500)
        {
            ErrorCode = errorCode;
            Error = error;
            HttpStatus = httpStatus;
        }
        
        public static ErrorResponse DefaultResponse = new ErrorResponse("M_UNKNOWN", "Internal server error");
    }
}
