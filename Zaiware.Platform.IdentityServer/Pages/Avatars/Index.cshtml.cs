using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Avatars;

/// <summary>
/// Serves a user's avatar. Authenticated browsers are redirected to a fresh, short-lived
/// presigned URL when an object exists; otherwise the response is an initials placeholder.
/// The bucket is never exposed directly.
/// </summary>
[Authorize]
public sealed class IndexModel(
    AvatarStorage avatarStorage,
    IUserProfileSelfService profiles,
    ILogger<IndexModel> logger) : PageModel
{
    private static readonly AttributeCode EmailCode = AttributeCode.Create("email");

    public async Task<IActionResult> OnGetAsync(string subjectId)
    {
        if (!UserSubjectId.TryCreate(subjectId, out var id))
        {
            return NotFound();
        }

        var objectKey = id.ToString();

        try
        {
            if (await avatarStorage.ExistsAsync(objectKey, HttpContext.RequestAborted))
            {
                Response.Headers.CacheControl = "private, no-store";
                return Redirect(avatarStorage.CreatePresignedGetUrl(objectKey, TimeSpan.FromMinutes(5)));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not read the avatar object for subject {SubjectId}", objectKey);
        }

        var profile = await profiles.TryGetAsync(id, HttpContext.RequestAborted);
        var svg = AvatarPlaceholder.Create(
            profile is null ? null : TryGetString(profile, OidcStandardAttributes.Name.Code),
            profile is null ? null : TryGetString(profile, ProfileSchema.PreferredUsername.Code),
            profile is null ? null : TryGetString(profile, EmailCode));

        Response.Headers.CacheControl = "private, max-age=60";
        return Content(svg, "image/svg+xml");
    }

    private static string? TryGetString(UserProfile profile, AttributeCode code) =>
        profile.Attributes.TryGetValue(code, out var attribute)
        && attribute.TryGetValue<string>(out var text)
        && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
