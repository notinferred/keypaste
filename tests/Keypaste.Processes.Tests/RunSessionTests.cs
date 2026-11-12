using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// <c>keypaste run --session</c> takes a project's set from the process holding the vault (E.1c), across real processes: the built keypaste,
/// tests/Keypaste.AppDriver holding the vault with its prompt drawn and clicked, or keypaste approve answering in its terminal, and a child
/// that prints the set from its own environment.
/// </summary>
/// <remarks>The timeout, and what each refusal says in full, are the tests' of the session's env requests in Core.Tests and App.Tests.</remarks>
public sealed class RunSessionTests
{
    private const string _deploy = "SENTINEL-RUN-SESSION-DEPLOY-5e2b90";
    private const string _database = "SENTINEL-RUN-SESSION-DB-a17c3d";
    private static readonly string _set = $"deploy={_deploy} database={_database}";

    [Fact]
    public void With_nothing_holding_the_vault_the_run_says_so_and_starts_nothing()
    {
        using var workspace = Seeded();
        using var run = Run(workspace, "ci");
        AssertRefused(run, "nothing holds");
    }

    [Fact]
    public void The_apps_prompt_names_the_project_its_command_directory_variables_and_entries_and_Allow_starts_the_child_with_the_set()
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace);

        using (var run = Run(workspace, "ci", "case-approve"))
        {
            var prompt = app.WaitFor("^env-prompt project=");
            Assert.StartsWith("env-prompt project=ci command=", prompt, StringComparison.Ordinal);
            Assert.Contains("case-approve directory=", prompt, StringComparison.Ordinal);
            Assert.EndsWith("project keys=DB_URL,DEPLOY_KEY entries=env/ci/.env,env/ci/.env", prompt, StringComparison.Ordinal);
            app.Send("once");
            AssertReleased(run);
        }

        // An entry tagged into prod as well offers Allow once alone, and each source entry is named once.
        using (var run = Run(workspace, "tagged", "case-tagged"))
        {
            Assert.EndsWith("keys=DB_URL,DEPLOY_KEY entries=env/tagged/.env,services/Deploy", app.WaitFor("^env-prompt project=", 2), StringComparison.Ordinal);
            app.Send("once");
            AssertReleased(run);
        }

        Assert.DoesNotContain(app.Driver.Output, line => line.Contains(_deploy, StringComparison.Ordinal) || line.Contains(_database, StringComparison.Ordinal));
    }

    [Fact]
    public void Deny_a_lock_while_asked_and_a_set_keypaste_cannot_release_each_start_nothing()
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace);

        using (var run = Run(workspace, "ci", "case-deny"))
        {
            app.WaitFor("^env-prompt project=");
            app.Send("deny");
            AssertRefused(run, "said no");
        }

        using (var run = Run(workspace, "broken", "case-broken"))
        {
            AssertRefused(run, "placeholder");
        }

        Assert.Single(app.Driver.Output, line => line.StartsWith("env-prompt project=", StringComparison.Ordinal));

        // A refusal cools that request down (D-0425), so the next asks with a command of its own.
        using (var run = Run(workspace, "ci", "case-lock"))
        {
            app.WaitFor("^env-prompt project=", 2);
            app.Send("lock");
            app.WaitFor("^locked");
            AssertRefused(run, "locked");
        }
    }

    [Fact]
    public void With_keypaste_approve_holding_the_vault_o_releases_once_and_n_refuses()
    {
        using var workspace = Seeded();
        using var approve = RunningProcess.Start(Built.Keypaste, workspace.Environment, "approve", "--vault", workspace.Vault);
        approve.Send(Workspace.Master);
        approve.WaitForError("listening on");

        using (var run = Run(workspace, "ci", "agent-yes"))
        {
            approve.WaitForError("is asking for a project's variables");
            Assert.Contains(approve.Error, line => line.Contains("variables    DB_URL DEPLOY_KEY", StringComparison.Ordinal));
            Assert.Contains(approve.Error, line => line.StartsWith("  command      ", StringComparison.Ordinal) && line.Contains("agent-yes", StringComparison.Ordinal));
            approve.Send("o");
            AssertReleased(run);
        }

        using (var run = Run(workspace, "ci", "agent-no"))
        {
            approve.WaitForError("is asking for a project's variables", 2);
            approve.Send("n");
            AssertRefused(run, "said no");
        }

        Assert.DoesNotContain(approve.Error, line => line.Contains(_deploy, StringComparison.Ordinal) || line.Contains(_database, StringComparison.Ordinal));
    }

    private static Workspace Seeded()
    {
        var workspace = new Workspace();
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(workspace.Vault)!, "project"));
        EnvSet(workspace, "ci", "DEPLOY_KEY", _deploy);
        EnvSet(workspace, "ci", "DB_URL", _database);
        EnvSet(workspace, "broken", "DB_URL", "postgres://ci:{REF:P@I:0}@db");
        EnvSet(workspace, "tagged", "DB_URL", _database);

        var environment = workspace.Environment;
        environment["KEYPASTE_DRIVER_PASSWORD"] = Workspace.Master;
        using (var add = RunningProcess.Start(Built.AppDriver, environment, "raw-add", workspace.Vault, "services", "Deploy"))
        {
            add.Send("deploy-login");
            add.CloseInput();
            Assert.Equal(0, add.WaitForExit());
        }

        workspace.Keypaste("set", Workspace.Master, _deploy, "services/Deploy", "--field", "DEPLOY_KEY", "--vault", workspace.Vault);
        foreach (var environmentName in new[] { "dev", "prod" })
        {
            using var tag = RunningProcess.Start(Built.Keypaste, workspace.Environment, "env", "tag", "tagged", "services/Deploy", "-p", environmentName, "--yes", "--vault", workspace.Vault);
            tag.Send(Workspace.Master);
            tag.CloseInput();
            Assert.Equal(0, tag.WaitForExit());
        }

        return workspace;
    }

    private static void EnvSet(Workspace workspace, string project, string key, string value) =>
        workspace.Keypaste("env", Workspace.Master, value, "set", project, key, "-p", "dev", "--vault", workspace.Vault);

    /// <summary>Starts <c>keypaste run --session</c> from the project's directory, with the master password a script would pipe it.</summary>
    private static RunningProcess Run(Workspace workspace, string project, string marker = "case-run")
    {
        string[] child = OperatingSystem.IsWindows()
            ? ["cmd.exe", "/d", "/c", $"echo deploy=%DEPLOY_KEY% database=%DB_URL%& rem {marker}"]
            : ["/bin/sh", "-c", "printf \"deploy=%s database=%s\" \"${DEPLOY_KEY-unset}\" \"${DB_URL-unset}\"", marker];
        var run = RunningProcess.Start(
            Built.Keypaste,
            workspace.Environment,
            Path.Combine(Path.GetDirectoryName(workspace.Vault)!, "project"),
            ["run", "--session", "--vault", workspace.Vault, project, "--", .. child]);
        run.Send(Workspace.Master);
        run.CloseInput();
        return run;
    }

    private static void AssertReleased(RunningProcess run)
    {
        Assert.Equal(0, run.WaitForExit());
        Assert.Equal(_set, string.Join("", run.Output).Trim());
        AssertQuiet(run);
    }

    private static void AssertRefused(RunningProcess run, string why)
    {
        Assert.NotEqual(0, run.WaitForExit());
        Assert.DoesNotContain(run.Output, line => line.Length > 0);
        Assert.Contains(run.Error, line => line.Contains(why, StringComparison.Ordinal));
        AssertQuiet(run);
    }

    // The runner's own output carries no value and asks for no password: the set goes to the child alone.
    private static void AssertQuiet(RunningProcess run)
    {
        var said = string.Join('\n', run.Error);
        Assert.DoesNotContain(_deploy, said, StringComparison.Ordinal);
        Assert.DoesNotContain(_database, said, StringComparison.Ordinal);
        Assert.DoesNotContain("master password", said, StringComparison.OrdinalIgnoreCase);
    }
}
