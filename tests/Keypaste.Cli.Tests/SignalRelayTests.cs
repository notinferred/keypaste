using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Keypaste.Cli.Execution;
using Keypaste.Core.Launch;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>What <c>keypaste run</c>'s traps do with a signal that arrives before its child exists (D-0394).</summary>
public sealed class SignalRelayTests
{
    [Fact]
    public void A_SIGTERM_before_the_child_exists_keeps_keypaste_and_reaches_the_child_once_it_starts()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows holds nothing before the child exists; the Windows case below says what it does instead.");
        }

        using var relay = new SignalRelay(stdinRedirected: false);
        var context = new PosixSignalContext(PosixSignal.SIGTERM);
        relay.Handle(PosixSignal.SIGTERM, context);
        Assert.True(context.Cancel);

        using var child = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false })!;
        try
        {
            relay.Attach(child);

            Assert.True(child.WaitForExit(TimeSpan.FromSeconds(10)), "the held SIGTERM never reached the child");
            Assert.Equal(128 + NativeSignals.SigTerm, child.ExitCode);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill();
            }
        }
    }

    [Fact]
    public void A_SIGINT_the_terminal_delivered_before_the_child_exists_is_left_to_end_keypaste()
    {
        using var relay = new SignalRelay(stdinRedirected: false);
        var context = new PosixSignalContext(PosixSignal.SIGINT);
        relay.Handle(PosixSignal.SIGINT, context);

        Assert.False(context.Cancel);
        Assert.Null(relay.Held);
    }

    [Fact]
    public void On_Windows_every_signal_before_the_child_exists_is_left_to_end_keypaste()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("only Windows cannot relay a held signal.");
        }

        using var relay = new SignalRelay(stdinRedirected: true);
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGQUIT, PosixSignal.SIGHUP })
        {
            var context = new PosixSignalContext(signal);
            relay.Handle(signal, context);
            Assert.False(context.Cancel);
        }

        Assert.Null(relay.Held);
    }

    [Fact]
    public void A_signal_held_before_the_start_starts_nothing_and_reports_128_plus_its_number()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows holds nothing before the child exists.");
        }

        var marker = Path.Combine(Path.GetTempPath(), $"keypaste-relay-{Guid.NewGuid():N}");
        var start = new ChildStart("/usr/bin/touch", [marker], new Dictionary<string, string>());
        try
        {
            using (var quiet = new SignalRelay(stdinRedirected: false))
            {
                Assert.Equal(new ChildResult(ChildOutcome.Exited, 0, string.Empty), SystemProcessLauncher.Run(start, quiet, Process.Start));
                Assert.True(File.Exists(marker), "the control never started the child, so the case below proves nothing");
            }

            File.Delete(marker);

            using var relay = new SignalRelay(stdinRedirected: false);
            relay.Handle(PosixSignal.SIGTERM, new PosixSignalContext(PosixSignal.SIGTERM));

            Assert.Equal(new ChildResult(ChildOutcome.Interrupted, 128 + NativeSignals.SigTerm, "received SIGTERM"), SystemProcessLauncher.Run(start, relay, Process.Start));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public void A_signal_that_arrives_as_a_start_fails_stops_the_run_rather_than_being_dropped()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows holds nothing before the child exists.");
        }

        var start = new ChildStart("missing-command", [], new Dictionary<string, string>());
        Func<ProcessStartInfo, Process?>[] failures =
        [
            _ => throw new Win32Exception(2),
            _ => throw new Win32Exception(13),
            _ => null,
        ];
        ChildOutcome[] unsignalled = [ChildOutcome.NotFound, ChildOutcome.NotExecutable, ChildOutcome.Failed];

        for (var i = 0; i < failures.Length; i++)
        {
            using (var quiet = new SignalRelay(stdinRedirected: false))
            {
                Assert.Equal(unsignalled[i], SystemProcessLauncher.Run(start, quiet, failures[i]).Outcome);
            }

            using var relay = new SignalRelay(stdinRedirected: false);
            var fail = failures[i];
            var result = SystemProcessLauncher.Run(start, relay, info =>
            {
                relay.Handle(PosixSignal.SIGTERM, new PosixSignalContext(PosixSignal.SIGTERM));
                return fail(info);
            });

            Assert.Equal(new ChildResult(ChildOutcome.Interrupted, 128 + NativeSignals.SigTerm, "received SIGTERM"), result);
        }
    }
}
