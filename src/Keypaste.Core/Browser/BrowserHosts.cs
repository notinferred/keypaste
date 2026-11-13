using Keypaste.Core.Clients;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Browser;

/// <summary>Who answers a browser's request for <see cref="HostPlaces.HostName"/>.</summary>
public enum HostState
{
    /// <summary>No manifest is where the browser looks.</summary>
    NotSetUp,

    /// <summary>keypaste's manifest, naming a program that exists.</summary>
    Keypaste,

    /// <summary>keypaste's manifest, naming a program that no longer exists.</summary>
    KeypasteMissing,

    /// <summary>A manifest keypaste did not write.</summary>
    Other,
}

/// <summary>One browser's host and extension.</summary>
/// <param name="Browser">The browser.</param>
/// <param name="State">Who answers it.</param>
/// <param name="Program">The program its manifest names, or the file another program wrote when it names none; null when not set up.</param>
/// <param name="Extension">Whether keypaste's extension is in its profiles.</param>
/// <param name="SharesWith">The other browsers that read the same manifest, which turn on and off with it: Brave and Chrome on Windows.</param>
public sealed record HostStatus(HostBrowser Browser, HostState State, string? Program, ExtensionState Extension, IReadOnlyList<HostBrowser> SharesWith);

/// <summary>What turning a browser's host on or off did.</summary>
public enum HostOutcome
{
    /// <summary>The manifest now names this keypaste.</summary>
    On,

    /// <summary>keypaste's manifest is gone.</summary>
    Off,

    /// <summary>Nothing needed changing.</summary>
    Unchanged,

    /// <summary>Another program's manifest is there, which turning off leaves.</summary>
    LeftOther,

    /// <summary>The change was attempted and did not happen.</summary>
    Failed,
}

/// <summary>What turning a browser's host on or off did.</summary>
/// <param name="Browser">The browser.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Replaced">The program another manifest named, which turning on replaced; otherwise null.</param>
/// <param name="Reason">What went wrong, when it failed.</param>
public sealed record HostChange(HostBrowser Browser, HostOutcome Outcome, string? Replaced = null, string? Reason = null);

/// <summary>
/// Turns keypaste on and off as each browser's host for its extension, at the user-level place the browser reads before a
/// system one: a manifest in its own directory, or on Windows a file under <c>%LOCALAPPDATA%\keypaste</c> that a key under
/// <c>HKCU</c> names.
/// </summary>
/// <remarks>Turning off and keeping current touch only a manifest keypaste wrote; another program's is left as it is.</remarks>
public sealed class BrowserHosts
{
    private const UnixFileMode _readable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const string _bundled = "/Contents/MacOS/keypaste";

    private readonly HostPlaces _places;
    private readonly IHostRegistry? _registry;
    private readonly HostProgram _named;
    private readonly string _program;

    /// <summary>Initializes the hosts for one user and one program.</summary>
    /// <param name="places">The user's folders.</param>
    /// <param name="registry">The registry on Windows; null elsewhere.</param>
    /// <param name="program">The <c>keypaste</c> a manifest names.</param>
    public BrowserHosts(HostPlaces places, IHostRegistry? registry, HostProgram program)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(program);

        if (places.Platform == McpClientPlatform.Windows && registry is null)
        {
            throw new ArgumentException("Windows reads manifests through the registry", nameof(registry));
        }

        _places = places;
        _registry = registry;
        _named = program;
        _program = program.Path;
    }

    /// <summary>Gets the program a manifest names.</summary>
    public string Program => _program;

    /// <summary>Reads each browser's host and extension.</summary>
    /// <returns>The five, in <see cref="HostPlaces.Browsers"/>' order.</returns>
    public IReadOnlyList<HostStatus> Read() => [.. HostPlaces.Browsers.Select(Read)];

    /// <summary>Reads one browser's host and extension.</summary>
    /// <param name="browser">The browser.</param>
    /// <returns>Its status.</returns>
    public HostStatus Read(HostBrowser browser)
    {
        var (state, program) = Inspect(browser);
        return new HostStatus(browser, state, program, ExtensionPresence.Of(_places, browser), [.. _places.SharingSlot(browser).Where(other => other != browser)]);
    }

    /// <summary>Writes keypaste's manifest for <paramref name="browser"/>, replacing whatever is there.</summary>
    /// <param name="browser">The browser.</param>
    /// <returns>What happened, naming the program a replaced manifest named.</returns>
    public HostChange TurnOn(HostBrowser browser)
    {
        var (state, program) = Inspect(browser);

        if (state == HostState.Keypaste && string.Equals(program, _program, StringComparison.Ordinal))
        {
            return new HostChange(browser, HostOutcome.Unchanged);
        }

        var written = Write(browser);
        return written.Outcome == HostOutcome.On && state == HostState.Other ? written with { Replaced = program } : written;
    }

    /// <summary>Removes keypaste's manifest for <paramref name="browser"/>, and only keypaste's.</summary>
    /// <param name="browser">The browser.</param>
    /// <returns>What happened.</returns>
    public HostChange TurnOff(HostBrowser browser)
    {
        var (state, program) = Inspect(browser);

        return state switch
        {
            HostState.NotSetUp => new HostChange(browser, HostOutcome.Unchanged),
            HostState.Other => new HostChange(browser, HostOutcome.LeftOther, Replaced: program),
            _ => Remove(browser),
        };
    }

    /// <summary>Re-points each manifest of keypaste's that names a program that is gone or another copy of the app, as each start of the app does.</summary>
    /// <returns>The browsers whose manifest was rewritten.</returns>
    public IReadOnlyList<HostBrowser> KeepCurrent()
    {
        var rewritten = new List<HostBrowser>();

        foreach (var browser in HostPlaces.Browsers.Where(browser => _places.Slot(browser) == browser))
        {
            var (state, program) = Inspect(browser);
            var stale = state == HostState.KeypasteMissing
                || (state == HostState.Keypaste && program != _program && program!.EndsWith(_bundled, StringComparison.Ordinal));

            if (stale && Write(browser).Outcome == HostOutcome.On)
            {
                rewritten.Add(browser);
            }
        }

        return rewritten;
    }

    private (HostState State, string? Program) Inspect(HostBrowser browser)
    {
        var slot = _places.Slot(browser);
        var ours = _places.ManifestFile(slot);
        var file = ours;

        if (_registry is not null)
        {
            file = _registry.Read(_places.RegistryKey(slot));

            if (file is null)
            {
                return (HostState.NotSetUp, null);
            }

            if (ours is null || !IsAt(file, ours))
            {
                return (HostState.Other, ReadFile(slot, file)?.Program ?? file);
            }
        }

        if (file is null || !File.Exists(file))
        {
            return (_registry is null ? HostState.NotSetUp : HostState.Other, _registry is null ? null : file);
        }

        return ReadFile(slot, file) switch
        {
            { IsKeypaste: true, Program: var program } => (File.Exists(program) ? HostState.Keypaste : HostState.KeypasteMissing, program),
            var other => (HostState.Other, other?.Program ?? file),
        };
    }

    // Windows paths compare without case; a value that is no path at all names some other file.
    private static bool IsAt(string named, string ours)
    {
        try
        {
            return string.Equals(Path.GetFullPath(named), ours, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static ReadManifest? ReadFile(HostBrowser slot, string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists && info.Length <= HostManifest.MaximumLength ? HostManifest.Read(slot, File.ReadAllBytes(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private HostChange Write(HostBrowser browser)
    {
        var slot = _places.Slot(browser);

        if (_places.ManifestFile(slot) is not { } file)
        {
            return new HostChange(browser, HostOutcome.Failed, Reason: "the folder the browser reads its manifests from is unknown");
        }

        if (_named.Prepare?.Invoke() is { } unprepared)
        {
            return new HostChange(browser, HostOutcome.Failed, Reason: unprepared);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            AtomicFile.Write(file, HostManifest.Document(slot, _program), _readable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HostChange(browser, HostOutcome.Failed, Reason: ex.Message);
        }

        return _registry is null || _registry.Write(_places.RegistryKey(slot), file)
            ? new HostChange(browser, HostOutcome.On)
            : new HostChange(browser, HostOutcome.Failed, Reason: $"HKCU\\{_places.RegistryKey(slot)} could not be written");
    }

    private HostChange Remove(HostBrowser browser)
    {
        var slot = _places.Slot(browser);

        if (_registry is not null && !_registry.Delete(_places.RegistryKey(slot)))
        {
            return new HostChange(browser, HostOutcome.Failed, Reason: $"HKCU\\{_places.RegistryKey(slot)} could not be removed");
        }

        try
        {
            File.Delete(_places.ManifestFile(slot)!);
            return new HostChange(browser, HostOutcome.Off);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HostChange(browser, HostOutcome.Failed, Reason: ex.Message);
        }
    }
}
