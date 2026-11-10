using System.Diagnostics;
using Microsoft.Extensions.Options;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Infrastructure.Logging;

/// <summary>ILogger provider that forwards application logs to ClickHouse.</summary>
public sealed class ClickHouseLoggerProvider(ClickHouseLogWriter writer, IOptions<ClickHouseOptions> options) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ClickHouseLogger(categoryName, writer, options.Value.MinimumLevel);
    public void Dispose() { }

    private sealed class ClickHouseLogger(string category, ClickHouseLogWriter writer, LogLevel minLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => writer.Enabled && logLevel >= minLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            writer.Enqueue(ClickHouseLogWriter.AppLogsTable, new
            {
                Timestamp = DateTime.UtcNow,
                Level = logLevel.ToString(),
                Category = category,
                Message = formatter(state, exception),
                Exception = exception?.ToString() ?? "",
                TraceId = Activity.Current?.TraceId.ToString() ?? "",
            });
        }
    }
}

/// <summary>Writes one row per HTTP request (method, path, status, duration, user) to ClickHouse.</summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ClickHouseLogWriter writer)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(ctx);
        }
        finally
        {
            if (!ctx.Request.Path.StartsWithSegments("/health"))
            {
                writer.Enqueue(ClickHouseLogWriter.RequestLogsTable, new
                {
                    Timestamp = DateTime.UtcNow,
                    Method = ctx.Request.Method,
                    Path = ctx.Request.Path.Value ?? "",
                    Query = RedactToken(ctx.Request.QueryString.Value ?? ""),
                    Status = ctx.Response.StatusCode,
                    DurationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    UserId = ctx.User.UserId()?.ToString() ?? "",
                    Ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
                    UserAgent = ctx.Request.Headers.UserAgent.ToString(),
                    TraceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier,
                });
            }
        }
    }

    /// <summary>
    /// Credentials in query strings must never reach the logs: SignalR's <c>access_token</c> and the signature
    /// <c>s</c> of private file links (the link itself grants access).
    /// </summary>
    private static string RedactToken(string query) =>
        System.Text.RegularExpressions.Regex.Replace(query, "(?i)([?&](?:access_token|s)=)[^&]*", "$1***");
}

public static class ClickHouseLoggingSetup
{
    public static WebApplicationBuilder AddClickHouseLogging(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ClickHouseOptions>(builder.Configuration.GetSection("ClickHouse"));
        builder.Services.AddSingleton<ClickHouseLogWriter>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ClickHouseLogWriter>());
        builder.Services.AddSingleton<ILoggerProvider, ClickHouseLoggerProvider>();
        return builder;
    }
}
