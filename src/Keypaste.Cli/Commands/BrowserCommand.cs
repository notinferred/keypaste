using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core.Browser;
using Keypaste.Core.Clients;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste browser [on|off]</c>: lists who each browser starts for keypaste's extension and whether the extension is in
/// that browser, and turns this <c>keypaste</c> on or off as that host.
/// </summary>
internal static class BrowserCommand
{
    private const string _verb = "keypaste browser";

    private static readonly Argument<string[]> _browsers = Syntax.Operands("browser", $"{Names}, one or more", minimum: 1);

    internal static CliVerb Command { get; } = new(
        "browser",
        "turn keypaste on or off as each browser's extension host\n\n"
            + "lists Chrome, Chromium, Edge, Brave and Firefox: which program each starts for keypaste's extension, and "
            + "whether the extension is installed in it. on writes a manifest naming this keypaste where the browser "
            + "reads it, replacing another program's; off removes keypaste's own and leaves another program's. on Windows "
            + "Brave reads Chrome's, so the two turn on and off together.",
        List)
    {
        new CliVerb("on", "start this keypaste for keypaste's extension in these browsers", (line, context) => Change(line, context, "on")) { _browsers },
        new CliVerb("off", "remove keypaste's manifest from these browsers", (line, context) => Change(line, context, "off")) { _browsers },
        CliOptions.Json,
    };

    private static string Names => string.Join(", ", HostPlaces.Browsers.Select(Key));

    private static int List(ParseResult line, CliContext context)
    {
        if (!TryHosts(context, _verb, out var hosts))
        {
            return CliApp.ExitInternalError;
        }

        var statuses = hosts.Read();

        if (line.GetValue(CliOptions.Json))
        {
            CliJson.WriteArray(context.Stdout, statuses, WriteJson);
            return CliApp.ExitSuccess;
        }

        ClientsCommand.Table(
            context.Stdout,
            context.ConsoleStyle,
            ["BROWSER", "EXTENSION", "ANSWERED BY"],
            [.. statuses.Select(status => new[] { status.Browser.ToString(), Extension(status.Extension), Answer(status) })]);

        var places = Places(context);

        foreach (var status in statuses.Where(status => status.SharesWith.Count > 0 && places.Slot(status.Browser) == status.Browser))
        {
            context.Stderr.WriteLine($"{_verb}: {Reads(status.SharesWith)} {status.Browser}'s manifest, so they turn on and off together");
        }

        return CliApp.ExitSuccess;
    }

    private static int Change(ParseResult line, CliContext context, string word)
    {
        var verb = $"{_verb} {word}";
        var named = line.GetValue(_browsers)!.Distinct(StringComparer.Ordinal).ToList();

        if (named.FirstOrDefault(name => Parse(name) is null) is { } unknown)
        {
            context.Stderr.WriteLine($"{verb}: unknown browser '{DoctorCommand.Shown(unknown)}'; name {Names}");
            context.Stderr.WriteLine($"see {verb} --help");
            return CliApp.ExitUsageError;
        }

        if (!TryHosts(context, verb, out var hosts))
        {
            return CliApp.ExitInternalError;
        }

        var exit = CliApp.ExitSuccess;

        foreach (var browser in named.Select(name => Parse(name)!.Value))
        {
            var change = word == "on" ? hosts.TurnOn(browser) : hosts.TurnOff(browser);

            if (change.Outcome == HostOutcome.Failed)
            {
                context.Stderr.WriteLine($"{verb}: {browser}: {DoctorCommand.Shown(change.Reason ?? "it could not be changed")}");
                exit = CliApp.ExitInternalError;
                continue;
            }

            Done(context, browser, Said(change, hosts, Places(context), word));
        }

        return exit;
    }

    private static string Said(HostChange change, BrowserHosts hosts, HostPlaces places, string word)
    {
        var browser = change.Browser;
        var together = places.SharingSlot(browser).Where(other => other != browser).ToList();
        var with = together.Count == 0 ? string.Empty : $"; {Reads(together)} the same manifest, so {(together.Count == 1 ? "it is" : "they are")} {word} too";

        return change switch
        {
            { Outcome: HostOutcome.On, Replaced: { } replaced } =>
                $"now starts {DoctorCommand.Shown(hosts.Program)} for keypaste's extension; it replaced {DoctorCommand.Shown(replaced)}, which {browser} started before{with}",
            { Outcome: HostOutcome.On } => $"now starts {DoctorCommand.Shown(hosts.Program)} for keypaste's extension{with}",
            { Outcome: HostOutcome.Off } => $"off; keypaste's manifest is removed{with}",
            { Outcome: HostOutcome.LeftOther } => $"another program's manifest is there ({DoctorCommand.Shown(change.Replaced ?? "")}); keypaste leaves it",
            _ => $"already {word}",
        };
    }

    private static bool TryHosts(CliContext context, string verb, [NotNullWhen(true)] out BrowserHosts? hosts)
    {
        if (!HostProgram.TryForTheCli(context.ProcessPath, context.AppImage(), out var program, out var error))
        {
            context.Stderr.WriteLine($"{verb}: {error}");
            hosts = null;
            return false;
        }

        hosts = new BrowserHosts(Places(context), OperatingSystem.IsWindows() ? new RegistryHostKeys() : null, program);
        return true;
    }

    private static void Done(CliContext context, HostBrowser browser, string what)
    {
        var style = context.ConsoleStyle;
        var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        context.Stderr.WriteLine($"  {done} {browser} {style.Glyph(context.Stderr, Mark.Dot)} {what}");
    }

    private static void WriteJson(Utf8JsonWriter json, HostStatus status)
    {
        json.WriteString("browser", Key(status.Browser));
        json.WriteString("state", status.State switch
        {
            HostState.Keypaste => "keypaste",
            HostState.KeypasteMissing => "keypaste-missing",
            HostState.Other => "other",
            _ => "not-set-up",
        });

        if (status.Program is { } program)
        {
            json.WriteString("program", program);
        }
        else
        {
            json.WriteNull("program");
        }

        json.WriteString("extension", status.Extension switch
        {
            ExtensionState.Installed => "installed",
            ExtensionState.NotFound => "not-found",
            _ => "browser-not-found",
        });

        json.WriteStartArray("shares_with");

        foreach (var other in status.SharesWith)
        {
            json.WriteStringValue(Key(other));
        }

        json.WriteEndArray();
    }

    private static string Answer(HostStatus status) => status.State switch
    {
        HostState.Keypaste => $"keypaste at {DoctorCommand.Shown(status.Program!)}",
        HostState.KeypasteMissing => $"keypaste at {DoctorCommand.Shown(status.Program!)}, which no longer exists",
        HostState.Other => $"another program: {DoctorCommand.Shown(status.Program!)}",
        _ => "not set up",
    };

    private static string Extension(ExtensionState state) => state switch
    {
        ExtensionState.Installed => "installed",
        ExtensionState.NotFound => "not found",
        _ => "browser not found",
    };

    private static HostPlaces Places(CliContext context) =>
        HostPlaces.From(context.Environment.Get, McpClientMachine.CurrentPlatform, context.HostRegistryRoot);

    private static string Reads(IReadOnlyList<HostBrowser> browsers) => $"{string.Join(" and ", browsers)} {(browsers.Count == 1 ? "reads" : "read")}";

    private static HostBrowser? Parse(string name) => HostPlaces.Browsers.Where(browser => Key(browser) == name).Cast<HostBrowser?>().FirstOrDefault();

    private static string Key(HostBrowser browser) => browser.ToString().ToLowerInvariant();
}
