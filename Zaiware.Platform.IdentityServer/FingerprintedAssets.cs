using System.Text.RegularExpressions;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Recognises the content-fingerprinted static asset paths produced by
/// <c>MapStaticAssets</c> (for example <c>tokens.o0euuicwcg.css</c> or
/// <c>Assembly.ct1mhd5pqj.styles.css</c>), which are safe to cache immutably.
/// </summary>
public static partial class FingerprintedAssets
{
    /// <summary>The compiled fingerprint pattern.</summary>
    [GeneratedRegex(
        @"\.[a-z0-9]{8,12}\.(?:[a-z0-9]+\.)?(?:css|js|woff2?|svg|png|webp|jpg|jpeg)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex Path();
}
