using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>What Always allow offers, for a test naming a project environment.</summary>
internal static class TestOffers
{
    internal static AlwaysOffer Project(string name)
    {
        Assert.True(ProjectScope.TryParseName(name, out var scope, out var error), error);

        return new AlwaysOffer(AccessEdit.Automatic(scope), scope.Name);
    }
}
