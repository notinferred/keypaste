using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Ipc;

// An agent's search of the items it may be shown, and what it found: names, origins, usernames, links and projects, never a value.
public static partial class ApproverProtocol
{
    internal const string FindKind = "find";

    /// <summary>Encodes an agent's search.</summary>
    /// <param name="request">What to search for, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    public static byte[] Encode(FindRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new FindRequestFrame(request), Wire.FindRequestFrame);
    }

    /// <summary>Encodes what a search found, bounded to what one frame can carry as <see cref="Encode(NamesReply)"/> is.</summary>
    /// <param name="reply">The items, or the reason there are none.</param>
    /// <returns>The frame's bytes, without a delimiter. Never over <see cref="MessageFramer.MaximumPayloadBytes"/>.</returns>
    public static byte[] Encode(FindReply reply) => WriteFound(Fitted(reply));

    /// <summary>A search's reply exactly as <see cref="Encode(FindReply)"/> sends it, measured as <see cref="Fitted(NamesReply)"/> measures a listing.</summary>
    /// <param name="reply">The items, or the reason there are none.</param>
    /// <returns>
    /// The items that fit one frame, complete only when nothing was dropped, with the projects a kept item is a member of, or the
    /// <see cref="Undersized"/> stand-in.
    /// </returns>
    public static FindReply Fitted(FindReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var kept = Fit(WriteFound(reply with { Found = [], Complete = false }).Length, FoundRow.Of(reply.Found), Wire.FoundRow);
        var fitted = kept == reply.Found.Count
            ? reply
            : reply with
            {
                Found = [.. reply.Found.Take(kept)],
                Projects = [.. reply.Projects.Where(project => reply.Found.Take(kept).Any(item => item.Entry.Projects.Contains(project.Name, StringComparer.Ordinal)))],
                Complete = false,
            };

        return WriteFound(fitted).Length <= MessageFramer.MaximumPayloadBytes
            ? fitted
            : reply with { Found = [], Projects = [], Reason = Undersized, Complete = false };
    }

    private static byte[] WriteFound(FindReply reply) =>
        JsonSerializer.SerializeToUtf8Bytes(new FindReplyFrame(reply), Wire.FindReplyFrame);

    /// <summary>Decodes an agent's search, bounded before anything is searched.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed search within every bound.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out FindRequest? request)
    {
        request = TryAttached(frame, FindKind, out var vault, out var session, out var root)
            && TryStrings(root, "exposure", out var exposure)
            && exposure.Count <= MaximumExposureGlobs
            && TryQueryPart(root, "text", out var text)
            && TryQueryPart(root, "host", out var host)
            && TryQueryPart(root, "related_to", out var relatedTo)
            && TryQueryPart(root, "project", out var project)
            && TryOptionalKind(root, "item_kind", out var kind)
                ? new FindRequest(exposure, text, host, kind, relatedTo)
                {
                    ClientLabel = Optional(root, "client_label"),
                    Vault = vault,
                    Session = session,
                    Project = project,
                }
                : null;

        return request is not null;
    }

    /// <summary>Decodes what a search found.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed search reply within every bound.</returns>
    /// <remarks>
    /// An origin is accepted only as <c>scheme://host:port</c> exactly as the owner writes one, a kind only when this version defines it,
    /// and a project only with its environments and a count of at least one item within their bounds; a frame without projects has none.
    /// </remarks>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out FindReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, FindKind)
                || !root.TryGetProperty("unlocked", out var unlocked)
                || unlocked.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("found", out var rows)
                || rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() > AccessIndex.MaximumResults)
            {
                return false;
            }

            var found = new List<FoundAccess>(rows.GetArrayLength());

            foreach (var row in rows.EnumerateArray())
            {
                if (!TryFound(row, out var item))
                {
                    return false;
                }

                found.Add(item);
            }

            if (!TryProjects(root, out var projects))
            {
                return false;
            }

            reply = new FindReply(
                unlocked.GetBoolean(),
                found,
                Optional(root, "reason") ?? string.Empty,
                TrueOnly(root, "complete"))
            {
                Projects = projects,
                Session = Optional(root, "session"),
                Refusal = TryInteger(root, "method", out var method)
                    ? Enum.IsDefined((AuditMethod)method) ? (AuditMethod)method : AuditMethod.Failed
                    : null,
                Recorded = TrueOnly(root, "recorded"),
            };

            return true;
        }
    }

    private static bool TryFound(JsonElement row, [NotNullWhen(true)] out FoundAccess? found)
    {
        found = null;
        WebOrigin? origin = null;

        if (row.ValueKind != JsonValueKind.Object
            || !TryString(row, "group", out var group)
            || !TryString(row, "title", out var title)
            || !TryInteger(row, "kind", out var kind)
            || !Enum.IsDefined((ItemKind)kind)
            || !TryOptionalString(row, "origin", out var originText)
            || (originText is not null && (!WebOrigin.TryRead(originText, out origin, out _) || origin.ToString() != originText))
            || !TryOptionalString(row, "username", out var username)
            || !TryStrings(row, "fields", out var fields)
            || !TryStrings(row, "tags", out var tags)
            || !row.TryGetProperty("links", out var links)
            || links.ValueKind != JsonValueKind.Array
            || links.GetArrayLength() > AccessIndex.MaximumLinks)
        {
            return false;
        }

        var linked = new List<AccessLink>(links.GetArrayLength());

        foreach (var link in links.EnumerateArray())
        {
            if (link.ValueKind != JsonValueKind.Object
                || !TryString(link, "group", out var linkGroup)
                || !TryString(link, "title", out var linkTitle)
                || !TryOptionalString(link, "site", out var site)
                || !TryStrings(link, "projects", out var projects)
                || !TryOptionalBoolean(link, "same_account", out var sameAccount)
                || !TryOptionalBoolean(link, "references", out var references)
                || !TryOptionalBoolean(link, "referenced_by", out var referencedBy))
            {
                return false;
            }

            linked.Add(new AccessLink(new EntryName(linkGroup, linkTitle), site, projects)
            {
                SameAccount = sameAccount,
                References = references,
                ReferencedBy = referencedBy,
            });
        }

        var listing = new ListedEntry(new EntryName(group, title), fields, tags);
        found = new FoundAccess(new IndexedEntry(listing, (ItemKind)kind, origin, username), linked);
        return true;
    }

    /// <summary>A reply's projects: absent reads as none, and present, each must be within its bounds.</summary>
    private static bool TryProjects(JsonElement root, out IReadOnlyList<AccessProject> projects)
    {
        projects = [];

        if (!root.TryGetProperty("projects", out var rows))
        {
            return true;
        }

        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > AccessIndex.MaximumProjects)
        {
            return false;
        }

        var decoded = new List<AccessProject>(rows.GetArrayLength());

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !TryString(row, "name", out var name)
                || !TryStrings(row, "environments", out var environments)
                || environments.Count > AccessIndex.MaximumEnvironments
                || !TryInteger(row, "items", out var items)
                || items is < 1 or > Approval.VaultEntryNameLister.MaximumNames)
            {
                return false;
            }

            decoded.Add(new AccessProject(name, environments, items));
        }

        projects = decoded;
        return true;
    }

    /// <summary>An optional flag: absent reads as false, and one present must be true or false.</summary>
    private static bool TryOptionalBoolean(JsonElement root, string name, out bool value)
    {
        value = false;

        if (!root.TryGetProperty(name, out var element))
        {
            return true;
        }

        value = element.ValueKind == JsonValueKind.True;
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    /// <summary>An optional part of a query: absent reads as none, and one present must be a string within <see cref="AccessQuery.MaximumPartLength"/>.</summary>
    private static bool TryQueryPart(JsonElement root, string name, out string? value) =>
        TryOptionalString(root, name, out value) && !(value?.Length > AccessQuery.MaximumPartLength);

    /// <summary>An optional kind: absent reads as any, and one present must be a kind this version defines.</summary>
    private static bool TryOptionalKind(JsonElement root, string name, out ItemKind? kind)
    {
        kind = null;

        if (!root.TryGetProperty(name, out _))
        {
            return true;
        }

        if (!TryInteger(root, name, out var number) || !Enum.IsDefined((ItemKind)number))
        {
            return false;
        }

        kind = (ItemKind)number;
        return true;
    }

    /// <summary>An optional string: absent reads as null, and one present must be a string.</summary>
    private static bool TryOptionalString(JsonElement root, string name, out string? value)
    {
        value = null;

        return !root.TryGetProperty(name, out _) || TryString(root, name, out value);
    }
}
