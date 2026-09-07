using Shortener.Infrastructure;
using Xunit;

namespace Shortener.Authentication.Tests;

public sealed class RateLimitTests
{
    [Fact]
    public void Login_budget_is_shared_by_ip_and_does_not_store_the_address()
    {
        var rule = Assert.Single(AuthRateLimits.ForAuth("login", "192.0.2.1", null));
        Assert.Equal(10, rule.Limit);
        Assert.Equal(TimeSpan.FromMinutes(10), rule.Window);
        Assert.DoesNotContain("192.0.2.1", rule.Key);
        Assert.Equal(rule, Assert.Single(AuthRateLimits.ForAuth("login", "192.0.2.1", "person@example.test")));
    }

    [Fact]
    public void Email_budget_is_shared_across_endpoints_and_normalizes_email()
    {
        var first = AuthRateLimits.ForAuth("register", "192.0.2.1", " Person@example.test ");
        var second = AuthRateLimits.ForAuth("forgot-password", "192.0.2.1", "person@EXAMPLE.test");
        Assert.Equal(first, second);
        Assert.Contains(first, x => x.Limit == 3 && x.Window == TimeSpan.FromHours(1));
        Assert.Contains(first, x => x.Limit == 20 && x.Window == TimeSpan.FromHours(1));
        Assert.All(first, x => Assert.DoesNotContain("example", x.Key));
    }

    [Fact]
    public void Protected_budget_uses_user_identity()
    {
        var user = Guid.NewGuid();
        var rule = AuthRateLimits.ForUser(user);
        Assert.Equal(300, rule.Limit);
        Assert.Equal(TimeSpan.FromMinutes(1), rule.Window);
        Assert.NotEqual(rule.Key, AuthRateLimits.ForUser(Guid.NewGuid()).Key);
    }
}
