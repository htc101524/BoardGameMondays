using System;
using System.Net;

namespace BoardGameMondays.Core;

public sealed class EmailApiException : Exception
{
    public EmailApiException(string message, HttpStatusCode statusCode, string? responseBody, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }
}
