using System.Runtime.CompilerServices;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Asserts that the KeePassXC compatibility gate is still wired into CI (docs/PRODUCT.md law 4.6,
/// DECISIONS.md D-0008).
/// </summary>
/// <remarks>
/// <para>
/// This is a tripwire, not a lock. Anyone deliberately removing the gate will see this test
/// go red and delete it too. What it buys is converting *silent* removal — a merge-conflict
/// resolution, an over-eager "let's slim CI" change, an agent tidying YAML — into deliberate
/// removal, with a failure message that states the stakes. The mechanism that actually
/// prevents removal is branch protection: the three `keepassxc compat (...)` checks are
/// required on main, so a deleted job never reports and the pull request can never merge.
/// </para>
/// <para>
/// <b>The desktop app writes vaults too, since 4.2, and this gate still covers them — checked
/// rather than assumed.</b> It shares the writer: every mutation the app can make goes through
/// <see cref="Vault.Save"/> into the same vendored KeePassLib, which is the identical path the CLI
/// takes, and an inline edit's <c>&lt;History&gt;</c> element is exactly what section A of
/// <c>verify-keepassxc-writeback.sh</c> already opens. That argument is held by
/// <c>TheAppSharesTheWriterTests</c> in <c>Keypaste.App.Tests</c>, which asserts no app code writes a
/// file itself and that the app references only <c>Keypaste.Core</c>. The expiry condition is stated
/// in D-0050: the day the app writes a KDBX by any other route, <c>app.yml</c> needs a KeePassXC job
/// of its own.
/// </para>
/// </remarks>
public sealed class CompatGateIsPermanentTests
{
    [Fact]
    public void CiWorkflow_StillRunsTheKeePassXcCompatibilityGate()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

        Assert.Contains("scripts/verify-keepassxc-compat.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("keepassxc", workflow, StringComparison.OrdinalIgnoreCase);

        // The fixture must come from the shipped binary. Naming the throwaway generator again
        // would silently narrow the gate back to the vault writer alone (DECISIONS.md D-0012).
        Assert.Contains("scripts/make-compat-fixture.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Keypaste.CompatFixture", workflow, StringComparison.Ordinal);

        // The write-back direction is a separate script and therefore a separate way to lose the
        // coverage silently. Both gates enforce the same law and get the same tripwire.
        Assert.Contains("scripts/verify-keepassxc-writeback.sh", workflow, StringComparison.Ordinal);

        // Injection is the other law with no in-process test that can reach it (docs/PRODUCT.md 3.4 and
        // 4.5): the child owns the console, so only a real child can be asked what it received.
        Assert.Contains("scripts/verify-run-injection.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/verify-run-signals.sh", workflow, StringComparison.Ordinal);

        // The agent bridge has the same shape of gap: StdioServerTransport and Main are beyond
        // every in-process test, and "nothing but protocol reaches stdout" can only be asked of a
        // real spawned process (docs/PRODUCT.md laws 3.3 and 4.5).
        Assert.Contains("scripts/verify-mcp-stdio.sh", workflow, StringComparison.Ordinal);

        // And the approval flow has a third: the credential crossing a process boundary, which by
        // definition no single-process test can observe (docs/PRODUCT.md law 3.2, DECISIONS.md D-0023).
        Assert.Contains("scripts/verify-approval-e2e.sh", workflow, StringComparison.Ordinal);

        // The policy path has a fourth, and it is the only one asserting that a prompt did NOT
        // appear — which cannot be observed in-process at all, and is only worth anything paired
        // with one that did (DECISIONS.md D-0028).
        Assert.Contains("scripts/verify-policy-e2e.sh", workflow, StringComparison.Ordinal);

        // Both matrices must still name all three operating systems.
        //
        // Matched on the operating system inside the matrix line rather than on a runner label,
        // because the label belongs to whoever rents the machine and the law does not. Migrating to
        // Blacksmith renamed `windows-latest` to `blacksmith-4vcpu-windows-2025`, and a tripwire
        // looking for the old label could not tell that from Windows being dropped - it failed the
        // build for a change that took nothing away. docs/PRODUCT.md law 4.6 requires the operating system,
        // not the marketplace.
        //
        // Reading the matrix lines rather than the whole file is what keeps this honest: `windows`
        // appears in half a dozen comments about the gaps Windows has, so a substring check over
        // the document would pass with the OS gone from CI entirely.
        var matrices = workflow
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("os: [", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, matrices.Count);

        foreach (var matrix in matrices)
        {
            foreach (var os in new[] { "ubuntu", "windows", "macos" })
            {
                Assert.Contains(os, matrix, StringComparison.OrdinalIgnoreCase);
            }
        }

        // A gate that is allowed to fail is not a gate. Matched with the trailing colon so
        // this looks for the YAML key and not for the word — the warning comment in ci.yml
        // names `continue-on-error` as one of the things not to add, and a bare substring
        // check would be tripped by that comment.
        Assert.DoesNotContain("continue-on-error:", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-keepassxc-compat.sh");
        Assert.True(File.Exists(script), $"The compatibility gate script is missing: {script}");

        var fixture = Path.Combine(RepoRoot(), "scripts", "make-compat-fixture.sh");
        Assert.True(File.Exists(fixture), $"The fixture generator is missing: {fixture}");

        var text = File.ReadAllText(script);

        // Without the negative control, a gate that silently stopped testing anything would
        // report green forever — the most likely way this law dies is a no-op, not a delete.
        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);

        // Absent tooling must fail the build, never skip the gate.
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The MCP gate is the agent bridge's equivalent of the injection gate: the only place that
    /// asks a real spawned server what actually reached its stdout, which is the one thing every
    /// in-process test is structurally unable to see.
    /// </summary>
    [Fact]
    public void McpStdioScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-mcp-stdio.sh");
        Assert.True(File.Exists(script), $"The MCP stdio gate script is missing: {script}");

        var text = File.ReadAllText(script);

        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);

        // The four claims it exists to make. Named individually because dropping any one leaves a
        // script that still passes and a gate that has stopped gating.
        Assert.Contains("is not JSON", text, StringComparison.Ordinal);
        Assert.Contains("expected exactly 2 tools", text, StringComparison.Ordinal);
        Assert.Contains("isError=true", text, StringComparison.Ordinal);
        Assert.Contains("no audit log was written", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The approval gate is the only test in the repository where a credential leaves one process
    /// and arrives in another, which is the thing Stage 2.2's architecture is entirely made of.
    /// </summary>
    /// <remarks>
    /// Its four claims are named individually because dropping any one leaves a script that still
    /// passes while the flow is broken: a request must be refused when no agent is running, an
    /// approved one must return the secret, a refused one must not, and the audit log must never
    /// contain it. The last is the only one a reviewer cannot re-derive from the others.
    /// </remarks>
    [Fact]
    public void ApprovalScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-approval-e2e.sh");
        Assert.True(File.Exists(script), $"The approval gate script is missing: {script}");

        var text = File.ReadAllText(script);

        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);

        Assert.Contains("with no agent running, the request was not refused", text, StringComparison.Ordinal);
        Assert.Contains("an approved request did not return the credential", text, StringComparison.Ordinal);
        Assert.Contains("a refused request returned the credential", text, StringComparison.Ordinal);
        Assert.Contains("the audit log contains the released credential", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The policy gate is the only test in the repository that asserts a prompt did <b>not</b>
    /// happen — an absence no in-process test can see, and one that means nothing unless the same
    /// agent is shown drawing a prompt seconds later.
    /// </summary>
    /// <remarks>
    /// Its six claims are named individually because dropping any one leaves a script that passes
    /// while the policy path fails open, and because they fail in different directions: two are
    /// about a prompt appearing or not, two are about a rule reaching further than it may, one is
    /// about the all-or-nothing fallback, and one is about the operator's ceiling surviving a file
    /// the operator also wrote.
    /// </remarks>
    [Fact]
    public void PolicyScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-policy-e2e.sh");
        Assert.True(File.Exists(script), $"The policy gate script is missing: {script}");

        var text = File.ReadAllText(script);

        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);

        Assert.Contains("a policy grant put a prompt in front of the human", text, StringComparison.Ordinal);
        Assert.Contains("a request outside every policy rule did not reach a person", text, StringComparison.Ordinal);
        Assert.Contains("a policy rule released an entry outside the bridge's exposure", text, StringComparison.Ordinal);
        Assert.Contains("a rule matched a bridge the operator never labelled", text, StringComparison.Ordinal);
        Assert.Contains("a malformed policy file still granted a request without asking", text, StringComparison.Ordinal);
        Assert.Contains("a policy rule raised the TTL ceiling the operator set with --max-ttl", text, StringComparison.Ordinal);
        Assert.Contains("the audit log contains the policy-released credential", text, StringComparison.Ordinal);

        // The paired positive is the load-bearing part of every absence assertion above, and it is
        // one `#` away from being silently disabled.
        Assert.Contains("prompts_drawn", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The write-back gate proves the two claims the env convention was chosen for: that
    /// KeePassXC can edit a value keypaste stored, and that keypaste reads what KeePassXC wrote
    /// (DECISIONS.md D-0014). Losing it would leave the convention resting on an assertion nobody
    /// makes any more.
    /// </summary>
    [Fact]
    public void WriteBackScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-keepassxc-writeback.sh");
        Assert.True(File.Exists(script), $"The write-back gate script is missing: {script}");

        var text = File.ReadAllText(script);

        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);

        // The three directions it exists to cover. Named individually because dropping any one of
        // them still leaves a script that passes and a gate that has stopped gating.
        Assert.Contains("keepassxc-cli edit", text, StringComparison.Ordinal);
        Assert.Contains("env ls", text, StringComparison.Ordinal);
        Assert.Contains("db-info", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The injection gates carry the same tripwire as the compatibility ones, for the same
    /// reason: what they prove — that a child really received the value, and that nothing was
    /// written to disk doing it — is asserted nowhere else, and SECURITY.md makes both claims in
    /// prose.
    /// </summary>
    [Fact]
    public void RunGates_ExistAndKeepTheirNegativeControls()
    {
        foreach (var name in new[] { "verify-run-injection.sh", "verify-run-signals.sh" })
        {
            var script = Path.Combine(RepoRoot(), "scripts", name);
            Assert.True(File.Exists(script), $"The run gate script is missing: {script}");

            var text = File.ReadAllText(script);
            Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
            Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);
        }

        // The no-temp-file check is the narrow, testable half of what SECURITY.md promises about
        // injection. Losing it would leave the claim resting on nothing.
        var injection = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "verify-run-injection.sh"));
        Assert.Contains("TMPDIR", injection, StringComparison.Ordinal);
        Assert.Contains("-type f", injection, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the test binary's location to the directory holding the solution.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> <see cref="CallerFilePathAttribute"/>. The root props set
    /// <c>ContinuousIntegrationBuild</c> under GitHub Actions, which turns on deterministic
    /// source paths and rewrites every compile-time path to <c>/_/…</c> — so on CI, and only
    /// on CI, a <c>CallerFilePath</c> points at a directory that has never existed. The
    /// output directory is a runtime fact and survives that.
    /// <para>
    /// <c>UseArtifactsOutput</c> puts the binary at <c>artifacts/bin/&lt;project&gt;/&lt;config&gt;/</c>
    /// inside the repository, so walking up finds <c>keypaste.slnx</c>.
    /// </para>
    /// </remarks>
    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"Could not locate keypaste.slnx above '{AppContext.BaseDirectory}'. " +
                    "This test asserts on repository files and must run from inside a checkout.");
            }

            directory = parent;
        }

        return directory;
    }
}
