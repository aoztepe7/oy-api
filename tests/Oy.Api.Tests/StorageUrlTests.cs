using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Oy.Api.Infrastructure;

namespace Oy.Api.Tests;

public class StorageUrlTests
{
    private sealed class FakeStorage(string? publicBase) : IFileStorage
    {
        public string Description => "fake";
        public bool IsRemote => publicBase is not null;
        public Task SaveAsync(string key, Stream content, string contentType, bool isPublic, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public string? PublicUrl(string key) => publicBase is null ? null : $"{publicBase}/prod/{key}";
        public Task<IResult> ServePrivateAsync(string key, string contentType, CancellationToken ct = default) => Task.FromResult(Results.Ok());
        public TimeSpan MaxPrivateCache => TimeSpan.FromMinutes(50);
    }

    private static MediaService Media(string? spaces) =>
        new(Options.Create(new MediaOptions { PublicBaseUrl = "https://api.example.com" }), new FakeStorage(spaces));

    [Fact]
    public void Disk_serves_media_from_the_api()
    {
        Assert.Equal("https://api.example.com/media/a.webp?focus=40,60", Media(null).ToPublic("/media/a.webp?focus=40,60"));
    }

    [Fact]
    public void Spaces_serves_media_from_the_cdn_with_the_same_stored_value()
    {
        var media = Media("https://cdn.example.com");
        Assert.Equal("https://cdn.example.com/prod/media/seed/a.webp?focus=40,60", media.ToPublic("/media/seed/a.webp?focus=40,60"));
        Assert.Equal("https://elsewhere.com/x.png", media.ToPublic("https://elsewhere.com/x.png"));
    }

    [Theory]
    [InlineData("https://cdn.example.com/prod/media/a.webp?fit=contain")]
    [InlineData("https://api.example.com/media/a.webp?fit=contain")] // old API link saved before the move
    public void Both_public_forms_are_stored_relative(string url)
    {
        Assert.Equal("/media/a.webp?fit=contain", Media("https://cdn.example.com").ToStored(url));
    }
}
