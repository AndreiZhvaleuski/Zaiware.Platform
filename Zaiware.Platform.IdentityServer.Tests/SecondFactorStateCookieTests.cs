using Duende.UserManagement;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class SecondFactorStateCookieTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();

    [Fact]
    public void Round_trips_a_subject_id_with_the_host_prefixed_secure_cookie()
    {
        var (cookie, time, accessor) = Create();
        var subjectId = UserSubjectId.New();

        cookie.Write(subjectId);

        var header = SingleSetCookie(accessor.HttpContext!);
        Assert.StartsWith(SecondFactorStateCookie.CookieName + "=", header, StringComparison.Ordinal);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);

        accessor.HttpContext!.Request.Headers.Cookie = header[..header.IndexOf(';')];
        var reader = Create(accessor: accessor).Cookie;
        Assert.True(reader.TryRead(out var read));
        Assert.Equal(subjectId, read);
        Assert.True(time.UtcNow - Start < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Always_writes_the_host_prefixed_secure_name_even_without_https()
    {
        var (cookie, _, accessor) = Create(scheme: "http");
        cookie.Write(UserSubjectId.New());

        var header = SingleSetCookie(accessor.HttpContext!);
        Assert.StartsWith(SecondFactorStateCookie.CookieName + "=", header, StringComparison.Ordinal);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_payload_older_than_five_minutes()
    {
        var (cookie, time, accessor) = Create();
        cookie.Write(UserSubjectId.New());
        var header = SingleSetCookie(accessor.HttpContext!);
        accessor.HttpContext!.Request.Headers.Cookie = header[..header.IndexOf(';')];

        time.UtcNow = Start.AddMinutes(5).AddSeconds(1);

        var reader = Create(accessor: accessor).Cookie;
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public void Rejects_tampered_and_malformed_payloads()
    {
        var (cookie, _, accessor) = Create();
        cookie.Write(UserSubjectId.New());
        var header = SingleSetCookie(accessor.HttpContext!);
        var value = header[(header.IndexOf('=') + 1)..];
        var tamperIndex = Math.Min(10, value.Length - 1);
        var tampered = value[..tamperIndex]
            + (value[tamperIndex] == 'A' ? 'B' : 'A')
            + value[(tamperIndex + 1)..];

        var tamperedContext = Create(cookieValue: tampered).Accessor;
        var reader = Create(accessor: tamperedContext).Cookie;
        Assert.False(reader.TryRead(out _));

        var protector = CreateProtector();
        var malformedContext = Create(cookieValue: protector.Protect("not json")).Accessor;
        var malformedReader = Create(accessor: malformedContext).Cookie;
        Assert.False(malformedReader.TryRead(out _));
    }

    [Fact]
    public void Clears_the_host_prefixed_cookie()
    {
        var (cookie, _, accessor) = Create();
        cookie.Clear();

        var header = SingleSetCookie(accessor.HttpContext!);
        Assert.StartsWith(SecondFactorStateCookie.CookieName + "=", header, StringComparison.Ordinal);
        Assert.Contains("expires=thu, 01 jan 1970", header, StringComparison.OrdinalIgnoreCase);
    }

    private static IDataProtector CreateProtector() =>
        Protection.CreateProtector("SecondFactorState.v1");

    private static (SecondFactorStateCookie Cookie, MutableTimeProvider Time, TestHttpContextAccessor Accessor) Create(
        string scheme = "https",
        TestHttpContextAccessor? accessor = null,
        string? cookieValue = null)
    {
        var time = accessor?.Time ?? new MutableTimeProvider(Start);
        var context = accessor?.HttpContext ?? Fakes.CreateHttpContext(scheme: scheme);
        context.Request.Scheme = scheme;

        if (cookieValue is not null)
        {
            context.Request.Headers.Cookie = $"{SecondFactorStateCookie.CookieName}={cookieValue}";
        }

        var accessorInstance = accessor ?? new TestHttpContextAccessor(context, time);
        var cookie = new SecondFactorStateCookie(
            Protection,
            accessorInstance,
            time);

        return (cookie, time, accessorInstance);
    }

    private static string SingleSetCookie(HttpContext context)
    {
        var values = context.Response.Headers.SetCookie;
        Assert.Equal(1, values.Count);
        return values[0]!;
    }
}

internal sealed class TestHttpContextAccessor(HttpContext context, MutableTimeProvider time) : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; } = context;

    public MutableTimeProvider Time { get; } = time;
}
