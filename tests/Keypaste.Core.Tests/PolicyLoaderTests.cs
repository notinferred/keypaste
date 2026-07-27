using Keypaste.Core.Audit;
using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Reading the file, and the six states it can be in.
/// </summary>
/// <remarks>
/// All six are indistinguishable to an <em>agent</em> — every one of them means "everything
/// prompts", and telling them apart would let a request find out whether the human has a policy at
/// all. All six are distinct on the <em>operator's</em> terminal, because "I wrote a rule and it is
/// not working" and "I have no rules" need different next steps.
/// </remarks>
public sealed class PolicyLoaderTests : IDisposable
{
    private readonly string _home =
        Path.Combine(Path.GetTempPath(), "keypaste-policy-" + Guid.NewGuid().ToString("N")[..12]);

    public PolicyLoaderTests() => Directory.CreateDirectory(_home);

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void AnAbsentFile_IsNotAnError_AndSaysEverythingIsShownToYou()
    {
        var load = PolicyLoader.Load(PolicyPath);

        Assert.Equal(PolicyStatus.Absent, load.Status);
        Assert.False(load.HasRules);
        Assert.Empty(load.Rules.Rules);
        Assert.Empty(load.Digest);
        Assert.Contains("every request is shown to you", load.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "a zero-byte file")]
    [InlineData("# nothing but a comment\n", "comments only")]
    public void AFileDeclaringNoRules_IsEmptyRatherThanRejected(string text, string what)
    {
        var load = Write(text);

        Assert.True(load.Status == PolicyStatus.Empty, what);
        Assert.False(load.HasRules);
        Assert.Contains("no rules in", load.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AWellFormedFile_IsInForce_AndItsReasonNamesTheCountAndTheDigest()
    {
        var load = Write(PolicyRuleTests.Valid);

        Assert.Equal(PolicyStatus.InForce, load.Status);
        Assert.True(load.HasRules);
        Assert.Equal("allow#1", Assert.Single(load.Rules.Rules).Id);
        Assert.StartsWith("sha256:", load.Digest, StringComparison.Ordinal);
        Assert.Contains("1 rule from", load.Reason, StringComparison.Ordinal);
        Assert.Contains(load.Digest, load.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The property the whole stage rests on. Two perfectly good rules and one bad one means
    /// <b>zero</b> rules, not two — a policy that is partly wrong says nothing, because there is no
    /// way to know whether the difference between what it says and what was meant is narrower or
    /// wider.
    /// </summary>
    [Fact]
    public void AMalformedFile_IsIgnoredWhole_NotUpToTheBadLine()
    {
        var load = Write(PolicyRuleTests.Valid + "\n\n" + PolicyRuleTests.Valid + "\n\n[[allow]]\nclient = \"x\"\n");

        Assert.Equal(PolicyStatus.Rejected, load.Status);
        Assert.Empty(load.Rules.Rules);
        Assert.Contains("is NOT in force", load.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[[allow]]\nclient = \"a\"\nclient = \"b\"\n", "line 3", "a syntax error")]
    [InlineData("[[allow]]\nclientt = \"x\"\n", "line 2", "an unknown key")]
    [InlineData("[[deny]]\nclient = \"x\"\n", "line 1", "an unknown section")]
    public void ARejectionNamesTheLineToLookAt(string text, string line, string what)
    {
        var load = Write(text);

        Assert.True(load.Status == PolicyStatus.Rejected, what);
        Assert.Contains(line, load.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotUtf8_IsRejected()
    {
        File.WriteAllBytes(PolicyPath, [0xFF, 0xFE, 0x41, 0x00]);

        var load = PolicyLoader.Load(PolicyPath);

        Assert.Equal(PolicyStatus.Rejected, load.Status);
        Assert.Contains("UTF-8", load.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Refused on its length rather than read: this runs inside the process holding an unlocked
    /// vault, so a wrong path pointed at something enormous must cost a stat and not a read.
    /// </summary>
    [Fact]
    public void AFileOverTheSizeCap_IsRejectedWithoutBeingRead()
    {
        File.WriteAllBytes(PolicyPath, new byte[Toml.MaximumBytes + 1]);

        var load = PolicyLoader.Load(PolicyPath);

        Assert.Equal(PolicyStatus.Rejected, load.Status);
        Assert.Contains("not a policy file", load.Reason, StringComparison.Ordinal);
        Assert.Empty(load.Digest);
    }

    /// <summary>
    /// A policy file is an authorization document: anything that can write it can grant an agent
    /// silent access to a credential. It is refused rather than repaired — repairing it would be a
    /// race, and it would erase the evidence.
    /// </summary>
    [Fact]
    public void APolicyFileWritableByOthers_IsRefusedAndNotRepaired()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no owner-only file mode. SECURITY.md states that gap rather than implying a check keypaste did not make.");
            return;
        }

        Write(PolicyRuleTests.Valid);
        File.SetUnixFileMode(
            PolicyPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite);

        var load = PolicyLoader.Load(PolicyPath);

        Assert.Equal(PolicyStatus.Rejected, load.Status);
        Assert.Contains("writable by users other than its owner", load.Reason, StringComparison.Ordinal);

        // Refused, never repaired: the mode is exactly as it was found.
        Assert.True((File.GetUnixFileMode(PolicyPath) & UnixFileMode.OtherWrite) != 0);
    }

    [Fact]
    public void ADirectoryWritableByOthers_IsRefusedToo()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has no owner-only directory mode; see the file test above.");
            return;
        }

        Write(PolicyRuleTests.Valid);
        var original = File.GetUnixFileMode(_home);

        try
        {
            File.SetUnixFileMode(_home, original | UnixFileMode.OtherWrite);

            var load = PolicyLoader.Load(PolicyPath);

            Assert.Equal(PolicyStatus.Rejected, load.Status);
            Assert.Contains("writable by users other than its owner", load.Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(_home, original);
        }
    }

    /// <summary>
    /// The digest is over the bytes actually parsed, so an operator can tell whether what
    /// <c>keypaste policy ls</c> shows them is the same file the running approver read.
    /// </summary>
    [Fact]
    public void TheDigestFollowsTheBytes()
    {
        var first = Write(PolicyRuleTests.Valid).Digest;
        var again = Write(PolicyRuleTests.Valid).Digest;
        var edited = Write(PolicyRuleTests.Valid.Replace("300", "60", StringComparison.Ordinal)).Digest;

        Assert.Equal(first, again);
        Assert.NotEqual(first, edited);
        Assert.Equal("sha256:".Length + PolicyLoader.DigestLength, first.Length);
    }

    /// <summary>
    /// Every state must reach an agent as the same thing: no rules. Distinguishing them would let a
    /// request find out whether the human has a policy at all, and — worse — invite a per-entry
    /// diagnostic, which is the enumeration oracle D-0027 already closed once.
    /// </summary>
    [Fact]
    public void EveryUnusableState_LeavesTheSameThingBehind_NoRules()
    {
        var loads = new List<PolicyLoad> { PolicyLoader.Load(PolicyPath) };

        foreach (var text in new[] { string.Empty, "# comment\n", "[[deny]]\n", "[[allow]]\nclient = \"x\"\n" })
        {
            loads.Add(Write(text));
        }

        foreach (var load in loads)
        {
            Assert.Empty(load.Rules.Rules);
            Assert.False(load.HasRules);
            Assert.False(load.Rules.TryMatch("claude-code", new EntryName("env/dev", "K"), "password", out _));
        }
    }

    [Fact]
    public void TheHomeDirectoryRuleIsSharedWithTheAuditLog()
    {
        Assert.Equal(
            Path.Combine(KeypasteHome.Resolve(_home), KeypasteHome.PolicyFileName),
            KeypasteHome.PolicyPath(_home));

        Assert.Equal(
            Path.GetDirectoryName(KeypasteHome.AuditPath(_home)),
            Path.GetDirectoryName(KeypasteHome.PolicyPath(_home)));
    }

    [Fact]
    public void Load_RejectsNull() => Assert.Throws<ArgumentNullException>(() => PolicyLoader.Load(null!));

    private string PolicyPath => Path.Combine(_home, KeypasteHome.PolicyFileName);

    private PolicyLoad Write(string text)
    {
        File.WriteAllText(PolicyPath, text);
        return PolicyLoader.Load(PolicyPath);
    }
}
