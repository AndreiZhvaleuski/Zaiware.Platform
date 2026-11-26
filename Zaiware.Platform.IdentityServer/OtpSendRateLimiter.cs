using System.Net;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Applies a small fixed-window budget to one-time-code sends. The per-address production
/// cooldown in User Management only throttles a single email address, so without this an
/// attacker could send codes to an unbounded number of addresses from one client.
/// </summary>
public interface IOtpSendRateLimiter
{
    /// <summary>The generic response shown when the budget is exhausted.</summary>
    public const string TooManyRequestsMessage = "Too many requests. Try again later.";

    /// <summary>
    /// Attempts to reserve one send from the current window for the client address.
    /// </summary>
    /// <param name="clientAddress">The remote address, or <see langword="null"/> when unavailable.</param>
    OtpSendRateLimitResult TryAcquire(string? clientAddress);
}

/// <summary>The outcome of a rate-limit acquisition.</summary>
/// <param name="Allowed">Whether the send may proceed.</param>
/// <param name="RetryAfter">How long remains in the exhausted window when denied.</param>
public readonly record struct OtpSendRateLimitResult(bool Allowed, TimeSpan RetryAfter)
{
    /// <summary>A successful acquisition.</summary>
    public static OtpSendRateLimitResult AllowedNow => new(true, TimeSpan.Zero);

    /// <summary>A denied acquisition for the remainder of the current window.</summary>
    public static OtpSendRateLimitResult Denied(TimeSpan retryAfter) => new(false, retryAfter);
}

/// <summary>
/// Fixed-window budgets for OTP sends. Defaults allow 30 sends per client address and 300
/// sends overall in any rolling ten-minute fixed window; both are configurable through the
/// <c>OtpSendRateLimit</c> configuration section.
/// </summary>
public sealed class OtpSendRateLimitOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "OtpSendRateLimit";

    /// <summary>Maximum sends per client address per window.</summary>
    public int PerClientLimit { get; set; } = 30;

    /// <summary>Maximum sends across all client addresses per window.</summary>
    public int GlobalLimit { get; set; } = 300;

    /// <summary>The fixed window length.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// In-process fixed-window implementation. State is intentionally small (one counter per
/// active client address plus one global counter) and expires with the window; a restart
/// clears the budgets. This is a development/single-instance safeguard rather than a
/// distributed rate limiter.
/// </summary>
public sealed class OtpSendRateLimiter(
    TimeProvider timeProvider,
    OtpSendRateLimitOptions options) : IOtpSendRateLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Window> _perAddress = new(StringComparer.Ordinal);
    private Window _global;
    private bool _hasGlobal;

    /// <inheritdoc />
    public OtpSendRateLimitResult TryAcquire(string? clientAddress)
    {
        var key = Normalize(clientAddress);
        var now = timeProvider.GetUtcNow();
        var windowTicks = Math.Max(1, options.Window.Ticks);
        var windowStart = now.UtcTicks - (now.UtcTicks % windowTicks);
        var perClientLimit = Math.Max(1, options.PerClientLimit);
        var globalLimit = Math.Max(1, options.GlobalLimit);

        lock (_gate)
        {
            PruneExpiredWindows(windowStart);

            var addressWindow = _perAddress.TryGetValue(key, out var existing)
                && existing.StartTicks == windowStart
                    ? existing
                    : new Window(windowStart, 0);

            if (addressWindow.Count >= perClientLimit)
            {
                return OtpSendRateLimitResult.Denied(Remaining(windowStart, windowTicks, now));
            }

            if (_hasGlobal
                && _global.StartTicks == windowStart
                && _global.Count >= globalLimit)
            {
                return OtpSendRateLimitResult.Denied(Remaining(windowStart, windowTicks, now));
            }

            _perAddress[key] = addressWindow with { Count = addressWindow.Count + 1 };
            _global = _hasGlobal && _global.StartTicks == windowStart
                ? _global with { Count = _global.Count + 1 }
                : new Window(windowStart, 1);
            _hasGlobal = true;

            return OtpSendRateLimitResult.AllowedNow;
        }
    }

    private void PruneExpiredWindows(long currentWindowStart)
    {
        if (_perAddress.Count < 1024)
        {
            return;
        }

        foreach (var entry in _perAddress.ToArray())
        {
            if (entry.Value.StartTicks != currentWindowStart)
            {
                _perAddress.Remove(entry.Key);
            }
        }
    }

    private static TimeSpan Remaining(long windowStart, long windowTicks, DateTimeOffset now)
    {
        var remainingTicks = windowStart + windowTicks - now.UtcTicks;
        return remainingTicks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(remainingTicks);
    }

    private static string Normalize(string? clientAddress)
    {
        if (string.IsNullOrWhiteSpace(clientAddress))
        {
            return "unknown";
        }

        return IPAddress.TryParse(clientAddress, out var address) && address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : clientAddress.Trim();
    }

    private readonly record struct Window(long StartTicks, int Count);
}
