using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Keypaste.Cli.Output;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Tokens;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste token create|ls|revoke|bundle</c>: scoped tokens that only ever feed <c>keypaste run</c>.
/// </summary>
/// <remarks>
/// <para>
/// A token is printed once, by <c>create</c>, alone on stdout so a redirect captures it; the vault
/// keeps only its verifier. Nothing here prints a value or a verifier.
/// </para>
/// <para>
/// <c>create</c> and <c>revoke</c> save, so they take the vault's claim first and are refused while
/// the app or <c>keypaste agent</c> holds it: a token written under a running owner would be
/// invisible to it and leave its copy changed on disk (D-0317). <c>ls</c> and <c>bundle</c> only read.
/// </para>
/// </remarks>
internal static class TokenCommand
{
    private static readonly TimeSpan _defaultLifetime = TimeSpan.FromDays(30);

    private static readonly OptionSpec[] _createOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("scope", TakesValue: true),
        new("ttl", TakesValue: true),
        new("expires", TakesValue: true),
        new("allow-prod", TakesValue: false),
        new(CliJson.Option, TakesValue: false),
    ];

    private static readonly OptionSpec[] _listOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new(CliJson.Option, TakesValue: false),
    ];

    private static readonly OptionSpec[] _revokeOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
    ];

    private static readonly OptionSpec[] _bundleOptions =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("output", TakesValue: true, 'o'),
        new("force", TakesValue: false),
        new(RunWithToken.TokenOption, TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (args.Length < 2)
        {
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        switch (args[1])
        {
            case "create":
                return Parsed(args, _createOptions, "create", context, line => Create(line, context));

            case "ls":
                return Parsed(args, _listOptions, "ls", context, line => List(line, context));

            case "revoke":
                return Parsed(args, _revokeOptions, "revoke", context, line => Revoke(line, context));

            case "bundle":
                return Parsed(args, _bundleOptions, "bundle", context, line => Bundle(line, context));

            case "help":
            case "--help":
            case "-h":
                WriteUsage(context.Stdout);
                return CliApp.ExitSuccess;

            default:
                context.Stderr.WriteLine($"keypaste token: unknown subcommand '{args[1]}'");
                WriteUsage(context.Stderr);
                return CliApp.ExitUsageError;
        }
    }

    private static int Parsed(string[] args, OptionSpec[] options, string verb, CliContext context, Func<CommandLine, int> body)
    {
        if (!CommandLine.TryParse(args, 2, options, out var line, out var error))
        {
            return Fail(context, verb, error);
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        return body(line);
    }

    private static int Create(CommandLine line, CliContext context)
    {
        if (line.Operands.Count != 1)
        {
            return Fail(context, "create", "expected exactly one token name");
        }

        var name = line.Operands[0];

        if (!TokenStore.IsValidName(name, out var nameError))
        {
            return Fail(context, "create", nameError);
        }

        if (line.Value("scope") is not { } scopeText)
        {
            return Fail(context, "create", "--scope is required, as in --scope 'read:acme-api/staging/*'");
        }

        if (!TokenScope.TryParseList(scopeText, out var scopes, out var scopeError))
        {
            return Fail(context, "create", scopeError);
        }

        if (line.Value("ttl") is not null && line.Value("expires") is not null)
        {
            return Fail(context, "create", "give --ttl or --expires, not both");
        }

        var ttl = _defaultLifetime;

        if ((line.Value("ttl") ?? line.Value("expires")) is { } span && !TryLifetime(span, out ttl))
        {
            return Fail(context, "create", "--ttl takes a number and m, h or d, from 1m to 365d, as in 30d");
        }

        var allowProd = line.HasFlag("allow-prod");

        if (!allowProd && scopes.FirstOrDefault(scope => scope.IsProtected) is { } protectedScope)
        {
            return Fail(context, "create", $"{protectedScope} names a protected profile; --allow-prod lets the token ask you live for it, every time");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, "create", locateError);
        }

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            var store = new TokenStore(vault);

            if (!store.TryCreate(name, scopes, ttl, allowProd, context.Clock.GetUtcNow(), out var info, out var token, out var error))
            {
                context.Stderr.WriteLine($"keypaste token create: {error}");
                return CliApp.ExitInternalError;
            }

            vault.Save();

            if (line.HasFlag(CliJson.Option))
            {
                WriteCreated(context.Stdout, info, token);
                return CliApp.ExitSuccess;
            }

            context.Stdout.WriteLine(token);

            var style = context.ConsoleStyle;
            var dot = style.Glyph(context.Stderr, Mark.Dot);
            context.Stderr.WriteLine(
                $"  {style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done))} {ConsoleMarks.Shortened(context.Stderr, info.Prefix)} {dot} {TokenInfo.Mode} {dot} shown once");
            context.Stderr.WriteLine($"  scope {Scopes(info)} {dot} expires {Day(info.Expires)}");
            return CliApp.ExitSuccess;
        });
    }

    /// <summary>The one <c>--json</c> that is an object, not an array: a script reads <c>.token</c> from it.</summary>
    private static void WriteCreated(TextWriter writer, TokenInfo info, string token)
    {
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("name", info.Name);
            json.WriteString("token", token);
            json.WriteString("prefix", info.Prefix);
            WriteScopes(json, info);
            json.WriteString("expires", Timestamp(info.Expires));
            json.WriteEndObject();
        }

        writer.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static int List(CommandLine line, CliContext context)
    {
        if (line.Operands.Count > 0)
        {
            return Fail(context, "ls", $"unexpected argument '{line.Operands[0]}'");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, "ls", locateError);
        }

        return VaultSession.Open(path, line, context, vault =>
        {
            var tokens = new TokenStore(vault).List();
            var now = context.Clock.GetUtcNow();

            if (line.HasFlag(CliJson.Option))
            {
                CliJson.WriteArray(context.Stdout, tokens, (json, info) =>
                {
                    json.WriteString("name", info.Name);
                    json.WriteString("prefix", info.Prefix);
                    WriteScopes(json, info);
                    json.WriteString("mode", TokenInfo.Mode);
                    json.WriteString("created", Timestamp(info.Created));
                    json.WriteString("expires", Timestamp(info.Expires));
                    json.WriteBoolean("allow_prod", info.AllowProd);
                    json.WriteBoolean("expired", info.IsExpired(now));
                });

                return CliApp.ExitSuccess;
            }

            if (tokens.Count == 0)
            {
                context.Stderr.WriteLine("keypaste token ls: this vault holds no tokens; `keypaste token create` makes one");
                return CliApp.ExitSuccess;
            }

            string[][] rows =
            [
                ["NAME", "TOKEN", "SCOPE", "MODE", "EXPIRES"],
                .. tokens.Select(info => new[] { EntryNameSanitizer.Sanitize(info.Name).Text, ConsoleMarks.Shortened(context.Stdout, info.Prefix), Scopes(info), TokenInfo.Mode, string.Empty }),
            ];

            var widths = Enumerable.Range(0, 4).Select(column => rows.Max(row => row[column].Length)).ToArray();

            for (var i = 0; i < rows.Length; i++)
            {
                var cells = rows[i];
                var expires = i == 0
                    ? context.ConsoleStyle.Paint(context.Stdout, Tone.Muted, "EXPIRES")
                    : Remaining(tokens[i - 1], now, context);
                var text = string.Join("  ", cells.Take(4).Select((cell, column) => cell.PadRight(widths[column])));

                context.Stdout.WriteLine(i == 0
                    ? $"  {context.ConsoleStyle.Paint(context.Stdout, Tone.Muted, text)}  {expires}"
                    : $"  {text}  {expires}");
            }

            return CliApp.ExitSuccess;
        });
    }

    private static int Revoke(CommandLine line, CliContext context)
    {
        if (line.Operands.Count != 1)
        {
            return Fail(context, "revoke", "expected exactly one token name or id");
        }

        var target = line.Operands[0];

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, "revoke", locateError);
        }

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            var shown = EntryNameSanitizer.Sanitize(target, 64).Text;

            if (!new TokenStore(vault).Revoke(target))
            {
                context.Stderr.WriteLine($"keypaste token revoke: no token named '{shown}'");
                return CliApp.ExitNotFound;
            }

            vault.Save();

            var style = context.ConsoleStyle;
            context.Stderr.WriteLine($"  {style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done))} revoked {shown}");
            return CliApp.ExitSuccess;
        });
    }

    /// <summary>Seals every set the named token's scope covers into a file a machine with no vault can open.</summary>
    /// <remarks>
    /// Whole or nothing, and never a protected profile: a bundle opens without asking anybody and
    /// cannot be revoked once written (D-0348). The file is written beside its destination, the
    /// audit line is appended, and only then is it moved into place, so an unwritable log leaves no
    /// bundle behind.
    /// </remarks>
    private static int Bundle(CommandLine line, CliContext context)
    {
        if (line.Operands.Count != 1)
        {
            return Fail(context, "bundle", "expected exactly one token name");
        }

        var name = line.Operands[0];

        if (line.Value("output") is not { Length: > 0 } output)
        {
            return Fail(context, "bundle", "-o <file> is required");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, "bundle", locateError);
        }

        if (!RunWithToken.TryReadToken(line.Value(RunWithToken.TokenOption) ?? "env", "keypaste token bundle", context, out var token, out _, out var tokenExit))
        {
            return tokenExit;
        }

        var destination = Path.GetFullPath(output);

        if (!VaultOverwriteGuard.TryRefuse("keypaste token bundle", path, destination, "a bundle", context, out var refused))
        {
            return refused;
        }

        if (File.Exists(destination) && !line.HasFlag("force"))
        {
            context.Stderr.WriteLine($"keypaste token bundle: '{output}' already exists; --force replaces it");
            return CliApp.ExitInternalError;
        }

        return VaultSession.Open(path, line, context, vault =>
        {
            var now = context.Clock.GetUtcNow();

            switch (new TokenStore(vault).Verify(token, now, out var info))
            {
                case TokenCheck.Valid when string.Equals(info!.Name, name, StringComparison.Ordinal):
                    break;

                case TokenCheck.Valid:
                    context.Stderr.WriteLine($"keypaste token bundle: that token is not '{EntryNameSanitizer.Sanitize(name, 64).Text}'");
                    return CliApp.ExitInternalError;

                case TokenCheck.Expired:
                    context.Stderr.WriteLine("keypaste token bundle: the token has expired");
                    return CliApp.ExitInternalError;

                default:
                    context.Stderr.WriteLine("keypaste token bundle: the token is not valid for this vault");
                    return CliApp.ExitInternalError;
            }

            if (info.Pairs.Where(pair => EnvProfileNames.IsProtected(pair.Profile)).ToList() is [var (guardedProject, guardedProfile), ..])
            {
                context.Stderr.WriteLine(
                    $"keypaste token bundle: {guardedProject}/{guardedProfile} is protected; a bundle opens without asking anybody, so it cannot carry it");
                return CliApp.ExitInternalError;
            }

            List<BundledSet> sets = [];

            foreach (var (project, profile) in info.Pairs)
            {
                var resolved = EnvResolution.Resolve(vault, project, profile, info.KeysFor(project, profile), context.Clock);

                if (resolved.Outcome != EnvOutcome.Resolved)
                {
                    context.Stderr.WriteLine($"keypaste token bundle: {EntryNameSanitizer.SanitizeProse(resolved.Refusal, 1024).Text}; nothing was written");
                    return resolved.Outcome is EnvOutcome.NoProject or EnvOutcome.NoProfile ? CliApp.ExitNotFound : CliApp.ExitInternalError;
                }

                sets.Add(new BundledSet(project, profile, resolved.Variables));
            }

            return Write(path, destination, output, token, info, sets, now, context);
        });
    }

    private static int Write(
        string vaultPath,
        string destination,
        string output,
        string token,
        TokenInfo info,
        IReadOnlyList<BundledSet> sets,
        DateTimeOffset now,
        CliContext context)
    {
        if (!TokenSecret.TryParse(token, out _, out var secret))
        {
            return CliApp.ExitInternalError;
        }

        byte[] bundle;

        try
        {
            bundle = TokenBundle.Seal(info, secret, sets, now);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        if (bundle.Length > TokenBundle.MaximumBytes)
        {
            context.Stderr.WriteLine("keypaste token bundle: the sets are too large for one bundle; nothing was written");
            return CliApp.ExitInternalError;
        }

        // Checked again after the vault was open: a vault may have been put at the destination meanwhile.
        if (!VaultOverwriteGuard.TryRefuse("keypaste token bundle", vaultPath, destination, "a bundle", context, out var refused))
        {
            return refused;
        }

        var count = sets.Sum(set => set.Variables.Count);
        var auditPath = KeypasteHome.AuditPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));

        if (!AuditLog.TryOpen(auditPath, context.Clock, out var audit, out var auditError))
        {
            context.Stderr.WriteLine($"keypaste token bundle: {auditError}; nothing was written");
            return CliApp.ExitInternalError;
        }

        var temporary = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}.tmp");

        using (audit)
        {
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                using (var stream = new FileStream(temporary, options))
                {
                    stream.Write(bundle);
                }

                var record = new AuditRecord
                {
                    Tool = "token bundle",
                    Client = new AuditClient("keypaste token bundle", CoreInfo.Version, null),
                    Args = new AuditArgs
                    {
                        Entry = EntryNameSanitizer.SanitizePath(
                            string.Join(",", info.Pairs.Select(pair => $"{EnvConvention.RootGroup}/{pair.Project}/{pair.Profile}")),
                            maximumLength: AuditArgs.EntryLength).Text,
                    },
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Token,
                    Reason = $"token {info.Id} '{EntryNameSanitizer.Sanitize(info.Name).Text}': bundled {count} variable(s)",
                };

                if (!audit.TryAppend(record, out var appendError))
                {
                    File.Delete(temporary);
                    context.Stderr.WriteLine($"keypaste token bundle: {appendError}; nothing was written");
                    return CliApp.ExitInternalError;
                }

                File.Move(temporary, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                File.Delete(temporary);
                context.Stderr.WriteLine($"keypaste token bundle: the bundle could not be written: {ex.Message}");
                return CliApp.ExitInternalError;
            }
        }

        var style = context.ConsoleStyle;
        var dot = style.Glyph(context.Stderr, Mark.Dot);
        context.Stderr.WriteLine(
            $"  {style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done))} wrote {Path.GetFileName(output)} {dot} {count} values {dot} " +
            $"encrypted to {ConsoleMarks.Shortened(context.Stderr, info.Prefix)} {dot} expires {Day(info.Expires)}");
        return CliApp.ExitSuccess;
    }

    /// <summary>A lifetime written as a whole number and m, h or d.</summary>
    internal static bool TryLifetime(string text, out TimeSpan lifetime)
    {
        lifetime = default;

        if (text.Length < 2
            || !int.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        var unit = text[^1] switch
        {
            'm' => TimeSpan.FromMinutes(1),
            'h' => TimeSpan.FromHours(1),
            'd' => TimeSpan.FromDays(1),
            _ => TimeSpan.Zero,
        };

        if (unit == TimeSpan.Zero || amount > TokenStore.MaximumLifetime / unit)
        {
            return false;
        }

        lifetime = unit * amount;
        return lifetime >= TokenStore.MinimumLifetime;
    }

    private static string Remaining(TokenInfo info, DateTimeOffset now, CliContext context)
    {
        if (info.IsExpired(now))
        {
            return context.ConsoleStyle.Paint(context.Stdout, Tone.Danger, "expired");
        }

        var left = info.Expires - now;

        return left.TotalDays >= 1 ? Plural((int)left.TotalDays, "day")
            : left.TotalHours >= 1 ? Plural((int)left.TotalHours, "hour")
            : Plural(Math.Max(1, (int)left.TotalMinutes), "minute");

        static string Plural(int n, string unit) => $"in {n} {unit}{(n == 1 ? string.Empty : "s")}";
    }

    private static string Scopes(TokenInfo info) => string.Join(", ", info.Scopes);

    private static void WriteScopes(Utf8JsonWriter json, TokenInfo info)
    {
        json.WriteStartArray("scopes");
        foreach (var scope in info.Scopes)
        {
            json.WriteStringValue(scope.ToString());
        }

        json.WriteEndArray();
    }

    private static string Timestamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Day(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int Fail(CliContext context, string verb, string message)
    {
        context.Stderr.WriteLine($"keypaste token {verb}: {message}");
        return CliApp.ExitUsageError;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste token <command>");
        writer.WriteLine();
        writer.WriteLine("commands:");
        writer.WriteLine("  create <name> --scope <scopes> [--ttl <n>m|h|d] [--allow-prod] [--json]");
        writer.WriteLine("                             mint a token; it is printed once and never stored");
        writer.WriteLine("  ls [--json]                list tokens: name, prefix, scope and expiry, never a secret");
        writer.WriteLine("  revoke <name|id>           delete a token for good");
        writer.WriteLine("  bundle <name> -o <file> [--force] [--token <token|-|env>]");
        writer.WriteLine("                             seal the token's sets into a file for CI");
        writer.WriteLine();
        writer.WriteLine("a scope is read:<project>/<profile>/<KEY or *>; separate several with commas.");
        writer.WriteLine("a token only feeds `keypaste run --token` or `--bundle`: it injects a set into a");
        writer.WriteLine($"command and never prints a value. --ttl defaults to 30d. pass the token in {RunWithToken.EnvironmentVariable}.");
        writer.WriteLine();
        writer.WriteLine("create and revoke change the vault, so lock it in the app or `keypaste agent` first.");
        writer.WriteLine("a protected profile (prod) needs --allow-prod, then asks you live every time,");
        writer.WriteLine("and is never put in a bundle.");
    }
}
