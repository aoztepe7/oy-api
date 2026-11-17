using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Oy.Api.Infrastructure;

/// Turns request-binding failures (bad JSON, wrong types, unparsable route values) into a 4xx problem
/// response that names the offending field. Exception messages are never echoed to the client.
public sealed class BadRequestExceptionHandler(IProblemDetailsService problems, ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not BadHttpRequestException bad) return false;

        logger.LogInformation("Bad request {Method} {Path}: {Reason}", context.Request.Method, context.Request.Path, bad.GetType().Name);
        context.Response.StatusCode = bad.StatusCode;

        if (bad.InnerException is JsonException { Path: { } path })
        {
            var key = path.StartsWith("$.") ? path[2..] : path == "$" ? "body" : path.TrimStart('$');
            var details = new HttpValidationProblemDetails(new Dictionary<string, string[]> { [key] = ["Geçersiz değer."] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Girilen bilgiler geçersiz.",
            };
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = details });
        }

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = bad.StatusCode, Title = "Gönderilen istek okunamadı." },
        });
    }
}
