using System.Net;

namespace JellyfinDesktopSlideshow.Services;

public enum JellyfinErrorKind
{
    Unreachable = 0,
    Timeout = 1,
    Unauthorized = 2,
    Forbidden = 3,
    BadRequestOrVersionMismatch = 4,
    NotFound = 5,
    ServerError = 6,
    InvalidResponse = 7
}

public sealed class JellyfinApiException : Exception
{
    public JellyfinErrorKind ErrorKind { get; }
    public HttpStatusCode? StatusCode { get; }

    public JellyfinApiException(
        JellyfinErrorKind errorKind,
        string userMessage,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        ErrorKind = errorKind;
        StatusCode = statusCode;
    }
}
