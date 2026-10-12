using Keypaste.Core.Linking;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>A terminal link that answers as the test decided and counts what it was asked, so nothing is linked on the machine running the tests.</summary>
internal sealed class FakeCliLink(CliLinkStatus status) : ICliLink
{
    internal const string Location = "/home/person/.local/bin/keypaste";

    public string LinkPath => Status.LinkPath;

    public string BackupPath => Status.BackupPath;

    /// <summary>What <see cref="Read"/> answers; a link or a replacement that succeeds makes it this copy's.</summary>
    internal CliLinkStatus Status { get; private set; } = status;

    /// <summary>What a link or a replacement answers instead of succeeding, changing nothing.</summary>
    internal CliLinkResult? Answer { get; init; }

    /// <summary>Held by a link or a replacement until the test releases it.</summary>
    internal SemaphoreSlim? Gate { get; init; }

    internal int Links { get; private set; }

    internal int Replaces { get; private set; }

    internal int KeepCurrents { get; private set; }

    internal static FakeCliLink At(
        CliLinkState state,
        string? linkedTo = null,
        bool backupExists = false,
        bool onPath = true,
        bool translocated = false,
        CliLinkResult? answer = null,
        SemaphoreSlim? gate = null,
        string location = Location,
        string? earlierCopy = null) =>
        new(new CliLinkStatus(state, location, linkedTo, location + ".keypaste-backup", backupExists, onPath, translocated, earlierCopy))
        {
            Answer = answer,
            Gate = gate,
        };

    public CliLinkStatus Read() => Status;

    public CliLinkResult Link()
    {
        Links++;
        return Change(CliLinkOutcome.Linked);
    }

    public CliLinkResult Replace()
    {
        Replaces++;
        return Change(CliLinkOutcome.Replaced);
    }

    public bool KeepCurrent()
    {
        KeepCurrents++;
        return false;
    }

    private CliLinkResult Change(CliLinkOutcome outcome)
    {
        Gate?.Wait(TestContext.Current.CancellationToken);

        if (Answer is { } answer)
        {
            return answer;
        }

        Status = Status with
        {
            State = CliLinkState.ThisCopy,
            LinkedTo = null,
            BackupExists = Status.BackupExists || outcome == CliLinkOutcome.Replaced,
        };

        return new CliLinkResult(outcome);
    }
}
