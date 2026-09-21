using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
/// <para>
/// Since 4.8 the app <b>creates</b> vaults too, and the same argument carries it: creation goes
/// through <c>VaultCreation</c>, which is what <c>keypaste init</c> calls and what
/// <c>make-compat-fixture.sh</c> drives to build the fixture this gate opens. That tripwire forbids
/// <c>Vault.Create(</c> in app code, so the day creation stops being shared it fails there first.
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
        // coverage silently. All three gates enforce the same law and get the same tripwire.
        Assert.Contains("scripts/verify-keepassxc-writeback.sh", workflow, StringComparison.Ordinal);

        // Restoring a revision rewrites an entry and adds a history item, and V.2a gave it no
        // shipped surface, so nothing else asks KeePassXC to read the result.
        Assert.Contains("scripts/verify-keepassxc-history.sh", workflow, StringComparison.Ordinal);

        // Deleting moves an entry into the KDBX recycle bin and raises the file to KDBX 4.1 so it
        // can record where the entry came from. Both are claims about the format that only real
        // KeePassXC can settle (V.3a).
        Assert.Contains("scripts/verify-keepassxc-recyclebin.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/verify-keepassxc-backup.sh", workflow, StringComparison.Ordinal);

        // Renaming and moving make the opposite claim about the same format byte: an organized
        // vault stays KDBX 4.0, and only a deletion may raise it (V.5a).
        Assert.Contains("scripts/verify-keepassxc-organize.sh", workflow, StringComparison.Ordinal);

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

        // The compat job carries no job-level `if:` at all: it runs on every push to main, every pull
        // request, every tag and every dispatch. A condition here is how "permanent" would quietly
        // stop meaning anything, so any job-level `if:` is a red build.
        var compat = JobBlock(workflow, "compat");
        var jobLevel = compat.Where(line => line.StartsWith("    if:", StringComparison.Ordinal) && !line.StartsWith("     ", StringComparison.Ordinal)).ToList();
        Assert.Empty(jobLevel);

        // A step may carry an `if:` only to pick which operating system installs keepassxc-cli. A
        // step that could skip the fixture, the compat script or the write-back script is the same
        // hole as a job-level skip, one indent deeper.
        var stepLevel = compat.Where(line => line.TrimStart().StartsWith("if:", StringComparison.Ordinal)).Except(jobLevel);
        Assert.All(stepLevel, line => Assert.StartsWith("if: runner.os", line.Trim(), StringComparison.Ordinal));
    }

    /// <summary>
    /// The raw lines of one top-level job in <c>ci.yml</c>, from the line after its name to the next
    /// job. Indentation is the only structure YAML gives us here, and two spaces is what the file
    /// uses for the jobs map; anything at that indent that is not a comment starts the next job.
    /// Lines are not trimmed, because the indent is what tells a job-level key from a step-level one.
    /// </summary>
    private static List<string> JobBlock(string workflow, string job)
    {
        var lines = workflow.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(line => line == $"  {job}:");
        Assert.True(start >= 0, $"ci.yml has no job named '{job}'");

        var block = new List<string>();
        for (var i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var isNextJob = line.Length > 2 && line[0] == ' ' && line[1] == ' ' && line[2] != ' ' && line[2] != '#';
            if (isNextJob)
            {
                break;
            }

            block.Add(line);
        }

        return block;
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
    /// The history gate is the only place KeePassXC is asked to read a vault keypaste restored a
    /// revision into. V.2b put a restore on the entry pane, and a bash gate cannot drive a desktop
    /// window, so the script keeps driving the same <c>RestoreRevision</c> and <c>Save</c> through
    /// its own binary (D-0230). Losing it would leave law 4.6 unchecked on the one path that
    /// rewrites an entry's fields and its history at once (DECISIONS.md D-0228).
    /// </summary>
    [Fact]
    public void HistoryScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-keepassxc-history.sh");
        Assert.True(File.Exists(script), $"The history gate script is missing: {script}");

        var text = File.ReadAllText(script);

        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);

        // The two readers it needs: the restored value as current, and the XML export, which is the
        // only thing keepassxc-cli offers that can see a history item at all.
        Assert.Contains("show -a Password", text, StringComparison.Ordinal);
        Assert.Contains("export -f xml", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The organize gate's twin assertion. The recycle-bin gate holds the minor-version byte at
    /// <c>01</c> and this one holds it at <c>00</c>; losing either is how the reader floor changes
    /// silently, in the direction nobody would think to look.
    /// </summary>
    [Fact]
    public void OrganizeScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-keepassxc-organize.sh");
        Assert.True(File.Exists(script), $"The organize gate script is missing: {script}");

        var text = File.ReadAllText(script);

        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);

        // The seeding stays the shipped binary's (D-0012); only the four acts no CLI verb performs
        // go through the driver.
        Assert.Contains("\"$kp\" env set", text, StringComparison.Ordinal);
        Assert.Contains("\"$kp\" add", text, StringComparison.Ordinal);
        Assert.Contains("Keypaste.VaultRestorer", text, StringComparison.Ordinal);

        // Only `ls -R -f` sees a group with no entries in it, which is the whole of what creating
        // one does; `show` reads the moved value; the XML is the only reader that can show
        // PreviousParentGroup is absent.
        Assert.Contains("ls -R -f", text, StringComparison.Ordinal);
        Assert.Contains("show -a Password", text, StringComparison.Ordinal);
        Assert.Contains("export -f xml", text, StringComparison.Ordinal);

        // The minor-version byte, asserted as 00 here and as 01 by the recycle-bin gate.
        Assert.Contains("hdr:16:2", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The recycle-bin gate is the only place KeePassXC is asked to read a vault keypaste deleted
    /// into, restored out of and purged. It is also the only check that the file keypaste writes
    /// is KDBX 4.1 once something has been recycled: at 4.0 the format cannot carry
    /// <c>PreviousParentGroup</c>, so the deletion would still look right and a restore after a
    /// reopen would have lost where the entry belonged. V.3b put the listing, the restore and the
    /// purge on the desktop, which a bash gate cannot drive, so they stay with the driver (D-0254).
    /// </summary>
    [Fact]
    public void RecycleBinScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-keepassxc-recyclebin.sh");
        Assert.True(File.Exists(script), $"The recycle bin gate script is missing: {script}");

        var text = File.ReadAllText(script);

        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);

        // The deletion itself must stay the shipped binary's (D-0012); only what no shipped
        // surface can do goes through the driver.
        Assert.Contains("\"$kp\" rm", text, StringComparison.Ordinal);

        // The three readers it needs: where KeePassXC now sees the entry, what it holds there,
        // and the XML, which is the only thing keepassxc-cli offers that can show
        // PreviousParentGroup and DeletedObjects at all.
        Assert.Contains("ls -R -f", text, StringComparison.Ordinal);
        Assert.Contains("show -a Password", text, StringComparison.Ordinal);
        Assert.Contains("export -f xml", text, StringComparison.Ordinal);

        // The minor-version byte. Nothing else in the repository would notice it going back to 0.
        Assert.Contains("hdr:16:2", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The backup gate is the only place KeePassXC is asked to open a file keypaste produced by
    /// copying rather than by writing, and the only check that what a save preserved still holds
    /// the values that save replaced. A backup that is the right size and unreadable would pass
    /// every other check in the repository, which is why presence is never what this asserts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading a backup needs no driver, and that is itself the claim: a backup is an ordinary KDBX
    /// file, so <c>keepassxc-cli</c> and <c>keypaste --vault</c> both read one directly. A gate that
    /// had to reach for a special reader would be evidence the bytes were not simply copied.
    /// </para>
    /// <para>
    /// Putting one back and exporting are acts the desktop performs and no command line does, so
    /// since V.4b those two go through <c>Keypaste.VaultRestorer</c>, for D-0254's reason. What is
    /// pinned is that the restored vault is held to the backup's bytes and not merely opened, and
    /// each claim a regression would have to delete to pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void BackupScript_ExistsAndKeepsItsNegativeControl()
    {
        var script = Path.Combine(RepoRoot(), "scripts", "verify-keepassxc-backup.sh");
        Assert.True(File.Exists(script), $"The backup gate script is missing: {script}");

        var text = File.ReadAllText(script);

        Assert.Contains("NEGATIVE CONTROL", text, StringComparison.Ordinal);
        Assert.Contains("must never be skipped or soft-passed", text, StringComparison.Ordinal);

        // Every save under test is the shipped binary's (D-0012), so the backup examined is one a
        // real command produced rather than one the gate arranged.
        Assert.Contains("\"$kp\" env set", text, StringComparison.Ordinal);

        // The two readers that make this more than a directory listing: KeePassXC opening the
        // backup, and the value it reads back out of it.
        Assert.Contains("db-info", text, StringComparison.Ordinal);
        Assert.Contains("show -a Password", text, StringComparison.Ordinal);

        // The refusal. A save that cannot take a backup must leave the vault byte-identical, and
        // od over the whole file is what says so.
        Assert.Contains("od -An -v -tx1", text, StringComparison.Ordinal);

        // The restore and the export (V.4b): the driver that performs them, the byte comparison a
        // re-serialising restore would fail, and the four claims themselves.
        Assert.Contains("Keypaste.VaultRestorer", text, StringComparison.Ordinal);
        Assert.Contains("backup-restore", text, StringComparison.Ordinal);
        Assert.Contains("vault-export", text, StringComparison.Ordinal);
        Assert.Contains("cmp -s", text, StringComparison.Ordinal);
        Assert.Contains("the restored vault is not the backup's bytes", text, StringComparison.Ordinal);
        Assert.Contains("the vault a restore replaced was not kept", text, StringComparison.Ordinal);
        Assert.Contains("a restore pruned", text, StringComparison.Ordinal);
        Assert.Contains("the export does not open in KeePassXC", text, StringComparison.Ordinal);

        // The floor is defeated by renaming, never by a knob. A gate that could set an environment
        // variable to shorten it would be proving a configuration the product does not ship.
        Assert.DoesNotContain("KEYPASTE_BACKUP", text, StringComparison.Ordinal);
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

    /// <summary>
    /// Every third-party action is pinned to a commit, never to a tag.
    /// </summary>
    /// <remarks>
    /// A tag is a name its owner can move. Pinning to one means the workflows that build and publish
    /// this project's binaries run whatever that owner points it at tomorrow, which is a supply-chain
    /// hole in the one place a secrets tool can least afford one — <c>release.yml</c> is what puts
    /// bytes on <c>dl.keypaste.com</c>. Most of the repository already pinned by commit; thirteen
    /// references did not, and drifted back because dependabot proposes whatever form it finds. This
    /// is the check that makes the convention hold rather than being re-remembered.
    /// </remarks>
    [Fact]
    public void EveryActionIsPinnedToACommitAndNotToATag()
    {
        var workflows = Directory.GetFiles(
            Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml");

        Assert.NotEmpty(workflows);

        List<string> unpinned = [];
        foreach (var file in workflows)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var match = Regex.Match(lines[i], @"uses:\s*(?<ref>[^\s#]+)");
                if (!match.Success)
                {
                    continue;
                }

                var reference = match.Groups["ref"].Value;

                // A local composite action is this repository's own file, versioned with it.
                if (reference.StartsWith('.'))
                {
                    continue;
                }

                var at = reference.LastIndexOf('@');
                var pin = at < 0 ? string.Empty : reference[(at + 1)..];

                if (!Regex.IsMatch(pin, "^[0-9a-f]{40}$"))
                {
                    unpinned.Add($"{Path.GetFileName(file)}:{i + 1} {reference}");
                }
            }
        }

        Assert.True(
            unpinned.Count == 0,
            "these actions are pinned to something mutable:\n  "
            + string.Join("\n  ", unpinned));
    }
}
