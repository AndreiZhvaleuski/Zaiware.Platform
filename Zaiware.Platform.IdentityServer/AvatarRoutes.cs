using Duende.UserManagement;

namespace Zaiware.Platform.IdentityServer;

/// <summary>Stable application routes for avatar objects.</summary>
public static class AvatarRoutes
{
    /// <summary>The profile's picture value for a subject (never a presigned URL).</summary>
    public static string ForSubject(UserSubjectId subjectId) => $"/avatars/{subjectId}";
}
