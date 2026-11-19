using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>What kind an item is, as its row, its pane and the sidebar's places read it.</summary>
public sealed class EntryKindTests
{
    [Fact]
    public void A_lone_password_a_project_holds_is_its_environment_and_not_a_login()
    {
        var kind = EntryKinds.Of(new VaultEntry { Title = ".env", Password = "x" }, ["env:acme-api:prod"]);

        Assert.Equal(EntryKind.Environment, kind);
        Assert.Equal("Environment", EntryKinds.Label(kind));
        Assert.False(ItemPlaces.Holds(ItemPlace.Logins, kind));
        Assert.True(ItemPlaces.Holds(ItemPlace.All, kind));
    }

    [Fact]
    public void A_login_a_project_holds_stays_a_login()
    {
        var kind = EntryKinds.Of(new VaultEntry { Title = "github", Username = "me", Password = "x", Url = "https://github.com" }, ["env:acme-api:prod"]);

        Assert.Equal(EntryKind.Login, kind);
        Assert.True(ItemPlaces.Holds(ItemPlace.Logins, kind));
    }
}
