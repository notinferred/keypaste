using Keypaste.Core;

namespace Keypaste.VaultRestorer;

/// <summary>Performs one recovery in one vault, once, and saves.</summary>
/// <remarks>
/// <para>
/// The recovery gates' driver, and the reason it is a process. There is no shipped binary a script
/// can tell to restore a revision — and a gate that cannot make KeePassXC look at a restored vault
/// is not checking the half of docs/PRODUCT.md law 4.6 that matters here. Everything the shipped
/// writer can do in those gates, it does; this performs only the steps it cannot (DECISIONS.md
/// D-0228).
/// </para>
/// <para>
/// V.2b put a revision restore on the desktop entry pane, which a bash gate cannot drive, so this
/// stays until a restore verb reaches the CLI (D-0230). V.3b put the trash on the desktop the same
/// way: the shipped <c>keypaste rm</c> is what recycles an entry in the recycle-bin gate, and the
/// listing, the restore and the purge — which the app performs and no command line does — come
/// through here (D-0254).
/// </para>
/// <para>
/// V.4b put restoring a whole-vault backup on the desktop unlock screen and the encrypted copy in
/// its settings. Reading a backup needs no driver, because a backup is an ordinary vault the shipped
/// binary opens; putting one back and exporting are acts no command line performs, so they come
/// through here as well, over the same public calls the app makes.
/// </para>
/// <para>
/// V.5a added renaming and moving. Those are core operations with no front end at all yet — V.5b is
/// what puts them in front of a person — so every one of them comes through here, and a refusal
/// prints the outcome's own name rather than a sentence, so the gate can assert which refusal it
/// got. The driver goes when a shipped command-line surface performs them (D-0254).
/// </para>
/// <para>
/// It prints how many revisions, recycled entries or backups it saw and what it did, and never a value: the
/// gate seeded the values itself and asks KeePassXC for them, so nothing is learned by putting a
/// secret on this process's stdout.
/// </para>
/// <para>
/// The master password arrives in the environment rather than in <c>argv</c>, as it does for
/// <c>Keypaste.VaultSaver</c>. It is a fixture value and not a real secret, but a command line is
/// readable by every process on the machine and this repository does not write code that reads as
/// though that were fine.
/// </para>
/// </remarks>
internal static class Program
{
    private const string _usage =
        "usage: <vault-path> <index>            restore a revision, newest-first\n" +
        "       trash-ls <vault-path>           list what can be recovered\n" +
        "       trash-restore <vault-path> <id> put one back\n" +
        "       trash-purge <vault-path> <id>   remove one for good\n" +
        "       trash-empty <vault-path>        remove all of them for good\n" +
        "       backup-ls <vault-path>          list the vault's backups, newest first\n" +
        "       backup-restore <vault-path> <backup-file-name>\n" +
        "       vault-export <vault-path> <destination>\n" +
        "       group-create <vault-path> <parent-group-path> <name>\n" +
        "       group-rename <vault-path> <group-path> <new-name>\n" +
        "       entry-rename <vault-path> <group-path> <title> <new-title>\n" +
        "       entry-move   <vault-path> <group-path> <title> <destination-group-path>\n" +
        "the master password is read from KEYPASTE_RESTORER_PASSWORD";

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(_usage);
            return 2;
        }

        var password = Environment.GetEnvironmentVariable("KEYPASTE_RESTORER_PASSWORD");

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("KEYPASTE_RESTORER_PASSWORD is unset");
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "backup-ls" or "backup-restore" => Backup(args, password),
                "vault-export" => Export(args, password),
                _ when args[0].StartsWith("trash-", StringComparison.Ordinal) => Trash(args, password),
                _ when args[0].StartsWith("group-", StringComparison.Ordinal) => Groups(args, password),
                _ when args[0].StartsWith("entry-", StringComparison.Ordinal) => Entries(args, password),
                _ => RestoreRevision(args, password),
            };
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine($"restore refused: {ex.Message}");
            return 1;
        }
        catch (VaultException ex)
        {
            Console.Error.WriteLine($"restore refused: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"restorer failed: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    /// <summary>Creates or renames a group, and says which refusal it got when it did not.</summary>
    /// <remarks>
    /// The outcome's own name is printed on a refusal rather than a sentence, so the gate can
    /// assert <em>which</em> refusal happened. A gate that only checked that something failed would
    /// pass with every refusal collapsed into one.
    /// </remarks>
    private static int Groups(string[] args, string password)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine(_usage);
            return 2;
        }

        using var vault = Vault.Open(args[1], password);

        GroupOutcome outcome;
        string path;

        switch (args[0])
        {
            case "group-create":
                outcome = vault.CreateGroup(args[2], args[3], out path);
                break;

            case "group-rename":
                outcome = vault.RenameGroup(args[2], args[3], out path);
                break;

            default:
                Console.Error.WriteLine(_usage);
                return 2;
        }

        if (outcome is not (GroupOutcome.Created or GroupOutcome.Renamed))
        {
            Console.Error.WriteLine($"refused: {outcome}");
            return 1;
        }

        vault.Save();
        Console.WriteLine($"{(outcome is GroupOutcome.Created ? "created" : "renamed")}       {path}");
        return 0;
    }

    /// <summary>Renames or moves one entry, addressed by its group path and title as two values.</summary>
    /// <remarks>
    /// Never by the two joined: joining is the lossy step <see cref="EntryName"/> exists to avoid,
    /// and the gate has to be able to hand this a title that contains a separator.
    /// </remarks>
    private static int Entries(string[] args, string password)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine(_usage);
            return 2;
        }

        using var vault = Vault.Open(args[1], password);
        var name = new EntryName(args[2], args[3]);

        OrganizeOutcome outcome;
        EntryName? result;

        switch (args[0])
        {
            case "entry-rename":
                outcome = vault.RenameEntry(name, args[4], out result);
                break;

            case "entry-move":
                outcome = vault.MoveEntry(name, args[4], out result);
                break;

            default:
                Console.Error.WriteLine(_usage);
                return 2;
        }

        if (outcome is not (OrganizeOutcome.Renamed or OrganizeOutcome.Moved))
        {
            Console.Error.WriteLine($"refused: {outcome}");
            return 1;
        }

        vault.Save();
        Console.WriteLine($"{(outcome is OrganizeOutcome.Renamed ? "renamed" : "moved")}       {result!.GroupPath}/{result.Title}");
        return 0;
    }

    private static int RestoreRevision(string[] args, string password)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var index))
        {
            Console.Error.WriteLine(_usage);
            return 2;
        }

        var name = TargetEntry(out var entryPath);

        if (name is null)
        {
            Console.Error.WriteLine($"'{entryPath}' does not name an entry as <group>/<title>");
            return 2;
        }

        using var vault = Vault.Open(args[0], password);
        var revisions = vault.ReadHistory(name);

        if (revisions is null)
        {
            Console.Error.WriteLine($"no entry is called '{entryPath}'");
            return 1;
        }

        Console.WriteLine($"revisions     {revisions.Count}");

        foreach (var revision in revisions)
        {
            Console.WriteLine($"  [{revision.Index}]         {revision.ModifiedUtc:O}");
        }

        if (!vault.RestoreRevision(name, index))
        {
            Console.Error.WriteLine($"no entry is called '{entryPath}'");
            return 1;
        }

        vault.Save();
        Console.WriteLine($"restored      [{index}]");
        return 0;
    }

    private static int Backup(string[] args, string password)
    {
        if (args.Length != (args[0] == "backup-restore" ? 3 : 2))
        {
            Console.Error.WriteLine(_usage);
            return 2;
        }

        var backups = VaultBackups.List(args[1]);

        if (args[0] == "backup-ls")
        {
            Console.WriteLine($"backups       {backups.Count}");

            foreach (var listed in backups)
            {
                Console.WriteLine($"  {Path.GetFileName(listed.Path)}  {listed.TakenAt:O}");
            }

            return 0;
        }

        var backup = backups.FirstOrDefault(candidate => Path.GetFileName(candidate.Path) == args[2]);

        if (backup is null)
        {
            Console.Error.WriteLine($"restore refused: '{args[2]}' is not a name from backup-ls");
            return 1;
        }

        var validated = VaultBackups.Inspect(args[1], backup, password);

        Console.WriteLine($"taken         {validated.Backup.TakenAt:O}");
        Console.WriteLine($"entries       {validated.Entries}");
        Console.WriteLine($"groups        {validated.Groups}");
        Console.WriteLine($"projects      {validated.EnvProjects}");
        Console.WriteLine($"replaces      {(validated.Replaces is { } facts ? $"{facts.Length} bytes" : "nothing")}");

        var report = VaultBackups.Restore(validated);

        Console.WriteLine($"restored      {Path.GetFileName(report.Restored.Path)}");

        if (report.Preserved is { } preserved)
        {
            Console.WriteLine($"preserved     {Path.GetFileName(preserved.Path)}");
        }
        else if (report.AlreadyKeptAs is { } kept)
        {
            Console.WriteLine($"already-kept  {Path.GetFileName(kept.Path)}");
        }

        return 0;
    }

    private static int Export(string[] args, string password)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine(_usage);
            return 2;
        }

        using var vault = Vault.Open(args[1], password);
        vault.ExportTo(args[2]);

        Console.WriteLine($"exported      {args[2]}");
        return 0;
    }

    private static int Trash(string[] args, string password)
    {
        var verb = args[0];
        var takesId = verb is "trash-restore" or "trash-purge";

        if (args.Length != (takesId ? 3 : 2))
        {
            Console.Error.WriteLine(_usage);
            return 2;
        }

        RecycledEntryId id = default;

        if (takesId && !RecycledEntryId.TryParse(args[2], out id))
        {
            Console.Error.WriteLine($"'{args[2]}' is not an identity from trash-ls");
            return 2;
        }

        using var vault = Vault.Open(args[1], password);

        switch (verb)
        {
            case "trash-ls":
                return List(vault);

            case "trash-restore":
                return Restore(vault, id);

            case "trash-purge":
                if (!vault.PurgeRecycled(id))
                {
                    Console.Error.WriteLine("purge refused: nothing in the bin has that identity");
                    return 1;
                }

                vault.Save();
                Console.WriteLine($"purged        {id}");
                return 0;

            case "trash-empty":
                var removed = vault.EmptyRecycleBin();
                vault.Save();
                Console.WriteLine($"emptied       {removed}");
                return 0;

            default:
                Console.Error.WriteLine(_usage);
                return 2;
        }
    }

    /// <summary>
    /// One line per recoverable entry: identity, where it came from, and what it is called. No
    /// field values, because <see cref="RecycledEntry"/> has none to print.
    /// </summary>
    private static int List(Vault vault)
    {
        var rows = vault.ReadRecycled();
        Console.WriteLine($"recycled      {rows.Count}");

        foreach (var row in rows)
        {
            Console.WriteLine($"  {row.Id}  {row.OriginalGroupPath ?? "-"}  {row.Title}");
        }

        return 0;
    }

    private static int Restore(Vault vault, RecycledEntryId id)
    {
        switch (vault.RestoreRecycled(id))
        {
            case RestoreOutcome.Restored:
                vault.Save();
                Console.WriteLine($"restored      {id}");
                return 0;

            // Said in two words the gate can match on, because "back where it was" and "back, but
            // the group it came from is gone" are different outcomes for whoever deleted it.
            case RestoreOutcome.RestoredToRoot:
                vault.Save();
                Console.WriteLine($"restored root {id}");
                return 0;

            case RestoreOutcome.DestinationOccupied:
                Console.Error.WriteLine("restore refused: something else answers to that name now");
                return 1;

            default:
                Console.Error.WriteLine("restore refused: nothing in the bin has that identity");
                return 1;
        }
    }

    /// <summary>
    /// The entry to restore a revision of, taken from <c>KEYPASTE_RESTORER_ENTRY</c> as a group
    /// path and a title.
    /// </summary>
    /// <remarks>
    /// Split on the last separator, which is exactly the lossy join <see cref="Core.EntryName"/>
    /// exists to avoid — acceptable in a fixture driver whose entry is one the gate just created,
    /// and not something to copy into shipped code.
    /// </remarks>
    private static EntryName? TargetEntry(out string entryPath)
    {
        entryPath = Environment.GetEnvironmentVariable("KEYPASTE_RESTORER_ENTRY") ?? string.Empty;
        var separator = entryPath.LastIndexOf('/');

        return separator <= 0 || separator == entryPath.Length - 1
            ? null
            : new EntryName(entryPath[..separator], entryPath[(separator + 1)..]);
    }
}
