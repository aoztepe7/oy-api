using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features.Auth;
using Oy.Api.Features.Content;
using Oy.Api.Features.Enrollments;
using Oy.Api.Features.Finance;
using Oy.Api.Features.Programs;
using Oy.Api.Features.Public;
using Oy.Api.Features.Settings;
using Oy.Api.Features.Staff;
using Oy.Api.Features.Users;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;
using Oy.Api.Infrastructure.Integrations;
using Oy.Api.Infrastructure.Logging;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Logging → console + ClickHouse
builder.AddClickHouseLogging();

// Data
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(config.GetConnectionString("Postgres")));
builder.Services.Configure<SeedOptions>(config.GetSection("Seed"));

// Auth
builder.Services.AddAppAuth(config);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// API conventions
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.Converters.Add(new TimeOnlyHhMmConverter());
});
builder.Services.AddValidation();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    // Validation keys follow the JSON contract (camelCase), e.g. "Title" → "title".
    if (ctx.ProblemDetails is HttpValidationProblemDetails v)
    {
        var errors = v.Errors.ToDictionary(e => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value);
        v.Errors.Clear();
        foreach (var (key, value) in errors) v.Errors[key] = value;
        v.Title = "Girilen bilgiler geçersiz.";
    }
});
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(Ip(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("public-forms", ctx => RateLimitPartition.GetFixedWindowLimiter(Ip(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); // runs behind the compose reverse proxy
    o.KnownProxies.Clear();
});

// Infrastructure services
builder.Services.Configure<MediaOptions>(config.GetSection("Media"));
builder.Services.AddSingleton<MediaService>();
builder.Services.AddMemoryCache(o => o.SizeLimit = 500); // visual editor previews
builder.Services.Configure<UiOptions>(config.GetSection("Ui"));
builder.Services.AddHttpClient<UiRevalidator>(c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddDataProtection()
    .SetApplicationName("okulumyeryuzu")
    .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeysPath"] ?? "data/keys"));

// Integrations (payment / e-invoice) — real clients plug in here later.
builder.Services.AddScoped<IntegrationService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<PaymentProviderResolver>();
builder.Services.AddSingleton<IPaymentProvider>(new PendingPaymentProvider("Iyzico"));
builder.Services.AddSingleton<IPaymentProvider>(new PendingPaymentProvider("PayTR"));
builder.Services.AddSingleton<IEInvoiceProvider, MockEInvoiceProvider>();
foreach (var name in new[] { "Uyumsoft", "QnbEFinans", "Logo" })
    builder.Services.AddSingleton<IEInvoiceProvider>(new PendingEInvoiceProvider(name));
builder.Services.AddHostedService<InvoiceWorker>();

var app = builder.Build();

await MigrateAndSeedAsync(app);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var media = app.Services.GetRequiredService<MediaService>();
Directory.CreateDirectory(media.FullPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(media.FullPath),
    RequestPath = MediaService.RoutePrefix.TrimEnd('/'),
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable",
});

app.MapOpenApi();
app.MapScalarApiReference("/docs");
app.MapHealthChecks("/health");

app.MapPublic();
app.MapAuth();
app.MapDashboard();
app.MapUsers();
app.MapPrograms();
app.MapSpecialDays();
app.MapStaff();
app.MapEnrollments();
app.MapFinance();
app.MapContent();
app.MapPages();
app.MapPreviews();
app.MapIntegrations();

app.Run();

static async Task MigrateAndSeedAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
        scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value);
}

public partial class Program;
