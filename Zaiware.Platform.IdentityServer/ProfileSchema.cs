using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement.Profiles;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// The in-memory user profile schema. The built-in schema is extended with the two
/// attributes this application relies on: a unique sign-in username and the stable
/// avatar route. Values are stored in the existing EAV tables, so no database
/// migration is required when the schema changes.
/// </summary>
public static class ProfileSchema
{
    /// <summary>
    /// The unique username a user can sign in with. Unlike the OIDC standard attribute,
    /// this application enforces uniqueness so password sign-in cannot be ambiguous.
    /// </summary>
    public static readonly AttributeDefinition PreferredUsername = OidcStandardAttributes.PreferredUserName with
    {
        IsUnique = true,
        Description = AttributeDescription.Create("The unique username used to sign in."),
        DisplayName = AttributeDisplayName.Create("Username"),
    };

    /// <summary>
    /// The stable application route (<c>/avatars/{subjectId}</c>) for the user's avatar.
    /// Presigned object URLs are never stored here because they expire.
    /// </summary>
    public static readonly AttributeDefinition Picture = OidcStandardAttributes.Picture with
    {
        IsQueryable = false,
        Description = AttributeDescription.Create("Stable application route that serves the user's avatar."),
        DisplayName = AttributeDisplayName.Create("Avatar"),
    };

    /// <summary>
    /// The extended profile schema registered with <c>AddInMemoryDataExtensionSchemas</c>.
    /// </summary>
    public static SchemaConfiguration UserProfile => BuiltInSchemas.UserProfile.Extend([PreferredUsername, Picture]);
}
