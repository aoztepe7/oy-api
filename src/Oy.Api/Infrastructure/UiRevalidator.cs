using Microsoft.Extensions.Options;

namespace Oy.Api.Infrastructure;

public class UiOptions
{
    /// <summary>Internal URL of oy-ui, e.g. http://ui:3000</summary>
    public string Url { get; set; } = "";
    public string RevalidateSecret { get; set; } = "";
}

public static class CacheTags
{
    public const string Programs = "programs";
    public const string Themes = "themes";
    public const string Team = "team";
    /// <summary>Site settings and menus (layout).</summary>
    public const string Site = "site";
    /// <summary>The list of published pages (sitemap).</summary>
    public const string Pages = "pages";
    public static string Page(string slug) => $"page:{slug}";
}

/// <summary>Tells oy-ui to drop cached data so published changes appear instantly.</summary>
public class UiRevalidator(HttpClient http, IOptions<UiOptions> options, ILogger<UiRevalidator> logger)
{
    public async Task RevalidateAsync(params string[] tags)
    {
        var o = options.Value;
        if (string.IsNullOrEmpty(o.Url)) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.Url.TrimEnd('/')}/api/revalidate")
            {
                Content = JsonContent.Create(new { tags }),
            };
            request.Headers.Add("x-revalidate-secret", o.RevalidateSecret);
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("UI revalidation failed for {Tags}: {Status}", tags, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            // Never fail the admin request because the site is unreachable; cache expires on its own.
            logger.LogWarning(ex, "UI revalidation unreachable for {Tags}", tags);
        }
    }
}
