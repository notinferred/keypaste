using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The only code in keypaste that turns an entry into a secret on the agent path, so CORE.md law
/// 4.5 applies with full force. What it refuses matters at least as much as what it releases.
/// </summary>
/// <remarks>
/// One vault, created once: Argon2 at KDBX4 defaults is deliberately expensive, and a fixture per
/// test would make this the slowest file in the suite for no extra coverage.
/// </remarks>
public sealed class VaultCredentialSourceTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    /// <summary>
    /// Four different sentinels in one entry. Distinct on purpose: "the password came back" would
    /// pass for a selector that returns whichever field it likes, so each field has to be
    /// identifiable on its own.
    /// </summary>
    internal const string SentinelPassword = "SENTINEL-PASSWORD-4a91";

    internal const string SentinelUsername = "SENTINEL-USERNAME-b3d7";
    internal const string SentinelUrl = "https://sentinel-url-c8e2.example";
    internal const string SentinelNotes = "SENTINEL-NOTES-d5f0";

    private readonly string _directory;
    private readonly Vault _vault;

    public VaultCredentialSourceTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-credential-tests-").FullName;
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), MasterPassword);

        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "env/dev",
            Title = "STRIPE_KEY",
            Password = SentinelPassword,
            Username = SentinelUsername,
            Url = SentinelUrl,
            Notes = SentinelNotes,
        });

        _vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "BLANK", Password = "" });

        // The collision EntryHandle exists for: group "a" + title "b/c" and group "a/b" + title
        // "c" produce the identical VaultEntry.Path "a/b/c".
        _vault.AddEntry(new VaultEntry { GroupPath = "a", Title = "b/c", Password = "slash-in-title" });
        _vault.AddEntry(new VaultEntry { GroupPath = "a/b", Title = "c", Password = "slash-in-group" });

        // An entry whose title is shaped exactly like a handle, which is why resolution has to
        // fall back to a path match rather than stopping when no handle matches.
        _vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "k1_0123456789abcdef", Password = "titled-like-a-handle" });
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private VaultCredentialSource Source() => new(() => _vault);

    private static VaultCredentialSource Locked() => new(() => null);

    private EntryName Resolve(string entryArgument)
    {
        Assert.True(Source().TryResolve(entryArgument, out var name, out var failure), failure.ToString());
        Assert.NotNull(name);
        Assert.Equal(CredentialFailure.None, failure);
        return name;
    }

    private string Read(string entryArgument, string field)
    {
        using var released = ReadField(entryArgument, field);
        return released.Value.ToString();
    }

    private ReleasedField ReadField(string entryArgument, string field)
    {
        Assert.True(Source().TryRead(Resolve(entryArgument), field, out var value, out var failure), failure.ToString());
        Assert.NotNull(value);
        return value;
    }

    /// <summary>
    /// The whole point of <see cref="ReleasedField"/>, asserted against a vault where all four
    /// fields are populated and distinguishable. A selector that returned the wrong field, or a
    /// type that carried more than one, fails here.
    /// </summary>
    [Theory]
    [InlineData("password", SentinelPassword)]
    [InlineData("username", SentinelUsername)]
    [InlineData("url", SentinelUrl)]
    [InlineData("notes", SentinelNotes)]
    public void EachFieldReleases_ExactlyItself(string field, string expected)
    {
        using var released = ReadField("env/dev/STRIPE_KEY", field);

        Assert.Equal(field, released.Field, StringComparer.Ordinal);
        Assert.Equal(expected, released.Value.ToString(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Stated as an invariant rather than as four equality checks, because it is the claim
    /// THREATS.md T-8 makes: the other three fields are not merely unselected, they are not
    /// present in the released object at all.
    /// </summary>
    [Fact]
    public void TheOtherThreeFields_AreNowhereInTheReleasedValue()
    {
        using var released = ReadField("env/dev/STRIPE_KEY", "password");

        var text = released.Value.ToString();

        Assert.DoesNotContain(SentinelUsername, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelNotes, text, StringComparison.Ordinal);
    }

    [Fact]
    public void APathResolvesToTheEntryItNames()
    {
        var name = Resolve("env/dev/STRIPE_KEY");

        Assert.Equal("env/dev", name.GroupPath, StringComparer.Ordinal);
        Assert.Equal("STRIPE_KEY", name.Title, StringComparer.Ordinal);
    }

    [Fact]
    public void AHandleResolvesToTheEntryItWasDerivedFrom()
    {
        var handle = EntryHandle.For(new EntryName("env/dev", "STRIPE_KEY"));

        Assert.Equal(SentinelPassword, Read(handle, "password"), StringComparer.Ordinal);
    }

    /// <summary>
    /// The reason resolution does not stop at "no handle matched". An entry may legitimately be
    /// titled something handle-shaped, and if a failed handle lookup were final that entry would
    /// be permanently unreachable — which is a data-loss bug wearing a security bug's clothes.
    /// </summary>
    [Fact]
    public void AnEntryTitledLikeAHandle_IsStillReachableByItsPath()
    {
        Assert.Equal("titled-like-a-handle", Read("env/dev/k1_0123456789abcdef", "password"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Both halves in one test, because either alone would be misleading. Refusing the ambiguous
    /// path is the fail-closed half; still reaching each entry by handle is what stops that
    /// refusal from being a permanent lockout, and is the entire justification for
    /// <see cref="EntryHandle"/>'s NUL separator.
    /// </summary>
    [Fact]
    public void ACollidingPath_IsRefused_ButEachEntryIsStillReachableByItsHandle()
    {
        Assert.False(Source().TryResolve("a/b/c", out var name, out var failure));
        Assert.Null(name);
        Assert.Equal(CredentialFailure.Ambiguous, failure);

        Assert.Equal("slash-in-title", Read(EntryHandle.For(new EntryName("a", "b/c")), "password"), StringComparer.Ordinal);
        Assert.Equal("slash-in-group", Read(EntryHandle.For(new EntryName("a/b", "c")), "password"), StringComparer.Ordinal);
    }

    [Fact]
    public void ALockedVault_ResolvesNothingAndReadsNothing()
    {
        Assert.False(Locked().TryResolve("env/dev/STRIPE_KEY", out var name, out var resolveFailure));
        Assert.Null(name);
        Assert.Equal(CredentialFailure.VaultLocked, resolveFailure);

        var released = Locked().TryRead(new EntryName("env/dev", "STRIPE_KEY"), "password", out var value, out var readFailure);

        // The using is what satisfies CA2000. It is also the honest shape: the assertion is that
        // nothing came back, and if that ever fails, whatever did come back still has to be zeroed.
        using (value)
        {
            Assert.False(released);
            Assert.Null(value);
            Assert.Equal(CredentialFailure.VaultLocked, readFailure);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("env/dev/NOT_THERE")]
    [InlineData("k1_ffffffffffffffff")]
    [InlineData("env/dev")]
    [InlineData("/env/dev/STRIPE_KEY")]
    public void ANameNothingAnswersTo_IsNotFound(string entryArgument)
    {
        Assert.False(Source().TryResolve(entryArgument, out var name, out var failure));
        Assert.Null(name);
        Assert.Equal(CredentialFailure.NotFound, failure);
    }

    /// <summary>
    /// Case-sensitive, ordinal, and deliberately so: a looser match is a wider match, and widening
    /// what an agent can name is not something this type may do by accident.
    /// </summary>
    [Fact]
    public void ResolutionIsCaseSensitive()
    {
        Assert.False(Source().TryResolve("ENV/DEV/STRIPE_KEY", out _, out var failure));
        Assert.Equal(CredentialFailure.NotFound, failure);
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("secret")]
    [InlineData("")]
    [InlineData("totp")]
    public void AFieldKeypasteDoesNotRelease_IsRefused(string field)
    {
        var released = Source().TryRead(new EntryName("env/dev", "STRIPE_KEY"), field, out var value, out var failure);

        using (value)
        {
            Assert.False(released);
            Assert.Null(value);
            Assert.Equal(CredentialFailure.NoSuchField, failure);
        }
    }

    /// <summary>
    /// An empty field is a misconfigured vault, and handing an agent <c>""</c> as though it were a
    /// credential would hide that behind a call that looks like it worked.
    /// </summary>
    [Fact]
    public void AnEmptyField_IsRefusedRatherThanReleased()
    {
        var released = Source().TryRead(new EntryName("env/dev", "BLANK"), "password", out var value, out var failure);

        using (value)
        {
            Assert.False(released);
            Assert.Null(value);
            Assert.Equal(CredentialFailure.Empty, failure);
        }
    }

    /// <summary>
    /// The other half of the rule above: an entry whose <em>other</em> fields are empty still
    /// releases the one that is not, so "refuse empty" cannot quietly become "refuse".
    /// </summary>
    [Fact]
    public void AnEntryWithOnlyOnePopulatedField_StillReleasesThatOne()
    {
        _vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "ONLY_URL", Url = "https://only.example" });

        Assert.Equal("https://only.example", Read("env/dev/ONLY_URL", "url"), StringComparer.Ordinal);
        Assert.False(Source().TryRead(new EntryName("env/dev", "ONLY_URL"), "password", out _, out var failure));
        Assert.Equal(CredentialFailure.Empty, failure);
    }

    [Fact]
    public void ReadingAnEntryThatIsNoLongerThere_IsNotFound()
    {
        var released = Source().TryRead(new EntryName("env/dev", "VANISHED"), "password", out var value, out var failure);

        using (value)
        {
            Assert.False(released);
            Assert.Null(value);
            Assert.Equal(CredentialFailure.NotFound, failure);
        }
    }

    /// <summary>
    /// A vault disposed under the source is an error path, and CORE.md law 3.7 says an error path
    /// denies. It must not surface as an exception out of a tool call.
    /// </summary>
    [Fact]
    public void ADisposedVault_FailsClosedRatherThanThrowing()
    {
        var vault = Vault.Create(Path.Combine(_directory, "disposed.kdbx"), MasterPassword);
        vault.Dispose();

        var source = new VaultCredentialSource(() => vault);

        Assert.False(source.TryResolve("env/dev/STRIPE_KEY", out _, out var failure));
        Assert.Equal(CredentialFailure.Failed, failure);
    }

    [Fact]
    public void TheSourceRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new VaultCredentialSource(null!));
        Assert.Throws<ArgumentNullException>(() => Source().TryResolve(null!, out _, out _));
        Assert.Throws<ArgumentNullException>(() => Source().TryRead(null!, "password", out _, out _));
        Assert.Throws<ArgumentNullException>(() => Source().TryRead(new EntryName("a", "b"), null!, out _, out _));
    }
}
