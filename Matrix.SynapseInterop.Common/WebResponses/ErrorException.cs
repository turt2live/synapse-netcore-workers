using System;
using System.Net;

namespace Matrix.SynapseInterop.Common.WebResponses
{
    public class ErrorException : Exception
    {
        public readonly ErrorResponse Response;
        
        public ErrorException(ErrorResponse res)
        {
            Response = res;
        }

        public ErrorException(string errcode, string error, int httpCode = 500) : this(new ErrorResponse(errcode, error, httpCode)) { }
        
        public ErrorException(string errcode, string error, HttpStatusCode httpCode) : this(new ErrorResponse(errcode, error, (int) httpCode)) { }
    }
}
