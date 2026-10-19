using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.Views;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Mvvm;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// What the app shows and a screen reader says holds item, environment, allowance, what an agent
/// can see, KeePass database and AI tool, never the words those replaced (D-0457).
/// </summary>
/// <remarks>
/// Commands, flags, file names, environment variables, <c>kp://</c> references and backticked names
/// keep their spelling, since a person types them, so they are taken out before looking. The vault's
/// own names hold none of the words, so whatever is found is keypaste's.
/// </remarks>
public sealed partial class PlainWordsTests
{
    [GeneratedRegex(@"\b(grants?|granted|entry|entries|exposure|exposed|expose|sessions?|kdbx|stdio|profiles?|clients?)", RegexOptions.IgnoreCase)]
    private static partial Regex Retired();

    [GeneratedRegex(@"`[^`]*`|--[a-z][a-z-]*|\bkeypaste [a-z]+(?: (?:revoke|verify|policy|serve|setup))?|\b[a-z]+(?:_[a-z]+)+\b|\b[\w-]+\.(?:toml|jsonl?|kdbx)\b|\bKEYPASTE_[A-Z_]+\b|kp://\S*")]
    private static partial Regex Interface();

    [Fact]
    public Task EveryScreen_HoldsNoneOfTheRetiredWords() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-plain-");
        var said = new List<(string Where, string Text)>();

        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.Selected = items.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();
        shell.Named<ToggleButton>("EntryMenu").IsChecked = true;
        shell.Named<ToggleButton>("AddEntry").IsChecked = true;
        said.AddRange(Shown(shell.Window, "Items"));
        shell.Named<ToggleButton>("EntryMenu").IsChecked = false;
        shell.Named<ToggleButton>("AddEntry").IsChecked = false;

        var detail = items.Detail!;
        var field = detail.Fields[0];

        foreach (var (state, open, close) in new (string, IRelayCommand, IRelayCommand)[]
        {
            ("an item's edit form", detail.EditCommand, detail.CancelCommand),
            ("an item's rotate confirmation", detail.RotateCommand, detail.CancelRotateCommand),
            ("an item's add-field form", detail.BeginAddFieldCommand, detail.CancelAddFieldCommand),
            ("an item's replace-field form", field.ReplaceCommand, detail.CancelReplaceFieldCommand),
            ("an item's remove-field confirmation", field.RemoveCommand, detail.CancelRemoveFieldCommand),
        })
        {
            Assert.True(open.CanExecute(null), state);
            open.Execute(null);
            RenderedShell.Drain();
            said.AddRange(Shown(shell.Window, state));
            close.Execute(null);
            RenderedShell.Drain();
        }

        detail.History.ToggleCommand.Execute(null);
        RenderedShell.Drain();
        detail.History.Selected = detail.History.Rows[0];
        RenderedShell.Drain();
        said.AddRange(Shown(shell.Window, "an item's history"));

        foreach (var kind in new[] { DestinationKind.AgentHistory, DestinationKind.Trash, DestinationKind.Settings, DestinationKind.Log, DestinationKind.Sharing, DestinationKind.Tokens, DestinationKind.Diagnostics })
        {
            shell.Shell.Current = Destinations.Of(kind);
            RenderedShell.Drain();
            said.AddRange(Shown(shell.Window, kind.ToString()));
        }

        shell.Shell.OpenProjectCommand.Execute("billing");
        RenderedShell.Drain();
        said.AddRange(Shown(shell.Window, "a project"));

        var project = Assert.IsType<EnvSetsViewModel>(shell.Shell.Content).OpenProject!;
        project.Environments[0].AddEntry.Execute(null);
        RenderedShell.Drain();
        said.AddRange(Shown(shell.Window, "a project's add-item form"));

        foreach (var (name, window) in Prompts())
        {
            (window.DataContext as PromptViewModel)?.Arm();
            window.Show();
            RenderedShell.Drain();
            said.AddRange(Shown(window, name));
            window.Close();
        }

        Assert.Empty(Offending(said));
    });

    /// <summary>Agents in every state it can be in, then with every Settings open and each kind of Connect pane, before and after Run it.</summary>
    [Fact]
    public Task Agents_HoldsNoneOfTheRetiredWords() => HeadlessSession.On(async () =>
    {
        var said = new List<(string Where, string Text)>();
        var state = 0;
        await AgentsFirstLevelTests.EveryStateAsync(window => said.AddRange(Shown(window, $"Agents, state {++state}")));

        await using var agents = await AgentsFirstLevelTests.Agents.StartAsync();
        await agents.RequestAsync("password");
        var model = agents.Model;

        foreach (var disclosure in agents.Window.GetVisualDescendants().OfType<Expander>().ToList())
        {
            disclosure.IsExpanded = true;
        }

        agents.Settle();

        foreach (var settings in model.Rows.Select(row => row.Settings).OfType<AgentSettingsViewModel>())
        {
            settings.ToggleAdvancedCommand.Execute(null);
        }

        agents.Settle();
        said.AddRange(Shown(agents.Window, "Agents, every Settings open"));

        foreach (var (open, key) in new (IRelayCommand Open, string Key)[] { (model.ConnectCommand, "cursor"), (model.DisconnectCommand, "gemini-cli"), (model.ConnectAgainCommand, "claude-code") })
        {
            var target = model.Rows.Single(row => row.Key == key);
            Assert.True(open.CanExecute(target), key);
            open.Execute(target);
            await model.Opening.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            agents.Settle();
            said.AddRange(Shown(agents.Window, $"Agents, {key}'s pane"));
        }

        await model.Connection!.ConfirmCommand.ExecuteAsync();
        await model.Looking.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        agents.Settle();
        said.AddRange(Shown(agents.Window, "Agents, after Run it"));

        Assert.Empty(Offending(said));
    });

    private static IEnumerable<(string Name, Window Window)> Prompts()
    {
        Assert.True(ProjectScope.TryCreate("billing", "dev", out var dev, out var error), error);
        var preview = new EnvPreview("billing", ["STRIPE_KEY"]);
        var env = EnvReleasePrompt.For(preview, ["npm", "test"], "/home/maya/billing");

        yield return ("the env prompt, timed", new EnvApprovalWindow(new EnvApprovalViewModel(env with { GrantSeconds = 900 })));
        yield return ("the env prompt, once only", new EnvApprovalWindow(new EnvApprovalViewModel(env)));

        var run = new RunPrompt
        {
            Client = "claude-code",
            Label = "work laptop",
            Reason = "Run the tests against the dev account.",
            ReasonWasTruncated = false,
            ReasonWasAltered = false,
            Program = "/usr/local/bin/npm",
            Command = "npm test",
            Directory = "/home/maya/billing",
            Project = "billing",
            Profile = "dev",
            Variables = [new("STRIPE_KEY", "env/billing/.env", "STRIPE_KEY")],
            GrantSeconds = 900,
        };

        yield return ("the run prompt, timed", new RunApprovalWindow(new RunApprovalViewModel(run)));
        yield return ("the run prompt, once only", new RunApprovalWindow(new RunApprovalViewModel(run with { GrantSeconds = 0 })));

        var timed = ApprovalPrompt.For("claude-code", new EntryName("env/billing", ".env"), "STRIPE_KEY", "Run the tests against the dev account.", 3600, "work laptop");

        yield return ("the credential prompt, timed", new ApprovalWindow(new ApprovalViewModel(timed with { AlwaysFor = dev })));
        yield return ("the credential prompt, once only", new ApprovalWindow(new ApprovalViewModel(
            ApprovalPrompt.For("claude-code", new EntryName("env/billing", ".env.prod"), "STRIPE_KEY", "Check the live account.", 0, null))));

        var change = AccessChangePrompt.For(
            "work laptop",
            AccessChange.Classify(EffectiveAccess.Defaults, AgentAccess.Defaults with { Projects = [new ProjectAccess(dev, AgentMode.Automatic)] }),
            AccessOrigin.Prompt,
            string.Empty,
            TimeZoneInfo.Utc);

        yield return ("the access-change confirmation", new AccessChangeWindow(new AccessChangeViewModel(change)));
        yield return ("a terminal's edit", new TerminalEditWindow(new TerminalEditViewModel(new TerminalEditPrompt
        {
            Command = "keypaste env set",
            Item = "env/billing/.env",
            Changes = ["Adds STRIPE_KEY to billing/dev on env/billing/.env with the value the terminal sent."],
        })));
        yield return ("the Always for notice", new NoticeWindow(new AlwaysForNotice("work laptop", "billing:dev", "another request was already in front of a person"), _ => { }));
    }

    private static IEnumerable<(string Where, string Text)> Shown(Control root, string where)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Prepend(root).Where(control => control.IsEffectivelyVisible))
        {
            if (control is TextBlock block)
            {
                yield return (where, Prose(block));

                foreach (var run in block.Inlines?.OfType<Run>() ?? [])
                {
                    yield return (where, run.Text ?? string.Empty);
                }
            }

            if (control is ContentControl { Content: string content })
            {
                yield return (where, content);
            }

            if (control is TextBox box)
            {
                yield return (where, box.PlaceholderText ?? string.Empty);
            }

            if (ToolTip.GetTip(control) is string tip)
            {
                yield return (where, tip);
            }

            if (AutomationProperties.GetName(control) is { Length: > 0 } name)
            {
                yield return (where, name);
            }
        }
    }

    /// <summary>A plan is the commands a tool runs, and history's rows keep the words the log records, so only its column heads are prose.</summary>
    private static string Prose(TextBlock block) => block.Name switch
    {
        "ConnectPlan" => string.Empty,
        "SessionHistory" => (block.Text ?? string.Empty).Split(Environment.NewLine)[0],
        _ => block.Text ?? string.Empty,
    };

    private static List<string> Offending(IEnumerable<(string Where, string Text)> said) =>
        [.. said.SelectMany(item => Retired()
            .Matches(Interface().Replace(item.Text, " "))
            .Select(match => $"{item.Where}: '{match.Value}' in {item.Text}"))
            .Distinct(StringComparer.Ordinal)];
}
