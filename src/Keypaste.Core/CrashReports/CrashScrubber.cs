using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Keypaste.Core.CrashReports;

/// <summary>
/// The only code that reads an exception for a crash report and the only code that writes a report's text (D-0558).
/// </summary>
/// <remarks>
/// A report keeps an exception's type, HResult and frames, each frame as its assembly, type and method names and its IL
/// offset, read through <see cref="DiagnosticMethodInfo"/>; never its message, data, source or the runtime's stack-trace
/// text. Every name kept must be a code identifier from an assembly on the allowlist, so no path, address or sentence fits.
/// A report read back from the outbox passes the same rules and must be byte for byte what this would write.
/// </remarks>
public static class CrashScrubber
{
    /// <summary>The largest report, in bytes; frames are dropped from the bottom until it fits.</summary>
    public const int MaximumBytes = 16 * 1024;

    /// <summary>The frames kept of the exception that failed.</summary>
    internal const int MaximumFrames = 48;

    /// <summary>The inner exceptions kept, breadth first.</summary>
    internal const int MaximumInner = 4;

    /// <summary>The frames kept of each inner exception.</summary>
    internal const int MaximumInnerFrames = 16;

    /// <summary>The longest name kept.</summary>
    internal const int MaximumNameLength = 256;

    /// <summary>What a name that is not a code identifier becomes.</summary>
    internal const string Unnamed = "[unnamed]";

    /// <summary>What a frame in an assembly off the allowlist becomes.</summary>
    internal const string External = "[external]";

    /// <summary>What a frame in a method no type declares becomes, such as a compiled expression's.</summary>
    internal const string Dynamic = "[dynamic]";

    private const int _schema = 1;
    private const string _crash = "crash";
    private const string _unobservedTask = "unobserved-task";
    private const string _unexpected = "unexpected";
    private const string _endedUnexpectedly = "ended-unexpectedly";
    private const string _dateFormat = "yyyy-MM-dd";

    private static readonly string[] _kinds = [_crash, _unobservedTask, _unexpected, _endedUnexpectedly];
    private static readonly string[] _systems = ["windows", "macos", "linux", "other"];
    private static readonly string[] _architectures = ["x64", "arm64", "x86", "arm", "other"];
    private static readonly string[] _uptimes = ["<1m", "<10m", "<1h", "<1d", ">1d"];

    // Each is a whole name, or the start of one that continues with a dot.
    private static readonly string[] _assemblies =
        ["keypaste-app", "Keypaste", "KeePassLib", "Avalonia", "System", "Microsoft", "Yubico", "SkiaSharp", "HarfBuzzSharp", "Tmds.DBus", "WinRT.Runtime"];

    // Every string written is ASCII from the grammars below, so the default encoder's HTML escaping would only obscure it.
    private static readonly JsonWriterOptions _writing = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string _version = IsVersion(CoreInfo.Version) ? CoreInfo.Version : "0.0.0-unknown";
    private static readonly string? _commit = Commit();
    private static readonly string _system = OperatingSystemName();
    private static readonly string _systemVersion = Numbers(Environment.OSVersion.Version);
    private static readonly string _architecture = ArchitectureName();
    private static readonly string _runtime = Numbers(Environment.Version);

    /// <summary>A report of an exception nothing caught.</summary>
    /// <param name="exception">The exception, of which only its type, HResult, frames and inner exceptions are read.</param>
    /// <param name="context">When, and what the app knew.</param>
    public static CrashReport Crash(Exception exception, CrashContext context) => FromException(_crash, null, exception, context);

    /// <summary>A report of a failed task nobody awaited.</summary>
    /// <param name="exception">The task's exception, of which only its type, HResult, frames and inner exceptions are read.</param>
    /// <param name="context">When, and what the app knew.</param>
    public static CrashReport UnobservedTask(Exception exception, CrashContext context) =>
        FromException(_unobservedTask, null, exception, context);

    /// <summary>A report of an exception a catch-everything block in keypaste's code caught.</summary>
    /// <param name="exception">The exception, of which only its type, HResult, frames and inner exceptions are read.</param>
    /// <param name="site">A constant in keypaste's code naming the block, such as <c>LockedNotice.Show</c>.</param>
    /// <param name="context">When, and what the app knew.</param>
    public static CrashReport Unexpected(Exception exception, string site, CrashContext context) =>
        FromException(_unexpected, Name(site), exception, context);

    /// <summary>A report that the app ended while no hook could run, found at a later start; it holds no exception.</summary>
    /// <param name="context">When, and what the app knew.</param>
    public static CrashReport EndedUnexpectedly(CrashContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Report(Body(_endedUnexpectedly, null, null, context), 1);
    }

    /// <summary>Reads a report the outbox kept, which must be exactly what this class writes.</summary>
    /// <param name="json">The file's bytes.</param>
    /// <param name="report">The report, when this returns true.</param>
    internal static bool TryRead(byte[] json, [NotNullWhen(true)] out CrashReport? report)
    {
        ArgumentNullException.ThrowIfNull(json);

        report = null;

        if (json.Length > MaximumBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (ReadBody(document.RootElement, out var count) is not { } body
                || !json.AsSpan().SequenceEqual(Write(body, count))
                || Write(body, int.MaxValue).Length > MaximumBytes)
            {
                return false;
            }

            report = Report(body, count);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>One report standing for both identical reports' failures.</summary>
    internal static CrashReport Merge(CrashReport kept, CrashReport added)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(added);

        if (kept.Identity != added.Identity)
        {
            throw new ArgumentException("only identical reports merge", nameof(added));
        }

        using var document = JsonDocument.Parse(kept.Json);
        var body = ReadBody(document.RootElement, out _) ?? throw new InvalidOperationException("a report this class wrote no longer reads");

        return Report(body, (int)Math.Min((long)kept.Count + added.Count, int.MaxValue));
    }

    /// <summary>A frame's three names as a report keeps them.</summary>
    /// <param name="assembly">The declaring assembly's display name.</param>
    /// <param name="type">The declaring type's full name, or null for a method no type declares.</param>
    /// <param name="method">The method's name, or null when the runtime could not name it.</param>
    internal static (string Assembly, string Type, string Method) FrameNames(string? assembly, string? type, string? method)
    {
        if (method is null)
        {
            return (Unnamed, Unnamed, Unnamed);
        }

        if (type is null)
        {
            return (Dynamic, Dynamic, Dynamic);
        }

        var simple = SimpleName(assembly);

        return IsAllowedAssembly(simple) ? (simple, Name(WithoutTypeArguments(type)), Name(method)) : (External, External, External);
    }

    /// <summary>The name, when it is a code identifier, else <see cref="Unnamed"/>.</summary>
    internal static string Name(string? text) => text is not null && IsName(text) ? text : Unnamed;

    /// <summary>Whether this is a code identifier: ASCII letters, digits and <c>_ . + &lt; &gt; ` $ | -</c>, at most 256 of them.</summary>
    internal static bool IsName(string text) => text.Length is > 0 and <= MaximumNameLength && text.All(IsNameCharacter);

    /// <summary>Whether frames from this assembly are named.</summary>
    internal static bool IsAllowedAssembly(string name) =>
        IsName(name) && _assemblies.Any(allowed => name == allowed || name.StartsWith(allowed + ".", StringComparison.Ordinal));

    /// <summary>How long the app had run, as a report keeps it.</summary>
    internal static string Bucket(TimeSpan uptime) =>
        uptime < TimeSpan.FromMinutes(1) ? "<1m"
        : uptime < TimeSpan.FromMinutes(10) ? "<10m"
        : uptime < TimeSpan.FromHours(1) ? "<1h"
        : uptime < TimeSpan.FromDays(1) ? "<1d"
        : ">1d";

    /// <summary>The screen's name, when it is lowercase letters, digits and hyphens, at most 32 and starting with a letter.</summary>
    internal static string Screen(string screen) =>
        screen.Length is > 0 and <= 32 && char.IsAsciiLetterLower(screen[0]) && screen.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
            ? screen
            : Unnamed;

    private static CrashReport FromException(string kind, string? site, Exception exception, CrashContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        var body = Body(kind, site, Describe(exception, MaximumFrames, Inner(exception)), context);

        while (Write(body, int.MaxValue).Length > MaximumBytes)
        {
            if (!DropLastFrame(body.Exception))
            {
                break;
            }
        }

        return Report(body, 1);
    }

    private static ReportBody Body(string kind, string? site, ErrorBody? exception, CrashContext context) => new(
        kind,
        site,
        _version,
        _commit,
        _system,
        _systemVersion,
        _architecture,
        _runtime,
        context.Uptime is { } uptime ? Bucket(uptime) : null,
        context.Screen is { } screen ? Screen(screen) : null,
        DateOnly.FromDateTime(context.At.UtcDateTime),
        exception);

    private static CrashReport Report(ReportBody body, int count) => new CrashReport(
        Write(body, count),
        count,
        Convert.ToHexStringLower(SHA256.HashData(Write(body, 0))),
        body.OccurredOn);

    private static ErrorBody Describe(Exception exception, int frameLimit, List<ErrorBody>? inner)
    {
        List<FrameBody> frames = [];
        var omitted = 0;

        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames() ?? [])
        {
            if (frames.Count < frameLimit)
            {
                var method = DiagnosticMethodInfo.Create(frame);
                var (assembly, type, name) = FrameNames(method?.DeclaringAssemblyName, method?.DeclaringTypeName, method?.Name);
                frames.Add(new FrameBody(assembly, type, name, frame.GetILOffset()));
            }
            else
            {
                omitted++;
            }
        }

        return new ErrorBody(TypeName(exception.GetType()), exception.HResult, omitted, frames, inner);
    }

    private static List<ErrorBody> Inner(Exception exception)
    {
        List<ErrorBody> found = [];
        var waiting = new Queue<Exception>(Children(exception));

        while (found.Count < MaximumInner && waiting.TryDequeue(out var next))
        {
            found.Add(Describe(next, MaximumInnerFrames, null));

            foreach (var child in Children(next))
            {
                waiting.Enqueue(child);
            }
        }

        return found;
    }

    private static Exception[] Children(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return [.. aggregate.InnerExceptions];
        }

        if (exception.InnerException is { } inner)
        {
            return [inner];
        }

        return [];
    }

    // The nearest type, from the thrown one down its bases, that an allowed assembly declares under a code identifier.
    private static string TypeName(Type thrown)
    {
        for (var type = thrown; type is not null; type = type.BaseType)
        {
            if (IsAllowedAssembly(SimpleName(type.Assembly.FullName)) && type.FullName is { } full && IsName(WithoutTypeArguments(full)))
            {
                return WithoutTypeArguments(full);
            }
        }

        return "System.Exception";
    }

    private static bool DropLastFrame(ErrorBody? exception)
    {
        if (exception is null)
        {
            return false;
        }

        var holder = exception.Inner?.LastOrDefault(inner => inner.Frames.Count > 0) ?? (exception.Frames.Count > 0 ? exception : null);

        if (holder is null)
        {
            return false;
        }

        holder.Frames.RemoveAt(holder.Frames.Count - 1);
        holder.Omitted++;
        return true;
    }

    private static byte[] Write(ReportBody body, int count)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer, _writing))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", _schema);
            json.WriteString("kind", body.Kind);
            WriteText(json, "site", body.Site);
            json.WriteString("version", body.Version);
            WriteText(json, "build", body.Build);
            json.WriteString("os", body.Os);
            json.WriteString("os_version", body.OsVersion);
            json.WriteString("arch", body.Arch);
            json.WriteString("runtime", body.Runtime);
            WriteText(json, "uptime", body.Uptime);
            WriteText(json, "screen", body.Screen);
            json.WriteString("occurred_on", body.OccurredOn.ToString(_dateFormat, CultureInfo.InvariantCulture));
            json.WriteNumber("count", count);
            json.WritePropertyName("exception");
            WriteError(json, body.Exception);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteError(Utf8JsonWriter json, ErrorBody? exception)
    {
        if (exception is null)
        {
            json.WriteNullValue();
            return;
        }

        json.WriteStartObject();
        json.WriteString("type", exception.Type);
        json.WriteNumber("hresult", exception.HResult);
        json.WriteNumber("omitted", exception.Omitted);
        json.WriteStartArray("frames");

        foreach (var frame in exception.Frames)
        {
            json.WriteStartObject();
            json.WriteString("asm", frame.Assembly);
            json.WriteString("type", frame.Type);
            json.WriteString("method", frame.Method);
            json.WriteNumber("il", frame.Offset);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        if (exception.Inner is { } inner)
        {
            json.WriteStartArray("inner");

            foreach (var each in inner)
            {
                WriteError(json, each);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteText(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }

    private static ReportBody? ReadBody(JsonElement root, out int count)
    {
        count = 0;

        var kind = Text(root, "kind");
        var site = Text(root, "site");
        var version = Text(root, "version");
        var build = Text(root, "build");
        var system = Text(root, "os");
        var systemVersion = Text(root, "os_version");
        var architecture = Text(root, "arch");
        var runtime = Text(root, "runtime");
        var uptime = Text(root, "uptime");
        var screen = Text(root, "screen");

        if (Number(root, "schema") != _schema
            || kind is null || !_kinds.Contains(kind)
            || (kind == _unexpected ? site is null || Name(site) != site : site is not null)
            || version is null || !IsVersion(version)
            || (build is not null && !IsCommit(build))
            || system is null || !_systems.Contains(system)
            || systemVersion is null || !IsNumbers(systemVersion)
            || architecture is null || !_architectures.Contains(architecture)
            || runtime is null || !IsNumbers(runtime)
            || (uptime is not null && !_uptimes.Contains(uptime))
            || (screen is not null && Screen(screen) != screen)
            || !DateOnly.TryParseExact(Text(root, "occurred_on"), _dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var occurredOn)
            || Number(root, "count") is not { } read || read < 1
            || !root.TryGetProperty("exception", out var thrown))
        {
            return null;
        }

        ErrorBody? exception = null;

        if (kind == _endedUnexpectedly)
        {
            if (thrown.ValueKind != JsonValueKind.Null)
            {
                return null;
            }
        }
        else if ((exception = ReadError(thrown, top: true)) is null)
        {
            return null;
        }

        count = read;
        return new ReportBody(kind, site, version, build, system, systemVersion, architecture, runtime, uptime, screen, occurredOn, exception);
    }

    private static ErrorBody? ReadError(JsonElement element, bool top)
    {
        if (Text(element, "type") is not { } type || !IsName(type)
            || Number(element, "hresult") is not { } hresult
            || Number(element, "omitted") is not { } omitted || omitted < 0
            || !element.TryGetProperty("frames", out var listed) || listed.ValueKind != JsonValueKind.Array
            || listed.GetArrayLength() > (top ? MaximumFrames : MaximumInnerFrames))
        {
            return null;
        }

        List<FrameBody> frames = [];

        foreach (var each in listed.EnumerateArray())
        {
            if (Text(each, "asm") is not { } assembly
                || Text(each, "type") is not { } declaring
                || Text(each, "method") is not { } method
                || Number(each, "il") is not { } offset || offset < -1
                || !IsFrame(assembly, declaring, method))
            {
                return null;
            }

            frames.Add(new FrameBody(assembly, declaring, method, offset));
        }

        List<ErrorBody>? inner = null;

        if (top)
        {
            if (!element.TryGetProperty("inner", out var nested) || nested.ValueKind != JsonValueKind.Array || nested.GetArrayLength() > MaximumInner)
            {
                return null;
            }

            inner = [];

            foreach (var each in nested.EnumerateArray())
            {
                if (each.ValueKind != JsonValueKind.Object || ReadError(each, top: false) is not { } read)
                {
                    return null;
                }

                inner.Add(read);
            }
        }

        return new ErrorBody(type, hresult, omitted, frames, inner);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool IsFrame(string assembly, string type, string method) =>
        assembly is Unnamed or Dynamic or External
            ? type == assembly && method == assembly
            : IsAllowedAssembly(assembly) && Name(type) == type && Name(method) == method;

    private static bool IsNameCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '+' or '<' or '>' or '`' or '$' or '|' or '-';

    private static bool IsVersion(string text) =>
        text.Length is > 0 and <= 64 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    private static bool IsCommit(string text) => text.Length == 40 && text.All(char.IsAsciiHexDigitLower);

    private static bool IsNumbers(string text)
    {
        var parts = text.Split('.');
        return parts.Length is 2 or 3 && parts.All(part => part.Length is > 0 and <= 10 && part.All(char.IsAsciiDigit));
    }

    private static string SimpleName(string? display)
    {
        if (display is null)
        {
            return string.Empty;
        }

        var comma = display.IndexOf(',', StringComparison.Ordinal);
        return comma < 0 ? display : display[..comma];
    }

    // A generic type's arguments are assembly-qualified names, with spaces and commas.
    private static string WithoutTypeArguments(string type)
    {
        var bracket = type.IndexOf('[', StringComparison.Ordinal);
        return bracket < 0 ? type : type[..bracket];
    }

    private static string? Commit()
    {
        var informational = typeof(CrashScrubber).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        var commit = plus < 0 ? string.Empty : informational![(plus + 1)..];

        return IsCommit(commit) ? commit : null;
    }

    private static string OperatingSystemName() =>
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsLinux() ? "linux"
        : "other";

    private static string ArchitectureName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        _ => "other",
    };

    private static string Numbers(Version version) => version.Build >= 0
        ? string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}")
        : string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}");

    private sealed record ReportBody(
        string Kind,
        string? Site,
        string Version,
        string? Build,
        string Os,
        string OsVersion,
        string Arch,
        string Runtime,
        string? Uptime,
        string? Screen,
        DateOnly OccurredOn,
        ErrorBody? Exception);

    private sealed class ErrorBody(string type, int hresult, int omitted, List<FrameBody> frames, List<ErrorBody>? inner)
    {
        public string Type { get; } = type;

        public int HResult { get; } = hresult;

        public int Omitted { get; set; } = omitted;

        public List<FrameBody> Frames { get; } = frames;

        public List<ErrorBody>? Inner { get; } = inner;
    }

    private sealed record FrameBody(string Assembly, string Type, string Method, int Offset);
}
