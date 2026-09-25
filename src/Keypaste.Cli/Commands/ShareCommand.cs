using System.Globalization;
using System.Net;
using Keypaste.Cli.Clipboard;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Sharing;

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

    private static readonly OptionSpec[] _vaultOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
    ];

    private static readonly OptionSpec[] _createOptions =
    [
        .. _vaultOptions,
        new("field", TakesValue: true),
        new("ttl", TakesValue: true),
        new("expires", TakesValue: true),
        new("views", TakesValue: true),
        new("passphrase", TakesValue: false),
        new("to", TakesValue: true),
        new("print", TakesValue: false),
        new("endpoint", TakesValue: true),
    ];

    private static readonly OptionSpec[] _listOptions =
    [
        .. _vaultOptions,
        new("offline", TakesValue: false),
        new(CliJson.Option, TakesValue: false),
    ];

    private static readonly OptionSpec[] _revokeOptions =
    [
        .. _vaultOptions,
        new("expired", TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };

        return Execute(args, context, handler);
    }

    /// <summary>The verb over a given transport, so tests speak to a fake server.</summary>
    internal static int Execute(string[] args, CliContext context, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);

        return args.Length > 1 ? args[1] switch
        {
            "ls" => List(args, context, handler),
            "revoke" => Revoke(args, context, handler),
            "help" or "--help" or "-h" => Usage(context.Stdout, CliApp.ExitSuccess),
            _ => Create(args, context, handler),
        } : Usage(context.Stderr, CliApp.ExitUsageError);
    }

    private static int Usage(TextWriter writer, int exit)
    {
        writer.WriteLine("usage: keypaste share <entry> [--field password|username|url|notes|login] [--ttl 1h|24h|7d]");
        writer.WriteLine("                      [--views 1|3|10] [--passphrase] [--to <label>] [--print] [--endpoint <url>]");
        writer.WriteLine("       keypaste share ls [--offline] [--json]");
        writer.WriteLine("       keypaste share revoke <id> | --expired");
        writer.WriteLine();
        writer.WriteLine("Encrypts one entry's field on this machine and uploads only ciphertext. The link");
        writer.WriteLine("holds the key, so it is copied to the clipboard; --print writes it to stdout instead.");
        writer.WriteLine("--ttl takes 5m to 7d (default 24h), --views 1 to 10 (default 1). --to is a label");
        writer.WriteLine("for your own list and is never sent. <entry> may be a kp:// reference, which names");
        writer.WriteLine("its own field.");
        return exit;
    }

    private static int Create(string[] args, CliContext context, HttpMessageHandler handler)
    {
        if (!CommandLine.TryParse(args, 1, _createOptions, out var line, out var error))
        {
            return Refuse(context, error, CliApp.ExitUsageError);
        }

        if (line.WantsHelp)
        {
            return Usage(context.Stdout, CliApp.ExitSuccess);
        }

        if (line.Operands.Count != 1)
        {
            return Refuse(context, "expected exactly one entry", CliApp.ExitUsageError);
        }

        var field = line.Value("field") ?? "password";
        KpReference? reference = null;

        if (line.Operands[0].StartsWith(KpReferences.Scheme, StringComparison.Ordinal))
        {
            if (!KpReferences.TryParse(line.Operands[0], out reference, out var referenceError))
            {
                return Refuse(context, referenceError, CliApp.ExitUsageError);
            }

            if (line.Value("field") is not null)
            {
                return Refuse(context, "a kp:// reference names its field, so it takes no --field", CliApp.ExitUsageError);
            }

            field = reference is EntryReference named ? named.Field : "password";
        }

        if (!ShareService.Fields.Contains(field, StringComparer.Ordinal))
        {
            return Refuse(context, "--field is one of password, username, url, notes or login", CliApp.ExitUsageError);
        }

        if (line.Value("ttl") is not null && line.Value("expires") is not null)
        {
            return Refuse(context, "give --ttl or --expires, not both", CliApp.ExitUsageError);
        }

        var ttlText = line.Value("ttl") ?? line.Value("expires");
        var ttl = _defaultTtl;
        if (ttlText is not null && !TryTtl(ttlText, out ttl))
        {
            return Refuse(context, "--ttl takes a span such as 30m, 24h or 7d, from 5m to 7d", CliApp.ExitUsageError);
        }

        var views = 1;
        if (line.Value("views") is { } viewsText
            && (!int.TryParse(viewsText, NumberStyles.None, CultureInfo.InvariantCulture, out views)
                || views is < 1 or > ShareService.MaximumViews))
        {
            return Refuse(context, $"--views takes a whole number from 1 to {ShareService.MaximumViews}", CliApp.ExitUsageError);
        }

        var recipient = line.Value("to");
        if (recipient is { Length: > ShareService.MaximumRecipientLength })
        {
            return Refuse(context, $"--to takes a label of at most {ShareService.MaximumRecipientLength} characters", CliApp.ExitUsageError);
        }

        if (!ShareEndpoint.TryResolve(line.Value("endpoint"), context.Environment.Get(ShareEndpoint.EnvironmentVariable), out var endpoint, out var endpointError))
        {
            return Refuse(context, endpointError, CliApp.ExitUsageError);
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Refuse(context, locateError, CliApp.ExitUsageError);
        }

        var service = Service(context, handler, endpoint);
        var print = line.HasFlag("print");
        var copied = false;

        var exit = VaultSession.OpenHeld(path, line, context, vault =>
        {
            var entry = reference is null
                ? Resolve(vault, line.Operands[0], context, out var resolveExit)
                : Resolve(vault, reference, context, out resolveExit);
            if (entry is null)
            {
                return resolveExit;
            }

            using var passphrase = line.HasFlag("passphrase") ? ReadPassphrase(context) : null;
            if (line.HasFlag("passphrase") && passphrase is null)
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

    private static int List(string[] args, CliContext context, HttpMessageHandler handler)
    {
        if (!CommandLine.TryParse(args, 2, _listOptions, out var line, out var error))
        {
            return Refuse(context, error, CliApp.ExitUsageError);
        }

        if (line.WantsHelp)
        {
            return Usage(context.Stdout, CliApp.ExitSuccess);
        }

        if (line.Operands.Count > 0)
        {
            return Refuse(context, $"unexpected argument '{line.Operands[0]}'", CliApp.ExitUsageError);
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Refuse(context, locateError, CliApp.ExitUsageError);
        }

        var service = Service(context, handler, ShareEndpoint.Default);

        return VaultSession.Open(path, line, context, vault =>
        {
            var rows = service.ListAsync(vault, online: !line.HasFlag("offline"), CancellationToken.None).GetAwaiter().GetResult();

            if (line.HasFlag(CliJson.Option))
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

    private static int Revoke(string[] args, CliContext context, HttpMessageHandler handler)
    {
        if (!CommandLine.TryParse(args, 2, _revokeOptions, out var line, out var error))
        {
            return Refuse(context, error, CliApp.ExitUsageError);
        }

        if (line.WantsHelp)
        {
            return Usage(context.Stdout, CliApp.ExitSuccess);
        }

        var expired = line.HasFlag("expired");
        if (expired == (line.Operands.Count == 1) || line.Operands.Count > 1)
        {
            return Refuse(context, "give one share id, or --expired", CliApp.ExitUsageError);
        }

        if (!expired && line.Operands[0].Length < 6)
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

            var prefix = line.Operands[0];
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

    /// <summary>The entry a <c>kp://</c> reference names: an env variable's entry, or the named entry.</summary>
    private static EntryName? Resolve(Vault vault, KpReference reference, CliContext context, out int exit)
    {
        exit = CliApp.ExitSuccess;

        var name = reference is EnvReference env
            ? new EntryName(EnvProfileNames.GroupPath(env.Project, env.Profile), env.Key)
            : ((EntryReference)reference).Entry;

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
            exit = Refuse(context, $"{Clean(reference.ToString())} names more than one entry", CliApp.ExitUsageError);
            return null;
        }

        if (found is null)
        {
            exit = Refuse(context, $"no entry at {Clean(reference.ToString())}", CliApp.ExitNotFound);
            return null;
        }

        return EntryName.Of(found);
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
            .Where(entry => !ReservedGroups.IsReserved(entry.GroupPath) && string.Equals(entry.Title, operand, StringComparison.Ordinal))
            .ToList();

        switch (titled.Count)
        {
            case 1:
                return EntryName.Of(titled[0]);

            case 0:
                exit = Refuse(context, $"no entry '{Clean(operand)}'", CliApp.ExitNotFound);
                return null;

            default:
                context.Stderr.WriteLine($"keypaste share: more than one entry is called '{Clean(operand)}'; name one by its path:");
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

        if (first.Length < ShareService.MinimumPassphraseLength || !ShareCrypto.AcceptsPassphrase(first.Value))
        {
            first.Dispose();
            context.Stderr.WriteLine($"keypaste share: a passphrase has at least {ShareService.MinimumPassphraseLength} characters: Latin letters, digits, spaces and punctuation");
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

        return ttl >= ShareService.MinimumTtl && ttl <= ShareService.MaximumTtl;
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
