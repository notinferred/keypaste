using Keypaste.Core.Agents;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The environments an item's preview lists, and whether an agent gets the item there without a person's answer.</summary>
public sealed class EntryEnvironmentTests : IDisposable
{
    private static readonly EntryName _stripe = new("services", "Stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-entry-environment-tests-").FullName;
    private readonly FakeAgentRules _allowing = new() { Rules = AgentRules.Defaults.Allowing(FieldCategory.ProjectVariable, confirmed: true) };
    private readonly Vault _vault;

    public EntryEnvironmentTests()
    {
        _vault = ClaimedVault.Create(Path.Combine(_directory, "v.kdbx"), EnvStoreTests.MasterPassword);
        _vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "login" });
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void An_unprotected_stage_needs_no_approval_once_project_variables_are_allowed()
    {
        Assert.True(_vault.AddTag(_stripe, "env:billing:dev"));

        Assert.Equal([new EntryEnvironment("billing", "dev", ApprovalRequired: false)], EntryEnvironment.Read(_vault, _stripe, _allowing));
    }

    [Fact]
    public void A_protected_stage_needs_approval_on_every_line_although_project_variables_are_allowed()
    {
        Assert.True(_vault.AddTag(_stripe, "env:billing:dev"));
        Assert.True(_vault.AddTag(_stripe, "env:billing:prod"));

        var environments = EntryEnvironment.Read(_vault, _stripe, _allowing)!;

        Assert.Equal(2, environments.Count);
        Assert.All(environments, environment => Assert.True(environment.ApprovalRequired));
    }
}
