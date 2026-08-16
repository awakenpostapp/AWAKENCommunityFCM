using System.Net;

namespace CommunityFootballClubManager.Services.Online;

public sealed class ApiException : Exception
{
    public ApiException(
        HttpStatusCode statusCode,
        string code,
        string message,
        string? traceId = null,
        object? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = string.IsNullOrWhiteSpace(code) ? "api_error" : code;
        TraceId = traceId;
        Details = details;
    }

    public HttpStatusCode StatusCode { get; }

    public string Code { get; }

    public string? TraceId { get; }

    public object? Details { get; }

    public bool IsAuthenticationFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;
}
