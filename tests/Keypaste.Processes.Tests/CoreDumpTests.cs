using Xunit;
using static System.FormattableString;

namespace Keypaste.Processes.Tests;

/// <summary>keypaste lowers its core dump limit to zero as it starts, so a crash writes none of its memory to disk, and on Linux keypaste approve is closed to other processes.</summary>
public sealed class CoreDumpTests
{
    [Fact]
    public void Keypaste_run_starts_its_command_under_a_core_dump_limit_of_zero_raised_back_by_choice()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no core dump limit.");

        using var workspace = new Workspace();
        using (var set = RunningProcess.Start(Built.Keypaste, workspace.Environment, "env", "set", "ci", "KEY=value", "--vault", workspace.Vault))
        {
            set.Send(Workspace.Master);
            set.CloseInput();
            Assert.Equal(0, set.WaitForExit());
        }

        using var run = RunningProcess.Start(
            "/bin/sh",
            workspace.Environment,
            "-c",
            "ulimit -S -c \"$(ulimit -H -c)\" && ulimit -S -c && exec \"$@\"",
            "sh",
            Built.Keypaste,
            "run",
            "ci",
            "--vault",
            workspace.Vault,
            "--",
            "/bin/sh",
            "-c",
            "ulimit -S -c; ulimit -S -c \"$(ulimit -H -c)\" && ulimit -S -c");
        run.Send(Workspace.Master);
        run.CloseInput();
        Assert.Equal(0, run.WaitForExit());

        var started = run.Output[0];
        Assert.SkipWhen(started == "0", "The hard core dump limit here is zero, so keypaste has no limit to lower.");
        Assert.Equal(["0", started], run.Output.Skip(1));
    }

    [Fact]
    public void Keypaste_approve_on_Linux_is_closed_to_other_processes_of_its_user()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux keeps the flag.");

        using var workspace = new Workspace();
        using var approve = RunningProcess.Start(Built.Keypaste, workspace.Environment, "approve", "--vault", workspace.Vault);
        approve.Send(Workspace.Master);
        approve.WaitForError("listening on");
        using var waiting = RunningProcess.Start("/bin/cat", workspace.Environment);

        Assert.NotEmpty(File.ReadAllBytes(Invariant($"/proc/{waiting.Id}/environ")));
        Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(Invariant($"/proc/{approve.Id}/environ")));
    }
}
