using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// D-0348's protection follows an entry's own tag and never its path (D-0416): through a real vault,
/// a tagged entry is asked about live every time, and anything the source cannot answer counts as
/// protected.
/// </summary>
public sealed class TagProtectionTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly CancellationToken _token = CancellationToken.None;

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-tag-protection-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("services", "Stripe", false)]
    [InlineData("services", "Database", true)]
    [InlineData("services", "Odd", true)]
    [InlineData("services", "Plain", false)]
    [InlineData("env/acme/prod", "DB", false)]
    [InlineData("services", "Db", true)]
    [InlineData("services", "Nobody", true)]
    public void The_vault_source_answers_from_the_entrys_own_tags_and_never_its_path(string group, string title, bool live)
    {
        using var vault = Seeded();
        var source = new VaultCredentialSource(() => vault);

        Assert.Equal(live, source.RequiresLiveApproval(new EntryName(group, title)));
    }

    [Fact]
    public void A_source_with_no_vault_or_an_ambiguous_name_fails_closed()
    {
        using var vault = Seeded();
        vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Plain", Password = "twice" });

        Assert.True(new VaultCredentialSource(() => null).RequiresLiveApproval(new EntryName("services", "Plain")));
        Assert.True(new VaultCredentialSource(() => vault).RequiresLiveApproval(new EntryName("services", "Plain")));
    }

    [Fact]
    public async Task A_handler_asks_every_time_for_a_protected_entry()
    {
        using var vault = Seeded();
        using var fixture = new ApproverFixture();
        var source = new VaultCredentialSource(() => vault);
        var handler = new ApproverHandler(source, new VaultEntryNameLister(() => vault), fixture.Gate, fixture.Settings);
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var first = await handler.RequestAsync(Request("services/Database"), "conn-1", _token);
        var second = await handler.RequestAsync(Request("services/Database"), "conn-1", _token);

        Assert.Equal(AuditMethod.Prompt, second.Method);
        Assert.True(handler.RequiresLiveApproval(new EntryName("services", "Odd")));

        Assert.Equal(2, fixture.Channel.Asked);
    }

    private Vault Seeded()
    {
        var vault = Vault.Create(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx"), _master);
        Add(vault, "services", "Stripe", "env:billing", "finance");
        Add(vault, "services", "Database", "env:billing:prod");
        Add(vault, "services", "Odd", "env:billing:Prod");
        Add(vault, "services", "Plain");
        Add(vault, "env/acme/prod", "DB");
        Add(vault, "services", "Db", "env:acme:prod");
        vault.Save();
        return vault;
    }

    private static void Add(Vault vault, string group, string title, params string[] tags)
    {
        vault.AddEntry(new VaultEntry { GroupPath = group, Title = title, Password = title + "-password" });

        foreach (var tag in tags)
        {
            vault.AddTag(new EntryName(group, title), tag);
        }
    }

    private static CredentialRequest Request(string entry) => new()
    {
        Entry = entry,
        Field = "password",
        Reason = "deploy billing",
        Exposure = ["services/**"],
        ClientName = "claude-code",
        ClientLabel = ApproverFixture.Agent,
    };
}
