namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class ProfileSchemaTests
{
    [Fact]
    public void Preferred_username_is_unique_and_used_for_sign_in()
    {
        Assert.True(ProfileSchema.PreferredUsername.IsUnique);
        Assert.Equal("preferred_username", ProfileSchema.PreferredUsername.Code.Value);
    }

    [Fact]
    public void Picture_is_not_queryable_and_holds_the_stable_route()
    {
        Assert.False(ProfileSchema.Picture.IsQueryable);
        Assert.Equal("picture", ProfileSchema.Picture.Code.Value);
    }

    [Fact]
    public void The_extension_schema_contains_both_attributes()
    {
        var schema = ProfileSchema.UserProfile;

        Assert.NotNull(schema);
    }
}
