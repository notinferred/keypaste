using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Keypaste.Core.Agents;

/// <summary>What one ACAF record says, and whether all of it was understood.</summary>
/// <param name="Access">
/// The settings it gives when <paramref name="Understood"/>; otherwise what it was read as, every part
/// not understood taken at its narrowest, which applies only beneath the defaults.
/// </param>
/// <param name="Understood">Whether its version, every member and every value were understood.</param>
/// <param name="Rewritable">Whether keypaste may write over it: false for a record of a version keypaste does not know.</param>
public sealed record AcafRead(AgentAccess Access, bool Understood, bool Rewritable)
{
    /// <summary>
    /// What the record says, each part at its word: a value keypaste cannot read at its narrowest, a
    /// member or row it cannot place left out, and nothing else held back for what is not understood.
    /// A change is judged against it, since a change keeps every part it does not name as it reads
    /// here. It never applies, and it is <see cref="Access"/> for a record wholly understood.
    /// </summary>
    public AgentAccess Said { get; init; } = Access;
}

/// <summary>
/// The Agent Credential Access Format, version 1: one agent's settings as the JSON object an entry's
/// <c>acaf</c> field holds.
/// </summary>
/// <remarks>
/// <para>
/// <c>{"acaf":1,"see":["tag:env:*"],"values":true,"run":false,"mode":"allow-for-a-while","limit":{"perHour":20,"until":"2026-12-31T23:59:59Z"},"projects":[{"project":"billing","mode":"automatic"}]}</c>.
/// A member left out takes the defaults' value, and <c>limit</c> and <c>projects</c> may be left out.
/// A row names a project, or a project and one environment, as <see cref="ProjectScope.Name"/> does.
/// </para>
/// <para>
/// A record with any version, member or value this version does not understand can narrow and never
/// widen: it is read with each part not understood at its narrowest and applied beneath the defaults.
/// A change is written into a version-1 record where it names something, every other member and row
/// kept as the record holds them; a record of another version is never rewritten.
/// </para>
/// </remarks>
public static class AcafRecord
{
    /// <summary>The version this reads and writes.</summary>
    public const int Version = 1;

    private const string _versionKey = "acaf";
    private const string _seeKey = "see";
    private const string _valuesKey = "values";
    private const string _runKey = "run";
    private const string _modeKey = "mode";
    private const string _limitKey = "limit";
    private const string _projectsKey = "projects";
    private const string _perHourKey = "perHour";
    private const string _untilKey = "until";
    private const string _projectKey = "project";
    private const string _timestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    // A member named twice would leave the reader to pick one, and the writer may have meant the other (D-0325).
    private static readonly JsonDocumentOptions _strict = new() { AllowDuplicateProperties = false };

    private static readonly AcafRead _unreadable = new(AgentAccess.Defaults, false, true);

    // A part of a limit keypaste cannot read is judged as the least it could allow: one an hour, until long ago.
    private const int _narrowestPerHour = 1;
    private static readonly DateTimeOffset _narrowestUntil = DateTimeOffset.UnixEpoch;
    private static readonly AccessLimit _narrowestLimit = new() { PerHour = _narrowestPerHour, Until = _narrowestUntil };

    private static readonly string[] _recordOrder = [_versionKey, _seeKey, _valuesKey, _runKey, _modeKey, _limitKey, _projectsKey];
    private static readonly string[] _rowOrder = [_projectKey, _modeKey, _limitKey];
    private static readonly string[] _limitOrder = [_perHourKey, _untilKey];

    /// <summary>Reads a record.</summary>
    /// <param name="text">The field's text.</param>
    /// <returns>What it says and how much of it was understood.</returns>
    public static AcafRead Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            using var document = JsonDocument.Parse(text, _strict);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return _unreadable;
            }

            var understood = true;
            var rewritable = true;

            if (!root.TryGetProperty(_versionKey, out var version))
            {
                understood = false;
            }
            else if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != Version)
            {
                understood = false;
                rewritable = false;
            }

            var said = AgentAccess.Defaults;
            var modeUnderstood = true;
            var limitUnderstood = true;
            var projectsUnderstood = true;

            foreach (var member in root.EnumerateObject())
            {
                switch (member.Name)
                {
                    case _versionKey:
                        break;
                    case _seeKey:
                        if (TryReadSee(member.Value, out var see))
                        {
                            said = said with { See = see };
                        }
                        else
                        {
                            understood = false;
                            said = said with { See = EntryExposure.Nothing };
                        }

                        break;
                    case _valuesKey:
                        understood &= TryReadBoolean(member.Value, out var values);
                        said = said with { Values = values };
                        break;
                    case _runKey:
                        understood &= TryReadBoolean(member.Value, out var run);
                        said = said with { Run = run };
                        break;
                    case _modeKey:
                        modeUnderstood = TryReadMode(member.Value, out var mode);
                        said = said with { Mode = mode };
                        break;
                    case _limitKey:
                        said = said with { Limit = ReadLimit(member.Value, out limitUnderstood) };
                        break;
                    case _projectsKey:
                        said = said with { Projects = ReadProjects(member.Value, out projectsUnderstood) };
                        break;
                    default:
                        understood = false;
                        break;
                }
            }

            var access = said;

            // A limit not understood may have been all that bounded an Automatic default.
            if (!modeUnderstood || !limitUnderstood || !projectsUnderstood)
            {
                understood = false;
                access = said with
                {
                    Mode = AgentMode.Ask,
                    Limit = limitUnderstood ? said.Limit : AccessLimit.None,
                    Projects = projectsUnderstood ? said.Projects : [],
                };
            }

            return new AcafRead(access, understood, rewritable) { Said = said };
        }
        catch (JsonException)
        {
            return _unreadable;
        }
    }

    /// <summary>Writes a record.</summary>
    /// <param name="access">The settings.</param>
    /// <returns>The record's text.</returns>
    public static string Write(AgentAccess access) => Write(access, []);

    /// <summary>Writes a record over earlier ones, keeping every top-level member they hold that this version does not know.</summary>
    /// <param name="access">The settings.</param>
    /// <param name="previous">The records it replaces, the first one's member kept where two hold the same unknown member; null for an entry with no record.</param>
    /// <returns>The record's text.</returns>
    /// <exception cref="ArgumentException">A previous record is of a version keypaste does not know, so it is never rewritten.</exception>
    public static string Write(AgentAccess access, IReadOnlyList<string?> previous)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(previous);

        if (previous.Any(text => text is not null && !Read(text).Rewritable))
        {
            throw new ArgumentException("a record of a version keypaste does not know is never rewritten", nameof(previous));
        }

        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber(_versionKey, Version);
            json.WriteStartArray(_seeKey);

            foreach (var glob in access.See.Globs)
            {
                json.WriteStringValue(glob);
            }

            json.WriteEndArray();
            json.WriteBoolean(_valuesKey, access.Values);
            json.WriteBoolean(_runKey, access.Run);
            json.WriteString(_modeKey, AgentModes.Name(access.Mode));
            WriteLimit(json, access.Limit);

            if (access.Projects.Count > 0)
            {
                json.WriteStartArray(_projectsKey);

                foreach (var row in access.Projects)
                {
                    json.WriteStartObject();
                    json.WriteString(_projectKey, row.Scope.Name);
                    json.WriteString(_modeKey, AgentModes.Name(row.Mode));
                    WriteLimit(json, row.Limit);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            HashSet<string> carried = new(StringComparer.Ordinal);

            foreach (var text in previous)
            {
                foreach (var member in Unknown(text))
                {
                    if (carried.Add(member.Name))
                    {
                        member.WriteTo(json);
                    }
                }
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Writes a change into the records it replaces: into the first, where the change names something,
    /// every other member and row kept as the record holds them, then each top-level member this
    /// version does not know that only another of them holds.
    /// </summary>
    /// <param name="edit">The change.</param>
    /// <param name="previous">
    /// The records it replaces, the one the change is made to first; null for an entry with no record.
    /// A first that is no JSON object holds nothing to keep, so the change is made to the defaults.
    /// </param>
    /// <param name="written">The new record, the first one's own text when the change alters nothing in it; empty when it cannot be written.</param>
    /// <returns>
    /// <see cref="AgentAccessWrite.Written"/>; <see cref="AgentAccessWrite.ProjectsUnreadable"/> when the
    /// change is to a row and the first record's <c>projects</c> is not a list, so no row can be placed
    /// in it without losing it; <see cref="AgentAccessWrite.TooManyProjects"/> when it would add a
    /// row to a list already holding <see cref="AgentAccess.MaximumProjects"/>; or
    /// <see cref="AgentAccessWrite.ProjectsOverLimit"/> when the list already holds more.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="previous"/> is empty, or holds a record of a version keypaste does not know, which it never rewrites.</exception>
    public static AgentAccessWrite WriteChange(AccessEdit edit, IReadOnlyList<string?> previous, out string written)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(previous);

        if (previous.Count == 0)
        {
            throw new ArgumentException("a change is written into a record", nameof(previous));
        }

        if (previous.Any(text => text is not null && !Read(text).Rewritable))
        {
            throw new ArgumentException("a record of a version keypaste does not know is never rewritten", nameof(previous));
        }

        written = string.Empty;

        if (Parsed(previous[0]) is not { } record)
        {
            written = Write(edit.Apply(AgentAccess.Defaults), previous);
            return AgentAccessWrite.Written;
        }

        var before = record.DeepClone();

        if (!record.ContainsKey(_versionKey))
        {
            record.Insert(0, _versionKey, JsonValue.Create(Version));
        }

        if (edit.Reset && edit.Project is null)
        {
            var defaults = Parsed(Write(edit.Apply(AgentAccess.Defaults)))!;

            foreach (var name in _recordOrder.Skip(1))
            {
                if (defaults[name] is { } value)
                {
                    Put(record, name, value.DeepClone(), _recordOrder);
                }
                else
                {
                    record.Remove(name);
                }
            }
        }
        else if (edit.Project is { } scope)
        {
            var outcome = ChangeRow(record, edit, scope, Read(previous[0]!).Access);

            if (outcome != AgentAccessWrite.Written)
            {
                return outcome;
            }
        }
        else
        {
            if (edit.Mode is { } mode)
            {
                Put(record, _modeKey, JsonValue.Create(AgentModes.Name(mode)), _recordOrder);
            }

            ChangeLimit(record, edit, _recordOrder);
        }

        if (edit.See is { } see)
        {
            Put(record, _seeKey, new JsonArray([.. see.Globs.Select(glob => (JsonNode?)JsonValue.Create(glob))]), _recordOrder);
        }

        if (edit.Values is { } values)
        {
            Put(record, _valuesKey, JsonValue.Create(values), _recordOrder);
        }

        if (edit.Run is { } run)
        {
            Put(record, _runKey, JsonValue.Create(run), _recordOrder);
        }

        foreach (var other in previous.Skip(1).Select(Parsed).OfType<JsonObject>())
        {
            foreach (var (name, value) in other)
            {
                if (!IsKnown(name) && !record.ContainsKey(name))
                {
                    record.Add(name, value?.DeepClone());
                }
            }
        }

        written = JsonNode.DeepEquals(before, record) ? previous[0]! : Text(record);
        return AgentAccessWrite.Written;
    }

    /// <summary>Changes, adds or with a reset removes the row a change names, collapsing two rows for its scope into one.</summary>
    /// <param name="record">The record.</param>
    /// <param name="edit">The change, which names a project.</param>
    /// <param name="scope">The project or environment it names.</param>
    /// <param name="read">What the record says, for the mode a new row with no mode of its own takes: the one that applies there now.</param>
    private static AgentAccessWrite ChangeRow(JsonObject record, AccessEdit edit, ProjectScope scope, AgentAccess read)
    {
        var held = record.TryGetPropertyValue(_projectsKey, out var projects);

        if (held && projects is not JsonArray)
        {
            return edit.Reset ? AgentAccessWrite.Written : AgentAccessWrite.ProjectsUnreadable;
        }

        var rows = projects as JsonArray ?? new JsonArray();
        var matching = rows.OfType<JsonObject>().Where(candidate => Names(candidate, scope)).ToList();

        foreach (var removed in edit.Reset ? matching : matching.Skip(1))
        {
            rows.Remove(removed);
        }

        if (edit.Reset)
        {
            if (matching.Count > 0 && rows.Count == 0)
            {
                record.Remove(_projectsKey);
            }

            return AgentAccessWrite.Written;
        }

        var row = matching.FirstOrDefault();

        // A list longer than keypaste keeps reads as no rows at all, so a change to one could not be judged.
        if (rows.Count > AgentAccess.MaximumProjects)
        {
            return AgentAccessWrite.ProjectsOverLimit;
        }

        if (row is null && rows.Count == AgentAccess.MaximumProjects)
        {
            return AgentAccessWrite.TooManyProjects;
        }

        if (row is null)
        {
            row = new JsonObject
            {
                [_projectKey] = scope.Name,
                [_modeKey] = AgentModes.Name(edit.Mode ?? read.Decide(scope.Project, scope.Environment).Mode),
            };

            // As a JsonNode, or the generic Add, which trimming and NativeAOT refuse, is the one chosen.
            rows.Add((JsonNode)row);
        }
        else if (edit.Mode is { } mode)
        {
            Put(row, _modeKey, JsonValue.Create(AgentModes.Name(mode)), _rowOrder);
        }

        ChangeLimit(row, edit, _rowOrder);

        if (!held)
        {
            Put(record, _projectsKey, rows, _recordOrder);
        }

        return AgentAccessWrite.Written;
    }

    /// <summary>Sets or removes the parts of a limit a change names, keeping the rest of it; a limit that is not an object is replaced.</summary>
    private static void ChangeLimit(JsonObject holder, AccessEdit edit, string[] order)
    {
        if (edit.PerHour is null && !edit.NoPerHour && edit.Until is null && !edit.NoUntil)
        {
            return;
        }

        if (holder[_limitKey] is not JsonObject limit)
        {
            limit = new JsonObject();
            Put(holder, _limitKey, limit, order);
        }

        if (edit.PerHour is { } perHour)
        {
            Put(limit, _perHourKey, JsonValue.Create(perHour), _limitOrder);
        }
        else if (edit.NoPerHour)
        {
            limit.Remove(_perHourKey);
        }

        if (edit.Until is { } until)
        {
            Put(limit, _untilKey, JsonValue.Create(until.UtcDateTime.ToString(_timestampFormat, CultureInfo.InvariantCulture)), _limitOrder);
        }
        else if (edit.NoUntil)
        {
            limit.Remove(_untilKey);
        }

        if (limit.Count == 0)
        {
            holder.Remove(_limitKey);
        }
    }

    /// <summary>Whether a row names a scope, as a row this version understands names it.</summary>
    private static bool Names(JsonObject row, ProjectScope scope) =>
        row[_projectKey] is JsonValue name
        && name.GetValueKind() == JsonValueKind.String
        && ProjectScope.TryParseName(name.GetValue<string>(), out var named, out _)
        && named == scope;

    /// <summary>Sets a member, in its place when the object holds it, otherwise after the members that come before it in <paramref name="order"/>.</summary>
    private static void Put(JsonObject json, string name, JsonNode? value, string[] order)
    {
        if (json.ContainsKey(name))
        {
            json[name] = value;
            return;
        }

        var rank = Array.IndexOf(order, name);
        var at = 0;
        var index = 0;

        foreach (var member in json)
        {
            index++;
            var known = Array.IndexOf(order, member.Key);

            if (known >= 0 && known < rank)
            {
                at = index;
            }
        }

        json.Insert(at, name, value);
    }

    /// <summary>A record's text as a JSON object, or null when it is none.</summary>
    private static JsonObject? Parsed(string? text)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: _strict) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonObject record)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            record.WriteTo(json);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static bool IsKnown(string member) =>
        member is _versionKey or _seeKey or _valuesKey or _runKey or _modeKey or _limitKey or _projectsKey;

    /// <summary>The top-level members of a record this version does not know, each copied out of the parsed text.</summary>
    private static List<JsonProperty> Unknown(string? text)
    {
        if (text is null)
        {
            return [];
        }

        try
        {
            // Cloned so the members outlive the document they were parsed from.
            using var document = JsonDocument.Parse(text, _strict);

            return document.RootElement.ValueKind == JsonValueKind.Object
                ? [.. document.RootElement.Clone().EnumerateObject().Where(member => !IsKnown(member.Name))]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void WriteLimit(Utf8JsonWriter json, AccessLimit limit)
    {
        if (limit.IsNone)
        {
            return;
        }

        json.WriteStartObject(_limitKey);

        if (limit.PerHour is { } perHour)
        {
            json.WriteNumber(_perHourKey, perHour);
        }

        if (limit.Until is { } until)
        {
            json.WriteString(_untilKey, until.UtcDateTime.ToString(_timestampFormat, CultureInfo.InvariantCulture));
        }

        json.WriteEndObject();
    }

    private static bool TryReadBoolean(JsonElement element, out bool value)
    {
        value = element.ValueKind == JsonValueKind.True;
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    private static bool TryReadMode(JsonElement element, out AgentMode mode)
    {
        mode = AgentMode.Ask;
        return element.ValueKind == JsonValueKind.String && AgentModes.TryParse(element.GetString()!, out mode);
    }

    private static bool TryReadSee(JsonElement element, [NotNullWhen(true)] out EntryExposure? see)
    {
        see = null;

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > EntryExposure.MaximumGlobs)
        {
            return false;
        }

        List<string> globs = [];

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            globs.Add(item.GetString()!);
        }

        return EntryExposure.TryCreate(globs, out see, out _);
    }

    /// <summary>A limit as it says: each part at its word, one keypaste cannot read at its narrowest, and a member it does not know left out.</summary>
    /// <param name="element">The limit.</param>
    /// <param name="understood">Whether every part was understood.</param>
    /// <returns>The limit; one that is no object, or has neither part, reads as the narrowest.</returns>
    private static AccessLimit ReadLimit(JsonElement element, out bool understood)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.EnumerateObject().Any())
        {
            understood = false;
            return _narrowestLimit;
        }

        understood = true;
        int? perHour = null;
        DateTimeOffset? until = null;

        foreach (var member in element.EnumerateObject())
        {
            switch (member.Name)
            {
                case _perHourKey:
                    understood &= TryReadPerHour(member.Value, out var count);
                    perHour = count;
                    break;
                case _untilKey:
                    understood &= TryReadUntil(member.Value, out var time);
                    until = time;
                    break;
                default:
                    understood = false;
                    break;
            }
        }

        return new AccessLimit { PerHour = perHour, Until = until };
    }

    /// <summary>Reads an hourly count, or gives the narrowest when it cannot.</summary>
    private static bool TryReadPerHour(JsonElement element, out int perHour)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out perHour) && perHour is >= 1 and <= AccessLimit.MaximumPerHour)
        {
            return true;
        }

        perHour = _narrowestPerHour;
        return false;
    }

    /// <summary>Reads a limit's time, or gives the narrowest when it cannot.</summary>
    private static bool TryReadUntil(JsonElement element, out DateTimeOffset until)
    {
        if (element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParseExact(
                element.GetString(),
                _timestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out until))
        {
            return true;
        }

        until = _narrowestUntil;
        return false;
    }

    /// <summary>
    /// Project rows as they say: each row whose project can be read, its mode at its word or else
    /// <see cref="AgentMode.Ask"/>, its limit as <see cref="ReadLimit"/> reads it, and two rows for one
    /// scope as the least of them; a row whose project cannot be read is left out, since no change can
    /// name it.
    /// </summary>
    /// <param name="element">The rows.</param>
    /// <param name="understood">Whether every row and every part of one was understood.</param>
    /// <returns>
    /// The rows; none when they are no list, or more than <see cref="AgentAccess.MaximumProjects"/>,
    /// which a change can only reset.
    /// </returns>
    private static List<ProjectAccess> ReadProjects(JsonElement element, out bool understood)
    {
        understood = element.ValueKind == JsonValueKind.Array && element.GetArrayLength() <= AgentAccess.MaximumProjects;

        if (!understood)
        {
            return [];
        }

        List<ProjectAccess> rows = [];

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                understood = false;
                continue;
            }

            ProjectScope? scope = null;
            var mode = AgentMode.Ask;
            var modeRead = false;
            var limit = AccessLimit.None;

            foreach (var member in item.EnumerateObject())
            {
                switch (member.Name)
                {
                    case _projectKey:
                        if (member.Value.ValueKind != JsonValueKind.String
                            || !ProjectScope.TryParseName(member.Value.GetString()!, out scope, out _))
                        {
                            understood = false;
                        }

                        break;
                    case _modeKey:
                        modeRead = TryReadMode(member.Value, out mode);
                        break;
                    case _limitKey:
                        limit = ReadLimit(member.Value, out var limitRead);
                        understood &= limitRead;
                        break;
                    default:
                        understood = false;
                        break;
                }
            }

            understood &= modeRead && scope is not null;

            if (scope is null)
            {
                continue;
            }

            var index = rows.FindIndex(row => row.Scope == scope);

            if (index < 0)
            {
                rows.Add(new ProjectAccess(scope, mode, limit));
                continue;
            }

            understood = false;
            rows[index] = new ProjectAccess(scope, AgentModes.Least(rows[index].Mode, mode), Tighter(rows[index].Limit, limit));
        }

        return rows;
    }

    /// <summary>The least of two limits: the lower hourly count and the earlier time, each where either sets one.</summary>
    private static AccessLimit Tighter(AccessLimit first, AccessLimit second) => new()
    {
        PerHour = first.PerHour is { } firstCount && second.PerHour is { } secondCount ? Math.Min(firstCount, secondCount) : first.PerHour ?? second.PerHour,
        Until = first.Until is { } firstTime && second.Until is { } secondTime ? (firstTime < secondTime ? firstTime : secondTime) : first.Until ?? second.Until,
    };
}
