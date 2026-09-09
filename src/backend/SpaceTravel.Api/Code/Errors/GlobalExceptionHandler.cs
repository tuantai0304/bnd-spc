using Microsoft.AspNetCore.Diagnostics;

namespace SpaceTravel.Api.Code.Errors;

/// <summary>
/// Turns any unhandled exception into RFC 7807 ProblemDetails so the API never
/// leaks a stack trace and always answers in one shape.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        // A domain guard that was violated is the caller's fault, not the server's.
        var statusCode = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                type = "https://datatracker.ietf.org/doc/html/rfc9110",
                title = statusCode == StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred."
                    : exception.Message,
                status = statusCode,
                traceId = httpContext.TraceIdentifier
            },
            cancellationToken);

        return true;
    }
}
