using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Agents;

/// <summary>One row of <c>clients.toml</c>: ceilings for an agent, or a mode ceiling and its limit for one of its projects.</summary>
/// <remarks>A setting left out sets no ceiling. A row with <see cref="Project"/> holds only <see cref="Mode"/> and <see cref="Limit"/>.</remarks>
public sealed record CapsRow
{
    /// <summary>The bridge's <c>--client-label</c>, or <see cref="AgentNames.Any"/>.</summary>
    public required string Label { get; init; }

    /// <summary>The project or environment the row's mode covers, or null for the whole agent.</summary>
    public ProjectScope? Project { get; init; }

    /// <summary>The most the mode may give.</summary>
    public AgentMode? Mode { get; init; }

    /// <summary><see langword="false"/> keeps values to commands the agent runs.</summary>
    public bool? Values { get; init; }

    /// <summary><see langword="false"/> refuses the agent's commands.</summary>
    public bool? Run { get; init; }

    /// <summary>The most the agent can see, in <c>--expose</c>'s selectors.</summary>
    public EntryExposure? See { get; init; }

    /// <summary>
    /// The most Automatic may give where this row decides the mode: at most so many releases an hour, and
    /// only until a time; null sets none.
    /// </summary>
    public AccessLimit? Limit { get; init; }

    /// <summary>Whether another row holds the same label, scope and ceilings.</summary>
    /// <param name="other">The other row.</param>
    /// <returns><see langword="true"/> when every member matches, a ceiling on what is seen by its patterns.</returns>
    public bool Equals(CapsRow? other) =>
        other is not null
        && string.Equals(Label, other.Label, StringComparison.Ordinal)
        && Project == other.Project
        && Mode == other.Mode
        && Values == other.Values
        && Run == other.Run
        && Limit == other.Limit
        && (See is null ? other.See is null : other.See is not null && See.Globs.SequenceEqual(other.See.Globs, StringComparer.Ordinal));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Label), Project, Mode, Values, Run, Limit);
}

/// <summary>What changing <c>clients.toml</c> came to.</summary>
public enum CapsChange
{
    /// <summary>The file holds the change.</summary>
    Written = 0,

    /// <summary>The change left every row as the file holds it, so nothing was written.</summary>
    Unchanged = 1,

    /// <summary>The change made nothing of the file as it reads now, so nothing was written.</summary>
    Declined = 2,

    /// <summary>The file could not be read or parsed, so nothing was written.</summary>
    Unreadable = 3,

    /// <summary>The file could not be written, or another keypaste process kept it too long.</summary>
    Unwritten = 4,
}

/// <summary>Whether <c>clients.toml</c> can be used, and if not, why not.</summary>
public enum CapsFile
{
    /// <summary>It is absent or well formed.</summary>
    Usable = 0,

    /// <summary>It was read, and holds what keypaste cannot parse.</summary>
    Malformed = 1,

    /// <summary>It could not be read at all, so what it holds is not known.</summary>
    Unreadable = 2,
}

/// <summary>
/// This computer's ceilings on what each agent may get, in <c>~/.keypaste/clients.toml</c>, keyed by
/// the <c>--client-label</c> each bridge was started with.
/// </summary>
/// <remarks>
/// <para>
/// Every setting only takes away: what applies is the least of the vault's settings and these, so no
/// value written here, by hand or by a script, gives an agent more than the vault does. Narrowings are
/// kept here so that restoring the vault or merging it with an older copy cannot undo them.
/// </para>
/// <para>
/// An agent with rows of its own is held to them, and every other agent, an unnamed one included, to
/// the <see cref="AgentNames.Any"/> rows. Within one agent's rows a mode and its limit are capped by its
/// environment's row, else its project's row, else the agent's row, so one project can be held apart from
/// the rest; a row made for a project starts from the limit the row it replaces held there, so making it
/// lifts nothing.
/// </para>
/// <para>
/// A file that cannot be read or parsed refuses every agent request, as before (D-0360). The earlier
/// <c>policy = …</c> form is such a file: <c>policy</c> is not a setting here.
/// </para>
/// <para>
/// keypaste changes the file only through <see cref="TryChange"/>, from what it holds at that moment,
/// so a change waiting on a person never writes back a copy read before another change was made.
/// </para>
/// </remarks>
public sealed class AccessCaps
{
    /// <summary>The most rows a file may hold.</summary>
    public const int MaximumRows = 2048;

    /// <summary>The largest file read or written.</summary>
    public const int MaximumBytes = 256 * 1024;

    /// <summary>The suffix of the file beside it that keypaste's processes hold while one changes it.</summary>
    internal const string LockSuffix = ".lock";

    /// <summary>The suffix of the file beside it that a reset keeps a malformed file under.</summary>
    public const string KeptSuffix = ".unreadable";

    private const int _lockAttempts = 50;
    private const int _lockWaitMilliseconds = 20;
    private const string _section = "client";
    private const string _labelKey = "label";
    private const string _projectKey = "project";
    private const string _modeKey = "mode";
    private const string _valuesKey = "values";
    private const string _runKey = "run";
    private const string _seeKey = "see";
    private const string _perHourKey = "per_hour";
    private const string _untilKey = "until";
    private const string _yes = "yes";
    private const string _no = "no";
    private const string _on = "on";
    private const string _off = "off";

    private static readonly TomlLimits _limits = TomlLimits.Policy with
    {
        Tables = MaximumRows,
        Lines = (MaximumRows * 10) + 16,
        LineLength = 2304,
        Bytes = MaximumBytes,
    };

    private static readonly Lock _changing = new();

    private static readonly string[] _header =
    [
        "keypaste: this computer's ceilings on each agent, by the --client-label its bridge was started with.",
        "Nothing here gives more than the vault's settings. \"*\" is every agent without rows of its own.",
        "mode: ask, allow-for-a-while or automatic. values = \"no\": values reach only commands it runs.",
        "run = \"off\": no commands. see: [selectors] as --expose takes them.",
        "per_hour = <n> and until = \"<time in UTC>\": Automatic at most n an hour, and only until then, where the row's mode applies.",
        "A row with project = \"<project>\" or \"<project>:<environment>\" holds only mode, per_hour and until.",
        "A project row's mode and limit replace the agent row's for that project, as an environment's row replaces its project's; lower both to take it away.",
    ];

    private AccessCaps(IReadOnlyList<CapsRow> rows)
    {
        Rows = rows;
    }

    /// <summary>No rows: nothing is capped.</summary>
    public static AccessCaps Empty { get; } = new([]);

    /// <summary>The rows, in the order written.</summary>
    public IReadOnlyList<CapsRow> Rows { get; }

    /// <summary>The ceilings an agent is held to.</summary>
    /// <param name="label">The bridge's raw label, or null for a bridge started without one.</param>
    /// <returns>Its own rows when the label names an agent that has some, otherwise the <see cref="AgentNames.Any"/> rows.</returns>
    public AccessCeiling For(string? label)
    {
        if (AgentNames.IsNamed(label))
        {
            var own = Rows.Where(row => string.Equals(row.Label, label, StringComparison.Ordinal)).ToList();

            if (own.Count > 0)
            {
                return new AccessCeiling(own);
            }
        }

        return new AccessCeiling([.. Rows.Where(row => string.Equals(row.Label, AgentNames.Any, StringComparison.Ordinal))]);
    }

    /// <summary>These ceilings with one row set, replacing the row for the same label and scope.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The new ceilings.</returns>
    /// <exception cref="ArgumentException">The row is not one the file may hold, or the file could not hold it.</exception>
    public AccessCaps With(CapsRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!IsValid(row, out var error))
        {
            throw new ArgumentException(error, nameof(row));
        }

        var rows = Rows.ToList();
        var index = rows.FindIndex(existing => SameScope(existing, row));

        if (index >= 0)
        {
            rows[index] = row;
        }
        else
        {
            rows.Add(row);
        }

        return Bounded(rows, nameof(row));
    }

    /// <summary>These ceilings without the row for a label and scope.</summary>
    /// <param name="label">The label.</param>
    /// <param name="project">The scope, or null for the agent's own row.</param>
    /// <returns>The new ceilings.</returns>
    public AccessCaps Without(string label, ProjectScope? project)
    {
        ArgumentNullException.ThrowIfNull(label);

        return new AccessCaps([.. Rows.Where(row => !(string.Equals(row.Label, label, StringComparison.Ordinal) && row.Project == project))]);
    }

    /// <summary>
    /// These ceilings lowered for an agent wherever a change that gives it nothing more takes something
    /// away, so a restore or a merge that brings an older record back cannot undo it on this computer.
    /// </summary>
    /// <param name="label">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="current">What the change takes away from: what applies to the agent now, or what its records say (<see cref="AccessChange.Said"/>).</param>
    /// <param name="change">The change, a narrowing.</param>
    /// <returns>The new ceilings.</returns>
    /// <remarks>An agent with no rows of its own is held to <see cref="AgentNames.Any"/>'s, so it starts from a copy of them.</remarks>
    /// <exception cref="ArgumentException">The file could not hold them: too many rows, or too large.</exception>
    public AccessCaps Narrowed(string label, EffectiveAccess current, AccessChange change)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(change);

        var own = OwnRows(label);
        var ceiling = new AccessCeiling(own);
        var proposed = change.Proposed;

        if (current.Values && !proposed.Values)
        {
            SetAgent(own, label, row => row with { Values = false });
        }

        if (current.Run && !proposed.Run)
        {
            SetAgent(own, label, row => row with { Run = false });
        }

        // A ceiling that already holds back some of what the change sees is kept, so lowering never lifts it.
        if (current.Terms.Any(term => !proposed.See.Covers(term.See)))
        {
            SetAgent(own, label, row => row.See is { } held && !held.Covers(proposed.See) ? row : row with { See = proposed.See });
        }

        foreach (var (scope, project, environment) in AccessChange.Classes(current, proposed))
        {
            var next = proposed.Decide(project, environment).Mode;

            if (next < AccessChange.ModeNow(current, project, environment))
            {
                SetMode(own, label, scope, AgentModes.Least(ceiling.ModeFor(project, environment), next));
            }
        }

        // A limit the change adds or tightens on Automatic, kept here as a ceiling on the limit, so it holds
        // when the change is an agent's own, which nothing writes to the vault.
        foreach (var (scope, project, environment) in AccessChange.Classes(current, proposed))
        {
            var next = proposed.Decide(project, environment);
            var held = new AccessCeiling(own);
            var (_, limit) = held.LimitFor(project, environment);

            if (next.Mode == AgentMode.Automatic
                && !next.Limit.IsNone
                && held.ModeFor(project, environment) == AgentMode.Automatic
                && !Bounds([.. current.Terms.Select(term => term.Decide(project, environment).Limit), limit], next.Limit))
            {
                SetLimit(own, label, scope, Tighter(limit, next.Limit));
            }
        }

        return Replaced(label, own);
    }

    /// <summary>These ceilings raised for an agent exactly as far as a change the person confirmed gives it more.</summary>
    /// <param name="label">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="change">The confirmed change.</param>
    /// <returns>The new ceilings.</returns>
    /// <remarks>An agent with no rows of its own starts from a copy of <see cref="AgentNames.Any"/>'s, so lifting its ceiling lifts no other agent's.</remarks>
    /// <exception cref="ArgumentException">The file could not hold them: too many rows, or too large.</exception>
    public AccessCaps Lifted(string label, AccessChange change)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(change);

        var own = OwnRows(label);
        var ceiling = new AccessCeiling(own);
        var proposed = change.Proposed;

        foreach (var gain in change.Gains)
        {
            switch (gain.Dimension)
            {
                case AccessDimension.Values:
                    SetAgent(own, label, row => row with { Values = null });
                    break;

                case AccessDimension.Run:
                    SetAgent(own, label, row => row with { Run = null });
                    break;

                case AccessDimension.See:
                    SetAgent(own, label, row => row with { See = row.See is null ? null : proposed.See });
                    break;

                case AccessDimension.Mode:
                    var decided = proposed.Decide(gain.Scope?.Project, gain.Scope?.Environment);

                    if (ceiling.ModeFor(gain.Scope?.Project, gain.Scope?.Environment) < decided.Mode)
                    {
                        SetMode(own, label, gain.Scope, decided.Mode);
                    }

                    Relax(own, label, gain.Scope, decided.Row, decided.Limit);
                    break;

                case AccessDimension.Limit:
                    var limited = proposed.Decide(gain.Scope?.Project, gain.Scope?.Environment);
                    Relax(own, label, gain.Scope, limited.Row, limited.Limit);
                    break;
            }
        }

        return Replaced(label, own);
    }

    /// <summary>
    /// These ceilings lowered as a change names them, for when no owner holds the vault: only what
    /// provably gives the agent no more on this computer is written.
    /// </summary>
    /// <param name="label">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="caps">The new ceilings, when every part of the change lowers one or leaves it.</param>
    /// <returns>
    /// False when any part could give more, or is not a ceiling: values or commands on, a mode no lower
    /// than the ceiling, a selector outside the ceiling, a limit's removal or a reset. Those need the vault's settings.
    /// </returns>
    /// <remarks>
    /// A mode, and a limit on Automatic, is lowered on the row it names and on every narrower row of the
    /// agent's that would otherwise beat it.
    /// </remarks>
    /// <exception cref="ArgumentException">The file could not hold them: too many rows, or too large.</exception>
    public bool TryLower(string label, AccessEdit edit, [NotNullWhen(true)] out AccessCaps? caps)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(edit);

        caps = null;

        if (edit.Reset || edit.NoPerHour || edit.NoUntil || edit.Values == true || edit.Run == true)
        {
            return false;
        }

        var own = OwnRows(label);
        var ceiling = new AccessCeiling(own);

        if (edit.See is { } see && ceiling.See is { } seen && !seen.Covers(see))
        {
            return false;
        }

        if (edit.Mode is { } mode
            && (mode == AgentMode.Automatic || mode > ceiling.ModeFor(edit.Project?.Project, edit.Project?.Environment)))
        {
            return false;
        }

        if (edit.Values == false)
        {
            SetAgent(own, label, row => row with { Values = false });
        }

        if (edit.Run == false)
        {
            SetAgent(own, label, row => row with { Run = false });
        }

        if (edit.See is { } narrower)
        {
            SetAgent(own, label, row => row with { See = narrower });
        }

        if (edit.Mode is { } lowered)
        {
            for (var i = 0; i < own.Count; i++)
            {
                if (own[i].Mode > lowered && Within(own[i].Project, edit.Project))
                {
                    own[i] = own[i] with { Mode = lowered };
                }
            }

            SetMode(own, label, edit.Project, lowered);
        }

        if (edit.PerHour is not null || edit.Until is not null)
        {
            var wanted = new AccessLimit { PerHour = edit.PerHour, Until = edit.Until };

            for (var i = 0; i < own.Count; i++)
            {
                if (own[i].Project is not null && own[i].Project != edit.Project && Within(own[i].Project, edit.Project))
                {
                    own[i] = own[i] with { Limit = Tighter(own[i].Limit ?? AccessLimit.None, wanted) };
                }
            }

            SetLimit(own, label, edit.Project, Tighter(new AccessCeiling(own).LimitFor(edit.Project?.Project, edit.Project?.Environment).Limit, wanted));
        }

        caps = Replaced(label, own);
        return true;

        static bool Within(ProjectScope? row, ProjectScope? scope) =>
            scope is null
            || (row is not null && string.Equals(row.Project, scope.Project, StringComparison.Ordinal)
                && (scope.Environment is null || string.Equals(row.Environment, scope.Environment, StringComparison.Ordinal)));
    }

    /// <summary>The file's text.</summary>
    /// <returns>The header comment and one <c>[[client]]</c> table per row.</returns>
    public string Format()
    {
        var writer = new TomlWriter(_limits);

        foreach (var line in _header)
        {
            writer.Comment(line);
        }

        foreach (var row in Rows)
        {
            writer.Table(_section).Text(_labelKey, row.Label);

            if (row.Project is { } project)
            {
                writer.Text(_projectKey, project.Name);
            }

            if (row.Mode is { } mode)
            {
                writer.Text(_modeKey, AgentModes.Name(mode));
            }

            if (row.Limit?.PerHour is { } perHour)
            {
                writer.Number(_perHourKey, perHour);
            }

            if (row.Limit?.Until is { } until)
            {
                writer.Text(_untilKey, UntilTime.Recorded(until));
            }

            if (row.Values is { } values)
            {
                writer.Text(_valuesKey, values ? _yes : _no);
            }

            if (row.Run is { } run)
            {
                writer.Text(_runKey, run ? _on : _off);
            }

            if (row.See is { } see)
            {
                writer.Strings(_seeKey, see.Globs);
            }
        }

        return writer.ToString();
    }

    /// <summary>Reads a file's bytes.</summary>
    /// <param name="bytes">The file.</param>
    /// <param name="caps">What it holds, on success.</param>
    /// <param name="problem">What is wrong with it, naming its line, otherwise empty.</param>
    /// <returns>Whether every table is a well-formed row; anything else is malformed.</returns>
    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out AccessCaps? caps, out string problem)
    {
        caps = null;

        if (!Toml.TryDecode(bytes, _limits, out var text, out problem)
            || !Toml.TryParse(text, _limits, out var document, out problem))
        {
            return false;
        }

        List<CapsRow> rows = [];
        HashSet<(string Label, ProjectScope? Project)> scopes = [];

        foreach (var table in document.Tables)
        {
            if (!TryRead(table, out var row, out problem))
            {
                return false;
            }

            if (!scopes.Add((row.Label, row.Project)))
            {
                problem = At(table.Line, row.Project is { } project
                    ? $"'{row.Label}' has two rows for {project.Name}"
                    : $"'{row.Label}' has two rows");
                return false;
            }

            rows.Add(row);
        }

        caps = rows.Count == 0 ? Empty : new AccessCaps(rows);
        problem = string.Empty;
        return true;
    }

    /// <summary>Reads the file.</summary>
    /// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="caps">What it holds; <see cref="Empty"/> when there is no file.</param>
    /// <param name="problem">Why it could not be used, otherwise empty.</param>
    /// <returns>Whether the file is absent or well formed.</returns>
    public static bool TryLoad(string path, [NotNullWhen(true)] out AccessCaps? caps, out string problem)
    {
        ArgumentNullException.ThrowIfNull(path);

        caps = null;
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            caps = Empty;
            problem = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"it could not be read: {ex.Message}";
            return false;
        }

        return TryParse(bytes, out caps, out problem);
    }

    /// <summary>Writes the file through a temporary file and a move, owner-only on Unix.</summary>
    /// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="caps">What to write.</param>
    /// <param name="error">Why it was not written, otherwise empty.</param>
    /// <returns>Whether it was written whole.</returns>
    public static bool TrySave(string path, AccessCaps caps, out string error)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(caps);

        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            AtomicFile.Write(path, Encoding.UTF8.GetBytes(caps.Format()));
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Changes the file from what it holds at this moment, under the lock every keypaste process that
    /// changes it takes, so a change made meanwhile, by another agent's request, another process or a
    /// person's hand edit saved before the lock, is read here rather than written over.
    /// </summary>
    /// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="change">The new ceilings from those the file holds now, or null to leave the file as it is.</param>
    /// <param name="problem">Why it was not written, otherwise empty.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The lock is a file beside it, held open as the audit log's is, so one a killed process left
    /// behind holds nothing back. It keeps keypaste's own processes apart, not a text editor saving at
    /// the same instant.
    /// </remarks>
    /// <exception cref="ArgumentException">From <paramref name="change"/>: the file could not hold the new ceilings.</exception>
    public static CapsChange TryChange(string path, Func<AccessCaps, AccessCaps?> change, out string problem)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(change);

        lock (_changing)
        {
            using var held = Hold(path, out problem);

            if (held is null)
            {
                return CapsChange.Unwritten;
            }

            if (!TryLoad(path, out var current, out problem))
            {
                return CapsChange.Unreadable;
            }

            if (change(current) is not { } changed)
            {
                return CapsChange.Declined;
            }

            if (changed.Rows.SequenceEqual(current.Rows))
            {
                return CapsChange.Unchanged;
            }

            return TrySave(path, changed, out problem) ? CapsChange.Written : CapsChange.Unwritten;
        }
    }

    /// <summary>Whether the file can be used, and if not, whether keypaste read it and could not parse it, or could not read it.</summary>
    /// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="problem">What is wrong with it, otherwise empty.</param>
    /// <returns>What it is.</returns>
    public static CapsFile Probe(string path, out string problem)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            problem = string.Empty;
            return CapsFile.Usable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"it could not be read: {ex.Message}";
            return CapsFile.Unreadable;
        }

        return TryParse(bytes, out _, out problem) ? CapsFile.Usable : CapsFile.Malformed;
    }

    /// <summary>
    /// Sets a malformed file aside beside it, under the first name no earlier reset's copy holds, and
    /// starts an empty one, under the lock every keypaste process that changes it takes.
    /// </summary>
    /// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="kept">Where the file was set aside, once it was; otherwise empty.</param>
    /// <param name="problem">Why it was not reset, otherwise empty.</param>
    /// <returns>
    /// <see cref="CapsChange.Written"/> once it was reset; <see cref="CapsChange.Unchanged"/> when it can be
    /// used now; <see cref="CapsChange.Unreadable"/> when it could not be read, which leaves it as it is,
    /// since what it holds is not known; otherwise <see cref="CapsChange.Unwritten"/>.
    /// </returns>
    /// <remarks>
    /// A copy set aside is the only one left of the ceilings it held, often narrowings the person meant
    /// to keep, so none is ever replaced: the first is <c>clients.toml.unreadable</c>, and later ones
    /// <c>clients.toml.unreadable-2</c> and on.
    /// </remarks>
    public static CapsChange TryReset(string path, out string kept, out string problem)
    {
        ArgumentNullException.ThrowIfNull(path);

        kept = string.Empty;

        lock (_changing)
        {
            using var held = Hold(path, out problem);

            if (held is null)
            {
                return CapsChange.Unwritten;
            }

            switch (Probe(path, out problem))
            {
                case CapsFile.Usable:
                    return CapsChange.Unchanged;
                case CapsFile.Unreadable:
                    return CapsChange.Unreadable;
            }

            var aside = Aside(path);

            try
            {
                File.Move(path, aside, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = ex.Message;
                return CapsChange.Unwritten;
            }

            kept = aside;
            return TrySave(path, Empty, out problem) ? CapsChange.Written : CapsChange.Unwritten;
        }
    }

    /// <summary>The first name beside the file that no copy set aside before holds.</summary>
    private static string Aside(string path)
    {
        var first = path + KeptSuffix;

        if (!Path.Exists(first))
        {
            return first;
        }

        for (var next = 2; ; next++)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"{first}-{next}");

            if (!Path.Exists(name))
            {
                return name;
            }
        }
    }

    /// <summary>Takes the lock beside the file, waiting a moment for another keypaste process to finish with it.</summary>
    private static FileStream? Hold(string path, out string problem)
    {
        var lockPath = path + LockSuffix;

        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return null;
        }

        for (var attempt = 1; attempt <= _lockAttempts; attempt++)
        {
            try
            {
                var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                problem = string.Empty;
                return held;
            }
            catch (UnauthorizedAccessException ex)
            {
                problem = ex.Message;
                return null;
            }
            catch (IOException)
            {
                Thread.Sleep(_lockWaitMilliseconds);
            }
        }

        problem = $"{lockPath} is held by another keypaste process changing it";
        return null;
    }

    private static bool TryRead(TomlTable table, [NotNullWhen(true)] out CapsRow? row, out string problem)
    {
        row = null;

        if (!string.Equals(table.Name, _section, StringComparison.Ordinal))
        {
            problem = At(table.Line, $"[[{Safe(table.Name)}]] is not a section this file has; use [[{_section}]]");
            return false;
        }

        if (table.Pairs.FirstOrDefault(pair => pair.Key is not (_labelKey or _projectKey or _modeKey or _valuesKey or _runKey or _seeKey or _perHourKey or _untilKey)) is { } unknown)
        {
            problem = At(unknown.Line, $"'{Safe(unknown.Key)}' is not a setting; a client has label, project, mode, per_hour, until, values, run and see");
            return false;
        }

        if (!table.TryGet(_labelKey, out var label) || label.Value.Kind != TomlValueKind.Text)
        {
            problem = At(table.Line, $"a client needs {_labelKey} as a string");
            return false;
        }

        if (!AgentNames.IsValid(label.Value.Text, out var labelError))
        {
            problem = At(label.Line, labelError);
            return false;
        }

        ProjectScope? project = null;
        AgentMode? mode = null;
        bool? values = null;
        bool? run = null;
        EntryExposure? see = null;

        if (table.TryGet(_projectKey, out var projectPair)
            && (projectPair.Value.Kind != TomlValueKind.Text || !ProjectScope.TryParseName(projectPair.Value.Text, out project, out _)))
        {
            problem = At(projectPair.Line, "project is \"<project>\" or \"<project>:<environment>\", naming a project the vault's tags can name");
            return false;
        }

        if (table.TryGet(_modeKey, out var modePair))
        {
            if (modePair.Value.Kind != TomlValueKind.Text || !AgentModes.TryParse(modePair.Value.Text, out var parsed))
            {
                problem = At(modePair.Line, "mode is \"ask\", \"allow-for-a-while\" or \"automatic\"");
                return false;
            }

            mode = parsed;
        }

        if (!TryWord(table, _valuesKey, _yes, _no, out values, out problem)
            || !TryWord(table, _runKey, _on, _off, out run, out problem)
            || !TryLimit(table, out var limit, out problem))
        {
            return false;
        }

        if (table.TryGet(_seeKey, out var seePair))
        {
            if (seePair.Value.Kind != TomlValueKind.Array)
            {
                problem = At(seePair.Line, "see is an array of selectors, as --expose takes them");
                return false;
            }

            if (!EntryExposure.TryCreate(seePair.Value.Items, out see, out var seeError))
            {
                problem = At(seePair.Line, $"see: {seeError}");
                return false;
            }
        }

        if (project is not null && (values is not null || run is not null || see is not null))
        {
            problem = At(table.Line, "a row with a project holds only mode, per_hour and until; values, run and see apply to the whole agent");
            return false;
        }

        if (project is not null && mode is null)
        {
            problem = At(table.Line, "a row with a project holds its mode");
            return false;
        }

        if (mode is null && values is null && run is null && see is null && limit is null)
        {
            problem = At(table.Line, "a client row sets at least one of mode, per_hour, until, values, run and see");
            return false;
        }

        row = new CapsRow { Label = label.Value.Text, Project = project, Mode = mode, Values = values, Run = run, See = see, Limit = limit };
        problem = string.Empty;
        return true;
    }

    private static bool TryLimit(TomlTable table, out AccessLimit? limit, out string problem)
    {
        limit = null;
        problem = string.Empty;
        int? perHour = null;
        DateTimeOffset? until = null;

        if (table.TryGet(_perHourKey, out var perHourPair))
        {
            if (perHourPair.Value.Kind != TomlValueKind.Number || perHourPair.Value.Number is < 1 or > AccessLimit.MaximumPerHour)
            {
                problem = At(perHourPair.Line, string.Create(CultureInfo.InvariantCulture, $"per_hour is a whole number from 1 to {AccessLimit.MaximumPerHour}"));
                return false;
            }

            perHour = perHourPair.Value.Number;
        }

        if (table.TryGet(_untilKey, out var untilPair))
        {
            if (untilPair.Value.Kind != TomlValueKind.Text || !UntilTime.TryParseRecorded(untilPair.Value.Text, out var time))
            {
                problem = At(untilPair.Line, "until is a time in UTC, such as \"2026-12-31T23:59:59Z\"");
                return false;
            }

            until = time;
        }

        limit = perHour is null && until is null ? null : new AccessLimit { PerHour = perHour, Until = until };
        return true;
    }

    private static bool TryWord(TomlTable table, string key, string yes, string no, out bool? value, out string problem)
    {
        value = null;
        problem = string.Empty;

        if (!table.TryGet(key, out var pair))
        {
            return true;
        }

        if (pair.Value.Kind == TomlValueKind.Text && string.Equals(pair.Value.Text, yes, StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        if (pair.Value.Kind == TomlValueKind.Text && string.Equals(pair.Value.Text, no, StringComparison.Ordinal))
        {
            value = false;
            return true;
        }

        problem = At(pair.Line, $"{key} is \"{yes}\" or \"{no}\"");
        return false;
    }

    private static bool IsValid(CapsRow row, out string error)
    {
        if (!AgentNames.IsValid(row.Label, out error))
        {
            return false;
        }

        if (row.Project is not null && (row.Mode is null || row.Values is not null || row.Run is not null || row.See is not null))
        {
            error = "a row with a project holds only mode and its limit, and must hold mode";
            return false;
        }

        if (row.Mode is null && row.Values is null && row.Run is null && row.See is null && row.Limit is null)
        {
            error = "a client row sets at least one of mode, per_hour, until, values, run and see";
            return false;
        }

        if (row.Limit is { IsNone: true })
        {
            error = "a limit row sets per_hour, until or both";
            return false;
        }

        var writer = new TomlWriter(_limits);

        if (!writer.CanWrite(_labelKey, row.Label)
            || (row.Project is { } project && !writer.CanWrite(_projectKey, project.Name))
            || (row.See is { } see && !writer.CanWriteStrings(_seeKey, see.Globs)))
        {
            error = "the row cannot be written so that it reads back as itself";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>An agent's own rows, or for one with none, a copy of <see cref="AgentNames.Any"/>'s that it is held to now.</summary>
    private List<CapsRow> OwnRows(string label)
    {
        var own = Rows.Where(row => string.Equals(row.Label, label, StringComparison.Ordinal)).ToList();

        return own.Count > 0 || string.Equals(label, AgentNames.Any, StringComparison.Ordinal)
            ? own
            : [.. Rows.Where(row => string.Equals(row.Label, AgentNames.Any, StringComparison.Ordinal)).Select(row => row with { Label = label })];
    }

    /// <summary>
    /// These ceilings with an agent's rows replaced, each row that sets nothing left out; an agent left
    /// with none while <see cref="AgentNames.Any"/> has some keeps an explicit row of no ceiling, or it
    /// would fall back to those.
    /// </summary>
    private AccessCaps Replaced(string label, List<CapsRow> own)
    {
        List<CapsRow> kept = [.. own.Where(row => row.Mode is not null || row.Values is not null || row.Run is not null || row.See is not null || row.Limit is not null)];

        if (kept.Count == 0
            && !string.Equals(label, AgentNames.Any, StringComparison.Ordinal)
            && Rows.Any(row => string.Equals(row.Label, AgentNames.Any, StringComparison.Ordinal)))
        {
            kept.Add(new CapsRow { Label = label, Mode = AgentMode.Automatic });
        }

        foreach (var row in kept)
        {
            if (!IsValid(row, out var error))
            {
                throw new ArgumentException(error, nameof(own));
            }
        }

        return Bounded([.. Rows.Where(row => !string.Equals(row.Label, label, StringComparison.Ordinal)), .. kept], nameof(own));
    }

    /// <summary>Ceilings of these rows, refused when the file they make could not be read back.</summary>
    /// <exception cref="ArgumentException">More than <see cref="MaximumRows"/> rows, or a file larger than <see cref="MaximumBytes"/>.</exception>
    private static AccessCaps Bounded(List<CapsRow> rows, string parameter)
    {
        if (rows.Count > MaximumRows)
        {
            throw new ArgumentException($"a clients file holds at most {MaximumRows} rows", parameter);
        }

        var caps = new AccessCaps(rows);

        return Encoding.UTF8.GetByteCount(caps.Format()) <= MaximumBytes
            ? caps
            : throw new ArgumentException($"a clients file holds at most {MaximumBytes / 1024} KiB", parameter);
    }

    private static void SetAgent(List<CapsRow> own, string label, Func<CapsRow, CapsRow> change)
    {
        var index = own.FindIndex(row => row.Project is null);

        if (index >= 0)
        {
            own[index] = change(own[index]);
        }
        else
        {
            own.Add(change(new CapsRow { Label = label }));
        }
    }

    private static void SetMode(List<CapsRow> own, string label, ProjectScope? scope, AgentMode mode)
    {
        var index = own.FindIndex(row => row.Project == scope);

        if (index >= 0)
        {
            own[index] = own[index] with { Mode = mode };
        }
        else
        {
            own.Add(new CapsRow { Label = label, Project = scope, Mode = mode, Limit = Inherited(own, scope) });
        }
    }

    /// <summary>Sets the limit of the row for a scope, making one that holds the mode and limit the scope had when there is none.</summary>
    private static void SetLimit(List<CapsRow> own, string label, ProjectScope? scope, AccessLimit limit)
    {
        var kept = limit.IsNone ? null : limit;

        if (scope is null)
        {
            SetAgent(own, label, row => row with { Limit = kept });
            return;
        }

        var index = own.FindIndex(row => row.Project == scope);

        if (index >= 0)
        {
            own[index] = own[index] with { Limit = kept };
        }
        else
        {
            own.Add(new CapsRow { Label = label, Project = scope, Mode = new AccessCeiling(own).ModeFor(scope.Project, scope.Environment), Limit = kept });
        }
    }

    /// <summary>The limit a new row for a project or environment starts from: the one the row it replaces there held.</summary>
    private static AccessLimit? Inherited(List<CapsRow> own, ProjectScope? scope) =>
        scope is not null && new AccessCeiling(own).LimitFor(scope.Project, scope.Environment).Limit is { IsNone: false } limit ? limit : null;

    /// <summary>Lifts the limit a scope is held to as far as a confirmed change's own limit there allows: each part tighter than it goes.</summary>
    private static void Relax(List<CapsRow> own, string label, ProjectScope? scope, string row, AccessLimit wanted)
    {
        var (counted, limit) = new AccessCeiling(own).LimitFor(scope?.Project, scope?.Environment);
        var relaxed = new AccessLimit
        {
            PerHour = limit.PerHour is { } count && wanted.PerHour is { } allowed && count >= allowed && string.Equals(counted, row, StringComparison.Ordinal) ? count : null,
            Until = limit.Until is { } until && wanted.Until is { } end && until >= end ? until : null,
        };

        if (relaxed != limit)
        {
            SetLimit(own, label, scope, relaxed);
        }
    }

    /// <summary>The least of two limits: the earlier end and the fewer an hour.</summary>
    private static AccessLimit Tighter(AccessLimit first, AccessLimit second) => new()
    {
        PerHour = (first.PerHour, second.PerHour) switch
        {
            ({ } one, { } other) => Math.Min(one, other),
            (var one, var other) => one ?? other,
        },
        Until = (first.Until, second.Until) switch
        {
            ({ } one, { } other) => one < other ? one : other,
            (var one, var other) => one ?? other,
        },
    };

    /// <summary>Whether limits in force already allow no more than a limit: an end no later, and on any row no more an hour.</summary>
    private static bool Bounds(IReadOnlyList<AccessLimit> inForce, AccessLimit limit) =>
        (limit.Until is not { } until || inForce.Any(held => held.Until <= until))
        && (limit.PerHour is not { } perHour || inForce.Any(held => held.PerHour <= perHour));

    private static bool SameScope(CapsRow first, CapsRow second) =>
        string.Equals(first.Label, second.Label, StringComparison.Ordinal) && first.Project == second.Project;

    private static string Safe(string text) => EntryNameSanitizer.Sanitize(text, AgentNames.MaximumLength).Text;

    private static string At(int line, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"line {line}: {message}");
}

/// <summary>The rows of <c>clients.toml</c> one agent is held to, read as ceilings.</summary>
public sealed class AccessCeiling
{
    private readonly CapsRow? _agent;

    internal AccessCeiling(IReadOnlyList<CapsRow> rows)
    {
        Rows = rows;

        // Read once, so ceilings taken before a change keep the agent's row as it was while the change replaces it.
        _agent = rows.FirstOrDefault(row => row.Project is null);
    }

    /// <summary>No rows: nothing is capped.</summary>
    public static AccessCeiling None { get; } = new([]);

    /// <summary>The rows.</summary>
    public IReadOnlyList<CapsRow> Rows { get; }

    /// <summary>Whether values may be given.</summary>
    public bool Values => _agent?.Values ?? true;

    /// <summary>Whether commands may run.</summary>
    public bool Run => _agent?.Run ?? true;

    /// <summary>The most the agent can see, or null for no ceiling.</summary>
    public EntryExposure? See => _agent?.See;

    /// <summary>The most a mode may give for a project's environment: its row's, else its project's row's, else the agent's row's.</summary>
    /// <param name="project">The project, or null for an item in no project.</param>
    /// <param name="environment">The environment, or null for one no row names.</param>
    /// <returns>The ceiling; <see cref="AgentMode.Automatic"/> when no row sets one.</returns>
    internal AgentMode ModeFor(string? project, string? environment) =>
        Deciding(project, environment)?.Mode ?? _agent?.Mode ?? AgentMode.Automatic;

    /// <summary>The limit on Automatic for a project's environment, from the row its mode comes from.</summary>
    /// <param name="project">The project, or null for an item in no project.</param>
    /// <param name="environment">The environment, or null for one no row names.</param>
    /// <returns>That row's name as a record names its rows, such as <c>billing:staging</c> or empty for the agent's, which its hourly count goes by, and its limit; <see cref="AccessLimit.None"/> when it sets none.</returns>
    internal (string Row, AccessLimit Limit) LimitFor(string? project, string? environment) =>
        Deciding(project, environment) is { Limit: { } limit } row ? (row.Project?.Name ?? string.Empty, limit) : (string.Empty, AccessLimit.None);

    /// <summary>The row a project's environment is decided by: its environment's, else its project's, else the agent's.</summary>
    private CapsRow? Deciding(string? project, string? environment) =>
        (project is null
            ? null
            : Rows.FirstOrDefault(row => row.Project is { Environment: not null } scope && Same(scope, project, environment))
                ?? Rows.FirstOrDefault(row => row.Project is { Environment: null } scope && Same(scope, project, null)))
        ?? _agent;

    private static bool Same(ProjectScope scope, string project, string? environment) =>
        string.Equals(scope.Project, project, StringComparison.Ordinal)
        && string.Equals(scope.Environment, environment, StringComparison.Ordinal);
}
