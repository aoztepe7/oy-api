using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Oy.Api.Infrastructure.Logging;

public class ClickHouseOptions
{
    /// <summary>HTTP endpoint, e.g. http://clickhouse:8123. Empty disables ClickHouse logging.</summary>
    public string Url { get; set; } = "";
    public string Database { get; set; } = "oy_logs";
    public string User { get; set; } = "default";
    public string Password { get; set; } = "";
    public int TtlDays { get; set; } = 90;
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
}

public record LogRow(string Table, object Row);

/// <summary>
/// Buffers log rows in memory and flushes them in batches to ClickHouse over HTTP (JSONEachRow).
/// Logging must never block or break requests: the queue is bounded and drops oldest rows when full.
/// Uses its own HttpClient (not IHttpClientFactory) so its HTTP calls are not logged back into itself.
/// </summary>
public sealed class ClickHouseLogWriter : BackgroundService
{
    public const string AppLogsTable = "app_logs";
    public const string RequestLogsTable = "request_logs";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly ClickHouseOptions _o;
    private readonly HttpClient _http;
    private readonly Channel<LogRow> _queue = Channel.CreateBounded<LogRow>(
        new BoundedChannelOptions(20_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ClickHouseLogWriter(IOptions<ClickHouseOptions> options)
    {
        _o = options.Value;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (Enabled)
        {
            _http.BaseAddress = new Uri(_o.Url.TrimEnd('/') + "/");
            _http.DefaultRequestHeaders.Add("X-ClickHouse-User", _o.User);
            _http.DefaultRequestHeaders.Add("X-ClickHouse-Key", _o.Password);
        }
    }

    public bool Enabled => !string.IsNullOrWhiteSpace(_o.Url);

    public void Enqueue(string table, object row)
    {
        if (Enabled) _queue.Writer.TryWrite(new LogRow(table, row));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled) return;
        await EnsureSchemaAsync(stoppingToken);

        var batch = new List<LogRow>(1000);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                while (batch.Count < 5000 && _queue.Reader.TryRead(out var row)) batch.Add(row);
                if (batch.Count == 0) continue;
                await FlushAsync(batch, CancellationToken.None);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) { }

        // Drain what is left on shutdown.
        while (_queue.Reader.TryRead(out var row)) batch.Add(row);
        if (batch.Count > 0) await FlushAsync(batch, CancellationToken.None);
    }

    private async Task FlushAsync(List<LogRow> batch, CancellationToken ct)
    {
        foreach (var group in batch.GroupBy(r => r.Table))
        {
            var body = new StringBuilder();
            foreach (var row in group) body.AppendLine(JsonSerializer.Serialize(row.Row, Json));
            var query = Uri.EscapeDataString($"INSERT INTO {_o.Database}.{group.Key} FORMAT JSONEachRow");
            try
            {
                using var content = new StringContent(body.ToString(), Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
                using var response = await _http.PostAsync($"?query={query}&date_time_input_format=best_effort", content, ct);
                if (!response.IsSuccessStatusCode)
                    Console.Error.WriteLine($"[clickhouse] insert failed {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[clickhouse] insert error: {ex.Message}");
            }
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        string[] statements =
        [
            $"CREATE DATABASE IF NOT EXISTS {_o.Database}",
            $"""
            CREATE TABLE IF NOT EXISTS {_o.Database}.{AppLogsTable} (
                timestamp DateTime64(3, 'UTC'),
                level LowCardinality(String),
                category LowCardinality(String),
                message String,
                exception String,
                trace_id String
            ) ENGINE = MergeTree ORDER BY (timestamp, level)
            TTL toDateTime(timestamp) + INTERVAL {_o.TtlDays} DAY
            """,
            $"""
            CREATE TABLE IF NOT EXISTS {_o.Database}.{RequestLogsTable} (
                timestamp DateTime64(3, 'UTC'),
                method LowCardinality(String),
                path String,
                query String,
                status UInt16,
                duration_ms Float64,
                user_id String,
                ip String,
                user_agent String,
                trace_id String
            ) ENGINE = MergeTree ORDER BY (timestamp, path)
            TTL toDateTime(timestamp) + INTERVAL {_o.TtlDays} DAY
            """,
        ];

        // ClickHouse may still be starting: retry with backoff instead of failing the API.
        for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                foreach (var sql in statements)
                {
                    using var response = await _http.PostAsync("", new StringContent(sql), ct);
                    response.EnsureSuccessStatusCode();
                }
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[clickhouse] schema init attempt {attempt} failed: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 3)), ct);
            }
        }
    }

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }
}
