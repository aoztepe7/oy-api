using Oy.Api.Domain;

namespace Oy.Api.Tests;

public class SessionTests
{
    [Fact]
    public void Refresh_tokens_are_random_and_only_their_hash_is_kept()
    {
        var a = Sessions.NewRefreshToken();
        var b = Sessions.NewRefreshToken();
        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length); // 256 bits, base64url without padding
        Assert.DoesNotContain('+', a);
        Assert.Equal(64, Sessions.Hash(a).Length);
        Assert.Equal(Sessions.Hash(a), Sessions.Hash(a));
        Assert.NotEqual(Sessions.Hash(a), Sessions.Hash(b));
    }

    [Fact]
    public void Revoked_or_expired_sessions_are_not_active()
    {
        var now = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
        var s = new UserSession { ExpiresAt = now.AddDays(1) };
        Assert.True(s.IsActive(now));
        Assert.False(s.IsActive(now.AddDays(2)));
        s.RevokedAt = now;
        Assert.False(s.IsActive(now));
    }
}
