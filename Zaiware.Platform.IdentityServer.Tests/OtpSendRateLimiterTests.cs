namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class OtpSendRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Per_address_budget_denies_after_the_limit_and_allows_a_different_address()
    {
        var clock = new MutableTimeProvider(Start);
        var limiter = Create(clock, perAddressLimit: 2, globalLimit: 100);

        Assert.True(limiter.TryAcquire("203.0.113.10").Allowed);
        Assert.True(limiter.TryAcquire("203.0.113.10").Allowed);

        var denied = limiter.TryAcquire("203.0.113.10");

        Assert.False(denied.Allowed);
        Assert.True(denied.RetryAfter > TimeSpan.Zero);
        Assert.True(limiter.TryAcquire("203.0.113.11").Allowed);
    }

    [Fact]
    public void The_budget_resets_at_the_next_fixed_window()
    {
        var clock = new MutableTimeProvider(Start);
        var limiter = Create(clock, perAddressLimit: 1, globalLimit: 100);

        Assert.True(limiter.TryAcquire("203.0.113.10").Allowed);
        Assert.False(limiter.TryAcquire("203.0.113.10").Allowed);

        clock.UtcNow = Start.AddMinutes(10);

        Assert.True(limiter.TryAcquire("203.0.113.10").Allowed);
    }

    [Fact]
    public void The_global_budget_denies_every_address_in_the_window()
    {
        var clock = new MutableTimeProvider(Start);
        var limiter = Create(clock, perAddressLimit: 10, globalLimit: 2);

        Assert.True(limiter.TryAcquire("203.0.113.10").Allowed);
        Assert.True(limiter.TryAcquire("203.0.113.11").Allowed);

        Assert.False(limiter.TryAcquire("203.0.113.12").Allowed);
    }

    [Fact]
    public void Ipv4_mapped_addresses_share_the_ipv4_budget()
    {
        var clock = new MutableTimeProvider(Start);
        var limiter = Create(clock, perAddressLimit: 1, globalLimit: 100);

        Assert.True(limiter.TryAcquire("::ffff:127.0.0.1").Allowed);
        Assert.False(limiter.TryAcquire("127.0.0.1").Allowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_address_is_bucketed_as_unknown(string? address)
    {
        var clock = new MutableTimeProvider(Start);
        var limiter = Create(clock, perAddressLimit: 1, globalLimit: 100);

        Assert.True(limiter.TryAcquire(address).Allowed);
        Assert.False(limiter.TryAcquire(null).Allowed);
    }

    private static OtpSendRateLimiter Create(
        TimeProvider clock,
        int perAddressLimit,
        int globalLimit) =>
        new(
            clock,
            new OtpSendRateLimitOptions
            {
                PerClientLimit = perAddressLimit,
                GlobalLimit = globalLimit,
                Window = TimeSpan.FromMinutes(10),
            });
}
