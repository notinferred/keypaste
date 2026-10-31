using System.CommandLine;
using System.Globalization;
using Keypaste.Cli.Clipboard;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Sharing;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste share</c>: an entry's field as an end-to-end encrypted, expiring, view-limited link,
/// the list of links made, and their revocation (D-0354).
/// </summary>
/// <remarks>
/// The link carries the key, so it is a secret: it goes to the clipboard with <c>get</c>'s clear, or
/// to stdout only under <c>--print</c>. A link that cannot be handed over is withdrawn before the
/// command exits. Verbs that save take the vault's claim, so they never write under a running owner.
/// </remarks>
internal static class ShareCommand
{
    private static readonly TimeSpan _defaultTtl = TimeSpan.FromHours(24);

    private static readonly Argument<string> _item = Syntax.Operand("item", "the item, or a kp:// reference, which names its own field");
    private static readonly Option<string> _field = Syntax.Value("--field", "name", $"which field: {ShareService.FieldRule}; the password by default");
    private static readonly Option<string> _ttl = Syntax.Value("--ttl", "5m-7d", $"how long the link works, {_defaultTtl.TotalHours:0}h by default");
    private static readonly Option<string> _expires = new("--expires") { Hidden = true };
    private static readonly Option<string> _views = Syntax.Value("--views", "1-10", "how many times it opens, 1 by default");
    private static readonly Option<bool> _passphrase = Syntax.Flag(
        "--passphrase", "ask for one the recipient must also type, of at least 20 characters; `keypaste generate --words 6` makes one");
    private static readonly Option<string> _to = Syntax.Value("--to", "label", "a label for your own list, never sent");
    private static readonly Option<bool> _print = Syntax.Flag("--print", "print the link rather than copy it");
    private static readonly Option<string> _endpoint = Syntax.Value("--endpoint", "url", $"the share service, or set {ShareEndpoint.EnvironmentVariable}");
    private static readonly Option<bool> _offline = Syntax.Flag("--offline", "what this vault recorded, without asking the service");
    private static readonly Argument<string?> _id = Syntax.Optional("id", "the share's id, at least its first 6 characters");
    private static readonly Option<bool> _expired = Syntax.Flag("--expired", "every expired link instead");

    internal static CliVerb Command { get; } = new(
        "share",
        "create an encrypted, expiring link\n\n"
            + "encrypts one item's field on this machine and uploads only ciphertext. the link holds the key, so it is "
            + "copied to the clipboard; --print writes it to stdout instead.",
        (line, context) => Sending(line, context, Create))
    {
        new CliVerb("ls", "list your share links", (line, context) => Sending(line, context, List))
        {
            _offline, CliOptions.Json, CliOptions.Vault, CliOptions.Keyfile,
        },
        new CliVerb("revoke", "end a share link, or every expired one", (line, context) => Sending(line, context, Revoke))
        {
            _id, _expired, CliOptions.Vault, CliOptions.Keyfile,
        },
        _item, _field, _ttl, _expires, _views, _passphrase, _to, _print, _endpoint, CliOptions.Vault, CliOptions.Keyfile,
    };

    /// <summary>Runs a subcommand over the context's transport, or a new one of its own.</summary>
    private static int Sending(ParseResult line, CliContext context, Func<ParseResult, CliContext, HttpMessageHandler, int> body)
    {
        if (context.ShareTransport is { } given)
        {
            return body(line, context, given);
        }

        using var handler = ShareClient.CreateTransport();
        return body(line, context, handler);
    }

    private static int Create(ParseResult line, CliContext context, HttpMessageHandler handler)
    {
        var item = line.GetValue(_item)!;
        var field = line.GetValue(_field) ?? "password";
        KpReference? reference = null;

        if (item.StartsWith(KpReferences.Scheme, StringComparison.Ordinal))
        {
            if (!KpReferences.TryParse(item, out reference, out var referenceError))
            {
                return Refuse(context, referenceError, CliApp.ExitUsageError);
            }

            if (line.GetValue(_field) is not null)
            {
                return Refuse(context, "a kp:// reference names its field, so it takes no --field", CliApp.ExitUsageError);
            }

            field = reference is EntryReference named ? named.Field : ((EnvReference)reference).Key;
        }

        if (!ShareService.IsShareable(field))
        {
            return Refuse(context, $"--field is one of {ShareService.FieldRule}", CliApp.ExitUsageError);
        }

        if (line.GetValue(_ttl) is not null && line.GetValue(_expires) is not null)
        {
            return Refuse(context, "give --ttl or --expires, not both", CliApp.ExitUsageError);
        }

        var ttlText = line.GetValue(_ttl) ?? line.GetValue(_expires);
        var ttl = _defaultTtl;
        if (ttlText is not null && !TryTtl(ttlText, out ttl))
        {
            return Refuse(context, "--ttl takes a span such as 30m, 24h or 7d, from 5m to 7d", CliApp.ExitUsageError);
        }

        var views = 1;
        if (line.GetValue(_views) is { } viewsText
            && (!int.TryParse(viewsText, NumberStyles.None, CultureInfo.InvariantCulture, out views)
                || !ShareService.IsValidViews(views)))
        {
            return Refuse(context, $"--views takes a whole number from 1 to {ShareService.MaximumViews}", CliApp.ExitUsageError);
        }

        var recipient = line.GetValue(_to);
        if (!ShareService.IsValidRecipient(recipient))
        {
            return Refuse(context, $"--to takes a label of at most {ShareService.MaximumRecipientLength} characters", CliApp.ExitUsageError);
        }

        if (!ShareEndpoint.TryResolve(line.GetValue(_endpoint), context.Environment.Get(ShareEndpoint.EnvironmentVariable), out var endpoint, out var endpointError))
        {
            return Refuse(context, endpointError, CliApp.ExitUsageError);
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Refuse(context, locateError, CliApp.ExitUsageError);
        }

        var service = Service(context, handler, endpoint);
        var print = line.GetValue(_print);
        var copied = false;

        var exit = VaultSession.OpenHeld(path, line, context, vault =>
        {
            var entry = reference is null
                ? Resolve(vault, item, context, out var resolveExit)
                : Resolve(vault, reference, context, out resolveExit);
            if (entry is null)
            {
                return resolveExit;
            }

            using var passphrase = line.GetValue(_passphrase) ? ReadPassphrase(context) : null;
            if (line.GetValue(_passphrase) && passphrase is null)
            {
                return CliApp.ExitUsageError;
            }

            var outcome = service.CreateAsync(vault, new ShareRequest(entry, field, ttl, views, passphrase, recipient), CancellationToken.None)
                .GetAwaiter().GetResult();
            if (!outcome.Ok)
            {
                return Refuse(context, outcome.Message, CliApp.ExitInternalError);
            }

            if (print)
            {
                context.Stdout.WriteLine(outcome.Link);
            }
            else if (context.Clipboard.TrySet(outcome.Link!, out var clipboardError) != ClipboardStatus.Ok)
            {
                var withdrawn = service.RevokeAsync(vault, outcome.Info!.Id, CancellationToken.None).GetAwaiter().GetResult();
                context.Stderr.WriteLine(withdrawn.Ok
                    ? $"keypaste share: {clipboardError}; pass --print to get the link on stdout"
                    : $"keypaste share: {clipboardError}, and the link could not be withdrawn ({withdrawn.Message}); run keypaste share revoke {outcome.Info.Id}");
                return CliApp.ExitInternalError;
            }
            else
            {
                copied = true;
            }

            Announce(context, outcome.Info!, endpoint, print);
            return CliApp.ExitSuccess;
        });

        if (copied)
        {
            context.Clipboard.TryReadHash(out var expected, out _);
            context.ClipboardClear.ClearAfter(context.Clipboard, expected, TimeSpan.FromSeconds(GetCommand.DefaultTimeoutSeconds), context.Stderr);
        }

        return exit;
    }

    private static int List(ParseResult line, CliContext context, HttpMessageHandler handler)
    {
        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Refuse(context, locateError, CliApp.ExitUsageError);
        }

        var service = Service(context, handler, ShareEndpoint.Default);

        return VaultSession.Open(path, line, context, vault =>
        {
            var rows = service.ListAsync(vault, online: !line.GetValue(_offline), CancellationToken.None).GetAwaiter().GetResult();

            if (line.GetValue(CliOptions.Json))
            {
                CliJson.WriteArray(context.Stdout, rows, WriteJson);
            }
            else if (rows.Count == 0)
            {
                context.Stderr.WriteLine("  no share links");
            }
            else
            {
                WriteTable(context, rows);
            }

            return CliApp.ExitSuccess;
        });
    }

    private static int Revoke(ParseResult line, CliContext context, HttpMessageHandler handler)
    {
        var expired = line.GetValue(_expired);
        var id = line.GetValue(_id);
        if (expired == (id is not null))
        {
            return Refuse(context, "give one share id, or --expired", CliApp.ExitUsageError);
        }

        if (!expired && id!.Length < 6)
        {
            return Refuse(context, "give at least the first 6 characters of the id", CliApp.ExitUsageError);
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Refuse(context, locateError, CliApp.ExitUsageError);
        }

        var service = Service(context, handler, ShareEndpoint.Default);
        var done = context.ConsoleStyle.Glyph(context.Stderr, Mark.Done);

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            var store = new ShareStore(vault);

            if (expired)
            {
                var now = context.Clock.GetUtcNow();
                var removed = store.List().Where(info => info.Expires <= now).Count(info => store.Remove(info.Id));
                if (removed > 0)
                {
                    vault.Save();
                }

                context.Stderr.WriteLine(removed == 0
                    ? "  nothing has expired"
                    : $"  {done} forgot {removed} expired link{(removed == 1 ? string.Empty : "s")}");
                return CliApp.ExitSuccess;
            }

            var prefix = id!;
            var info = store.Find(prefix, out var ambiguous);
            if (info is null)
            {
                return ambiguous
                    ? Refuse(context, $"more than one share starts with '{Clean(prefix)}'; give more of the id", CliApp.ExitUsageError)
                    : Refuse(context, $"no share starts with '{Clean(prefix)}'", CliApp.ExitNotFound);
            }

            var outcome = service.RevokeAsync(vault, info.Id, CancellationToken.None).GetAwaiter().GetResult();
            if (!outcome.Ok)
            {
                return outcome.Failure == ShareFailure.None
                    ? Refuse(context, outcome.Message, CliApp.ExitInternalError)
                    : Refuse(context, $"{outcome.Message}; the link still opens", CliApp.ExitInternalError);
            }

            context.Stderr.WriteLine($"  {done} revoked {ShortId(info.Id)} {Dot(context.Stderr)} the link no longer opens");
            return CliApp.ExitSuccess;
        });
    }

    /// <summary>The entry a <c>kp://</c> reference names: the one entry holding an env variable as a field, or the named entry.</summary>
    private static EntryName? Resolve(Vault vault, KpReference reference, CliContext context, out int exit)
    {
        if (reference is EnvReference env)
        {
            return ResolveVariable(vault, env, context, out exit);
        }

        exit = CliApp.ExitSuccess;

        var name = ((EntryReference)reference).Entry;

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            exit = Refuse(context, "keypaste's own records cannot be shared", CliApp.ExitUsageError);
            return null;
        }

        VaultEntry? found;
        try
        {
            found = vault.Find(name);
        }
        catch (VaultException)
        {
            exit = Refuse(context, $"{Clean(reference.ToString())} names more than one item", CliApp.ExitUsageError);
            return null;
        }

        if (found is null)
        {
            exit = Refuse(context, $"no item at {Clean(reference.ToString())}", CliApp.ExitNotFound);
            return null;
        }

        return EntryName.Of(found);
    }

    /// <summary>The entry whose field holds a variable in its environment, refusing none and several.</summary>
    private static EntryName? ResolveVariable(Vault vault, EnvReference env, CliContext context, out int exit)
    {
        exit = CliApp.ExitSuccess;

        var holding = EnvResolution.List(vault, env.Project, env.Profile).Sources
            .Where(source => string.Equals(source.Key, env.Key, StringComparison.Ordinal))
            .ToList();

        switch (holding.Count)
        {
            case 1:
                return holding[0].Entry;

            case 0:
                exit = Refuse(context, $"no item holds {Clean(env.ToString())}", CliApp.ExitNotFound);
                return null;

            default:
                var entries = string.Join(", ", holding.Select(source => ApprovalPrompt.Shown(source.Entry)).Order(StringComparer.Ordinal));
                exit = Refuse(
                    context,
                    $"{Clean(env.ToString())} is on more than one item ({entries}); share it from one: keypaste share <item> --field {Clean(env.Key)}",
                    CliApp.ExitUsageError);
                return null;
        }
    }

    /// <summary>An entry path, or a bare title that names exactly one entry outside keypaste's own groups.</summary>
    private static EntryName? Resolve(Vault vault, string operand, CliContext context, out int exit)
    {
        exit = CliApp.ExitSuccess;

        VaultEntry? byPath;
        try
        {
            byPath = vault.Find(operand);
        }
        catch (VaultException)
        {
            byPath = null;
        }

        if (byPath is not null && !ReservedGroups.IsReserved(byPath.GroupPath))
        {
            return EntryName.Of(byPath);
        }

        if (byPath is not null || ReservedGroups.IsReserved(operand))
        {
            exit = Refuse(context, "keypaste's own records cannot be shared", CliApp.ExitUsageError);
            return null;
        }

        var titled = vault.ReadEntries()
            .Where(entry => string.Equals(entry.Title, operand, StringComparison.Ordinal))
            .ToList();

        switch (titled.Count)
        {
            case 1:
                return EntryName.Of(titled[0]);

            case 0:
                exit = Refuse(context, $"no item '{Clean(operand)}'", CliApp.ExitNotFound);
                return null;

            default:
                context.Stderr.WriteLine($"keypaste share: more than one item is called '{Clean(operand)}'; name one by its path:");
                foreach (var entry in titled)
                {
                    context.Stderr.WriteLine("  " + EntryNameSanitizer.SanitizePath(entry.Path).Text);
                }

                exit = CliApp.ExitUsageError;
                return null;
        }
    }

    private static SecretBuffer? ReadPassphrase(CliContext context)
    {
        var first = context.Prompt.ReadSecret("Passphrase: ");
        if (first is null || first.Length == 0)
        {
            first?.Dispose();
            context.Stderr.WriteLine("keypaste share: no passphrase given");
            return null;
        }

        if (!ShareService.IsValidPassphrase(first.Value))
        {
            first.Dispose();
            context.Stderr.WriteLine($"keypaste share: {ShareService.PassphraseLengthRule}, of Latin letters, digits, spaces and punctuation");
            return null;
        }

        if (!context.Prompt.IsInteractive)
        {
            return first;
        }

        using var second = context.Prompt.ReadSecret("Repeat passphrase: ");
        if (second is null || !first.ValueEquals(second))
        {
            first.Dispose();
            context.Stderr.WriteLine("keypaste share: the passphrases do not match");
            return null;
        }

        return first;
    }

    private static void Announce(CliContext context, ShareInfo info, Uri endpoint, bool printed)
    {
        var dot = Dot(context.Stderr);
        var views = info.Views == 1 ? "1 view" : $"{info.Views} views";
        var expires = TimeZoneInfo.ConvertTime(info.Expires, context.Clock.LocalTimeZone)
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var via = ShareEndpoint.IsDefault(endpoint) ? string.Empty : $" {dot} via {endpoint.Authority}";

        context.Stderr.WriteLine(
            $"  {context.ConsoleStyle.Glyph(context.Stderr, Mark.Done)} link {(printed ? "created" : "copied")} {dot} {views} {dot} expires {expires}{via}");

        if (info.Passphrase)
        {
            context.Stderr.WriteLine("  passphrase: send it separately");
        }
    }

    private static void WriteTable(CliContext context, IReadOnlyList<(ShareInfo Info, string Status, int? ViewsLeft)> rows)
    {
        var dot = Dot(context.Stdout);
        var table = rows.Select(row => new[]
        {
            ShortId(row.Info.Id),
            Clean(row.Info.What, path: true),
            row.Info.Recipient is { } to ? Clean(to) : "-",
            row.Info.Rule.Replace("·", dot, StringComparison.Ordinal),
            row.Status,
        }).ToList();

        string[] header = ["ID", "WHAT", "TO", "RULE", "STATUS"];
        var widths = Enumerable.Range(0, header.Length - 1)
            .Select(column => table.Append(header).Max(cells => cells[column].Length))
            .ToArray();

        context.Stdout.WriteLine("  " + Pad(header, widths) + header[^1]);
        for (var i = 0; i < table.Count; i++)
        {
            var status = rows[i].ViewsLeft is not null
                ? context.ConsoleStyle.Paint(context.Stdout, Tone.Accent, table[i][^1])
                : context.ConsoleStyle.Paint(context.Stdout, Tone.Muted, table[i][^1]);
            context.Stdout.WriteLine("  " + Pad(table[i], widths) + status);
        }
    }

    private static string Pad(string[] cells, int[] widths) =>
        string.Concat(widths.Select((width, column) => cells[column].PadRight(width + 2)));

    private static void WriteJson(System.Text.Json.Utf8JsonWriter json, (ShareInfo Info, string Status, int? ViewsLeft) row)
    {
        var (info, status, viewsLeft) = row;

        json.WriteString("id", info.Id);
        json.WriteString("what", info.What);
        json.WriteString("field", info.Field);
        if (info.Recipient is null)
        {
            json.WriteNull("to");
        }
        else
        {
            json.WriteString("to", info.Recipient);
        }

        json.WriteNumber("views", info.Views);
        json.WriteNumber("ttl_seconds", (long)Math.Round((info.Expires - info.Created).TotalSeconds));
        json.WriteBoolean("passphrase", info.Passphrase);
        json.WriteString("created", info.Created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        json.WriteString("expires", info.Expires.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        json.WriteString("status", viewsLeft is null ? status : "open");
        if (viewsLeft is { } left)
        {
            json.WriteNumber("views_left", left);
        }
        else
        {
            json.WriteNull("views_left");
        }
    }

    private static ShareService Service(CliContext context, HttpMessageHandler handler, Uri endpoint)
    {
        var auditPath = KeypasteHome.AuditPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        return new ShareService(
            new ShareClient(handler, endpoint),
            context.Clock,
            () => AuditLog.TryOpen(auditPath, context.Clock, out var log, out _) ? log : null);
    }

    private static bool TryTtl(string text, out TimeSpan ttl)
    {
        ttl = default;

        if (text.Length < 2
            || !int.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        ttl = text[^1] switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => TimeSpan.Zero,
        };

        return ShareService.IsValidTtl(ttl);
    }

    private static string ShortId(string id) => id[..Math.Min(8, id.Length)];

    private static string Dot(TextWriter writer) => ConsoleMarks.For(writer, Mark.Dot);

    private static string Clean(string text, bool path = false) =>
        path ? EntryNameSanitizer.SanitizePath(text).Text : EntryNameSanitizer.SanitizeProse(text).Text;

    private static int Refuse(CliContext context, string message, int exit)
    {
        context.Stderr.WriteLine($"keypaste share: {message}");
        return exit;
    }
}
