using Keypaste.Mvvm;
using Xunit;

namespace Keypaste.App.Tests.Mvvm;

/// <summary>
/// The commands the views bind to: refused when they cannot run, and the async one never twice at once.
/// </summary>
public sealed class RelayCommandTests
{
    /// <summary>
    /// A second press while the first run is still going starts nothing.
    /// </summary>
    /// <remarks>
    /// Unlocking runs Argon2, and a double-click on the unlock button would otherwise start two
    /// derivations against one buffer.
    /// </remarks>
    [Fact]
    public async Task An_async_command_runs_once_at_a_time()
    {
        var release = new TaskCompletionSource();
        var runs = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            runs++;
            await release.Task;
        });

        var first = command.ExecuteAsync();

        Assert.False(command.CanExecute(null));

        command.Execute(null);
        await command.ExecuteAsync();

        Assert.Equal(1, runs);

        release.SetResult();
        await first;

        Assert.True(command.CanExecute(null));

        await command.ExecuteAsync();

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task An_async_command_announces_its_start_and_its_end()
    {
        var release = new TaskCompletionSource();
        var command = new AsyncRelayCommand(() => release.Task);
        var seen = new List<bool>();
        command.CanExecuteChanged += (_, _) => seen.Add(command.CanExecute(null));

        var run = command.ExecuteAsync();
        release.SetResult();
        await run;

        Assert.Equal([false, true], seen);
    }

    [Fact]
    public async Task An_async_command_that_throws_can_run_again()
    {
        var command = new AsyncRelayCommand(() => throw new InvalidOperationException("refused"));

        await Assert.ThrowsAsync<InvalidOperationException>(command.ExecuteAsync);

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task An_async_command_its_owner_refuses_does_not_run()
    {
        var runs = 0;
        var command = new AsyncRelayCommand(
            () =>
            {
                runs++;
                return Task.CompletedTask;
            },
            () => false);

        await command.ExecuteAsync();

        Assert.Equal(0, runs);
    }

    [Fact]
    public void A_command_its_owner_refuses_does_not_run()
    {
        var runs = 0;
        var allowed = false;
        var command = new RelayCommand(() => runs++, () => allowed);

        command.Execute(null);
        Assert.Equal(0, runs);

        allowed = true;
        command.Execute(null);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void A_parameterised_command_is_told_its_item_and_refuses_what_is_not_one()
    {
        var pressed = new List<string?>();
        var command = new RelayCommand<string>(pressed.Add, item => item is not null);

        command.Execute("row");
        command.Execute(42);

        Assert.Equal("row", Assert.Single(pressed));
        Assert.False(command.CanExecute(42));
    }
}
