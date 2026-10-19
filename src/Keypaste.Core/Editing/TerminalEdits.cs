using Keypaste.Core.Approval;

namespace Keypaste.Core.Editing;

/// <summary>
/// Checks and makes a terminal's edit on an open vault: the one place the CLI's saving verbs and the
/// vault's holder decide what an edit does, so the two cannot disagree (D-0464).
/// </summary>
/// <remarks>
/// Nothing here saves: the caller saves, or the holder saves under its claim. A refusal carries
/// keypaste's words without the verb, and a preview names items, fields and variables, never a value.
/// </remarks>
public static class TerminalEdits
{
    private const int _proseLength = 1024;

    /// <summary>The edit with every recipe it carries generated from, so it can be checked and made.</summary>
    /// <param name="edit">The edit.</param>
    /// <returns>The same edit, its secrets now values.</returns>
    /// <exception cref="ArgumentException">A recipe is invalid.</exception>
    public static TerminalEdit Generate(TerminalEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return edit switch
        {
            AddItem { Password: { } secret } add => add with { Password = secret.Generate() },
            SetPassword { Password: { } secret } set => set with { Password = secret.Generate() },
            EnvSetKey { Value: { } secret } env => env with { Value = secret.Generate() },
            _ => edit,
        };
    }

    /// <summary>Decides what <paramref name="edit"/> would do to <paramref name="vault"/>, changing nothing.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="edit">The edit, validated; its values may still be missing, except an <c>env set</c>'s, which its plan needs.</param>
    /// <param name="scene">This computer's settings and the time.</param>
    /// <returns>A refusal, a preview, or what the vault already holds.</returns>
    /// <exception cref="VaultException">More than one item answers to a path the edit names.</exception>
    public static EditCheck Check(Vault vault, TerminalEdit edit, EditScene scene)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(scene);

        return edit switch
        {
            AddItem add => CheckAdd(vault, add),
            SetPassword set => CheckSet(vault, set),
            SetFields fields => CheckFields(vault, fields),
            RemoveItem remove => CheckRemove(vault, remove),
            RotatePassword rotate => CheckRotate(vault, rotate),
            RemoveField field => CheckRemoveField(vault, field),
            EnvSetKey env => CheckEnvSet(vault, env),
            EnvRemoveKey env => CheckEnvRemove(vault, env),
            EnvPullFile pull => CheckPull(vault, pull),
            TagItem tag => CheckTag(vault, tag, scene),
            _ => throw new ArgumentException($"{edit} is not an edit keypaste makes", nameof(edit)),
        };
    }

    /// <summary>Makes <paramref name="edit"/> in <paramref name="vault"/>, checking it again first. Does not save.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="edit">The edit, with every value present.</param>
    /// <param name="scene">This computer's settings and the time.</param>
    /// <param name="shown">The preview a person allowed, or null when nobody was shown one; a different change now is refused.</param>
    /// <returns>What was changed, or why nothing was.</returns>
    /// <exception cref="VaultException">The vault refused the write, or more than one item answers to a path.</exception>
    public static EditResult Apply(Vault vault, TerminalEdit edit, EditScene scene, EditPreview? shown)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(edit);

        if (!edit.IsComplete)
        {
            throw new ArgumentException($"{edit} is missing a value", nameof(edit));
        }

        var check = Check(vault, edit, scene);

        if (check.Refusal is { } refusal)
        {
            return new EditResult { Refusal = refusal };
        }

        if (check.Unchanged is { } unchanged)
        {
            return shown is null
                ? new EditResult { Done = unchanged }
                : Refuse(EditRefusalKind.Conflict, "the vault changed while you were asked; nothing was written");
        }

        var preview = check.Preview!;

        if (shown is not null && !shown.Shows(preview))
        {
            return Refuse(EditRefusalKind.Conflict, "the vault changed while you were asked; nothing was written");
        }

        return edit switch
        {
            AddItem add => ApplyAdd(vault, add, preview),
            SetPassword set => ApplySet(vault, set, preview),
            SetFields fields => ApplyFields(vault, fields),
            RemoveItem remove => ApplyRemove(vault, remove),
            RotatePassword rotate => ApplyRotate(vault, rotate),
            RemoveField field => ApplyRemoveField(vault, field),
            EnvSetKey env => ApplyEnvSet(vault, env, preview),
            EnvRemoveKey env => ApplyEnvRemove(vault, env, preview),
            EnvPullFile pull => ApplyPull(vault, pull, preview),
            TagItem tag => ApplyTag(vault, tag, preview),
            _ => throw new ArgumentException($"{edit} is not an edit keypaste makes", nameof(edit)),
        };
    }

    private static EditCheck CheckAdd(Vault vault, AddItem add)
    {
        var path = TerminalEdit.PathOf(add.Name);

        // An entry is its group and its title. Asking the joined form whether one exists said
        // "already exists" about identities nothing had — a title of `b/c` in `a` refused a
        // genuinely different `c` in `a/b` (docs/STEPS.md F.1e).
        if (vault.Find(add.Name) is not null)
        {
            return Refused(EditRefusalKind.Invalid, $"'{path}' already exists");
        }

        // Throws when the path already names two: a third would deepen a collision `get` already
        // refuses, and nothing keypaste writes gets to make that worse.
        var sharesPath = vault.Find(path) is not null;

        List<string> given = [];
        if (add.Username.Length > 0)
        {
            given.Add("username");
        }

        if (add.Url.Length > 0)
        {
            given.Add("URL");
        }

        if (add.Notes.Length > 0)
        {
            given.Add("notes");
        }

        var alsoSent = given.Count == 0 ? string.Empty : $", and the {Joined(given)} the terminal sent";
        var password = add.Password?.Describe("password") ?? "a password";

        List<string> changes = [$"Creates {Shown(path)} with {password}{alsoSent}."];
        if (sharesPath)
        {
            changes.Add($"{Shown(path)} then names two items, and keypaste get refuses that path until one is renamed.");
        }

        return Ready(new EditPreview(EditAction.Add, Shown(path), changes) { SharesPath = sharesPath });
    }

    private static EditCheck CheckSet(Vault vault, SetPassword set)
    {
        var path = TerminalEdit.PathOf(set.Name);
        var existing = vault.Find(set.Name);

        if (existing is null && !Vault.IsCreatable(set.Name, out var refusal))
        {
            return Refused(EditRefusalKind.Invalid, refusal);
        }

        var password = set.Password?.Describe("password") ?? "a password";

        return Ready(new EditPreview(
            EditAction.Set,
            Shown(path),
            [existing is null
                ? $"Creates {Shown(path)} with {password}."
                : $"Replaces the password of {Shown(path)} with {password}; the old one stays in its history."])
        {
            Creates = existing is null,
        });
    }

    private static EditCheck CheckFields(Vault vault, SetFields set)
    {
        var path = TerminalEdit.PathOf(set.Name);

        if (vault.Fields(set.Name) is not { } current)
        {
            return Refused(EditRefusalKind.NotFound, $"no item '{Shown(path)}'; --field writes to an item that already exists");
        }

        if (set.Plain && set.Fields.FirstOrDefault(field => current.Any(existing => string.Equals(existing.Name, field.Name, StringComparison.Ordinal))) is { } kept)
        {
            return new EditCheck
            {
                Refusal = new EditRefusal(
                    EditRefusalKind.Invalid,
                    $"'{Shown(kept.Name)}' already exists and keeps its protection; --plain applies only to a new field")
                {
                    Then = ["Nothing was written."],
                },
            };
        }

        var names = Joined(set.Fields.Select(field => Shown(field.Name)).ToList());
        List<string> changes = [$"Sets {names} on {Shown(path)} to values the terminal sent; the item's earlier state stays in its history."];

        if (set.Plain)
        {
            changes.Add("A new field is written plain, not protected.");
        }

        return Ready(new EditPreview(EditAction.SetFields, Shown(path), changes));
    }

    private static EditCheck CheckRemove(Vault vault, RemoveItem remove)
    {
        if (vault.Find(remove.Item) is null)
        {
            var isGroup = vault.ReadGroupPaths(includeReserved: true).Contains(remove.Item, StringComparer.Ordinal);

            return Refused(
                EditRefusalKind.NotFound,
                isGroup ? $"'{remove.Item}' is a group; only items can be removed" : $"no item '{remove.Item}'");
        }

        var recycles = vault.RecyclesDeletedEntries;

        return Ready(new EditPreview(
            EditAction.Remove,
            Shown(remove.Item),
            [recycles
                ? $"Moves {Shown(remove.Item)} to the recycle bin, where it can be restored."
                : $"Removes {Shown(remove.Item)} for good: this vault has no recycle bin."])
        {
            Recycles = recycles,
        });
    }

    private static EditCheck CheckRotate(Vault vault, RotatePassword rotate)
    {
        var path = TerminalEdit.PathOf(rotate.Name);

        if (vault.Find(rotate.Name) is null)
        {
            return Refused(EditRefusalKind.NotFound, $"no item named {Shown(path)}");
        }

        return Ready(new EditPreview(
            EditAction.Rotate,
            Shown(path),
            [$"Replaces the password of {Shown(path)} with a generated {rotate.Recipe.Describe("password")}; the old one stays in its history."]));
    }

    private static EditCheck CheckRemoveField(Vault vault, RemoveField remove)
    {
        if (vault.Find(remove.Item) is not { } entry)
        {
            return Refused(EditRefusalKind.NotFound, $"no item '{remove.Item}'");
        }

        var fields = vault.Fields(EntryName.Of(entry)) ?? [];

        if (!fields.Any(field => string.Equals(field.Name, remove.Field, StringComparison.Ordinal)))
        {
            return Refused(EditRefusalKind.NotFound, $"'{Shown(entry.Path)}' has no field '{remove.Field}'");
        }

        return Ready(new EditPreview(
            EditAction.RemoveField,
            Shown(entry.Path),
            [$"Removes the field {Shown(remove.Field)} from {Shown(entry.Path)}; its value stays in history."]));
    }

    private static EditCheck CheckEnvSet(Vault vault, EnvSetKey set)
    {
        if (!TryEntry(vault, set.Entry, out var entry, out var missing))
        {
            return Refused(EditRefusalKind.NotFound, missing);
        }

        var where = $"{Shown(set.Project)}/{set.Environment}";

        if (set.Value?.Text is not { } value)
        {
            return Ready(new EditPreview(EditAction.EnvSet, where, []));
        }

        var plan = new EnvStore(vault).Plan(set.Project, set.Environment, [new(set.Key, value)], entry);

        if (plan.Refusal is { } refusal)
        {
            return Refused(EditRefusalKind.Invalid, Prose(refusal));
        }

        var written = plan.Keys[0];

        if (written.Change == EnvWriteChange.Unchanged)
        {
            return new EditCheck
            {
                Unchanged = new EditDone(EditAction.EnvSet, EditOutcome.Unchanged, TerminalEdit.PathOf(written.Entry))
                {
                    Names = [set.Key],
                    Keys = plan.Keys,
                    Entry = written.Entry,
                },
            };
        }

        var on = ApprovalPrompt.Shown(written.Entry);
        var described = set.Value.Describe("value");
        List<string> changes = [written.Change == EnvWriteChange.New
            ? $"Adds {Name(set.Key)} to {where} on {on} with {described}."
            : $"Replaces {Name(set.Key)} on {on} with {described}; the old value stays in the item's history."];

        if (plan.CreatesHome)
        {
            changes.Add($"Creates {on} and tags it {plan.Tag}.");
        }

        return Ready(new EditPreview(EditAction.EnvSet, on, changes) { Entry = written.Entry, Plan = plan });
    }

    private static EditCheck CheckEnvRemove(Vault vault, EnvRemoveKey remove)
    {
        if (!TryEntry(vault, remove.Entry, out var entry, out var missing))
        {
            return Refused(EditRefusalKind.NotFound, missing);
        }

        var listing = EnvResolution.List(vault, remove.Project, remove.Environment);

        if (listing.Outcome != EnvOutcome.Resolved)
        {
            return Refused(EditRefusalKind.NotFound, listing.Outcome == EnvOutcome.NoProject
                ? $"no env set for '{remove.Project}'"
                : $"'{remove.Project}' has no '{remove.Environment}' environment");
        }

        var holding = listing.Sources
            .Where(source => string.Equals(source.Key, remove.Key, StringComparison.Ordinal) && (entry is null || source.Entry == entry))
            .ToList();

        if (holding.Count == 0)
        {
            return Refused(EditRefusalKind.NotFound, string.Equals(remove.Environment, EnvProfileNames.Default, StringComparison.Ordinal)
                ? $"'{remove.Project}' has no variable '{remove.Key}'"
                : $"'{remove.Project}' has no variable '{remove.Key}' in its '{remove.Environment}' environment");
        }

        if (holding.Count > 1)
        {
            var entries = string.Join(", ", holding.Select(source => ApprovalPrompt.Shown(source.Entry)).Order(StringComparer.Ordinal));
            return Refused(EditRefusalKind.Conflict, $"{remove.Key} is on more than one item ({entries}); name the one to remove from with --entry");
        }

        var on = ApprovalPrompt.Shown(holding[0].Entry);

        return Ready(new EditPreview(
            EditAction.EnvRemove,
            on,
            [$"Removes {Name(remove.Key)} from {on} in {Shown(remove.Project)}/{remove.Environment}; its value stays in the item's history."])
        {
            Entry = holding[0].Entry,
        });
    }

    private static EditCheck CheckPull(Vault vault, EnvPullFile pull)
    {
        if (!DotEnv.TryParse(pull.Text, out var document))
        {
            return Refused(EditRefusalKind.Invalid, $"the file has {Count(document.Problems.Count, "problem")}; nothing was imported");
        }

        if (!TryEntry(vault, pull.Entry, out var entry, out var missing))
        {
            return Refused(EditRefusalKind.NotFound, missing);
        }

        var plan = EnvImport.Plan(new EnvStore(vault), pull.Project, pull.Environment, document, entry);

        if (plan.Refusal is { } refusal)
        {
            return Refused(EditRefusalKind.Invalid, Prose(refusal));
        }

        var where = $"{pull.Project}/{pull.Environment}";

        if (plan.Created.Count + plan.Updated.Count == 0)
        {
            return new EditCheck
            {
                Unchanged = new EditDone(EditAction.EnvPull, EditOutcome.Unchanged, where) { Keys = plan.Keys },
            };
        }

        List<string> changes =
        [
            $"Imports {Count(plan.Created.Count + plan.Updated.Count, "variable")} into {Shown(where)}: {plan.Created.Count} new, {plan.Updated.Count} updated, {plan.Unchanged} unchanged.",
        ];

        foreach (var written in plan.Keys.Where(key => key.Change != EnvWriteChange.Unchanged).GroupBy(key => (key.Entry, key.Change)))
        {
            var keys = Joined(written.Select(key => Name(key.Key)).ToList());
            changes.Add(written.Key.Change == EnvWriteChange.New
                ? $"New on {ApprovalPrompt.Shown(written.Key.Entry)}: {keys}."
                : $"Updated on {ApprovalPrompt.Shown(written.Key.Entry)}: {keys}; the previous values stay in the item's history.");
        }

        if (plan.CreatesHome)
        {
            changes.Add($"Creates {ApprovalPrompt.Shown(plan.Home)} and tags it into {Shown(where)}.");
        }

        return Ready(new EditPreview(EditAction.EnvPull, Shown(where), changes) { Plan = plan });
    }

    private static EditCheck CheckTag(Vault vault, TagItem change, EditScene scene)
    {
        if (vault.Find(change.Item) is not { } entry)
        {
            return Refused(EditRefusalKind.NotFound, $"no item '{change.Item}'");
        }

        var name = EntryName.Of(entry);

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            return Refused(EditRefusalKind.Invalid, $"{Shown(entry.Path)} is in keypaste's own group; it belongs to no project");
        }

        var matching = (vault.Tags(name) ?? [])
            .Where(held => ProjectTag.Read(held) is { Kind: ProjectTagKind.Member } read
                && string.Equals(read.Project, change.Project, StringComparison.Ordinal)
                && string.Equals(read.Environment, change.Environment, StringComparison.Ordinal))
            .ToList();

        if (change.Tagging ? matching.Count > 0 : matching.Count == 0)
        {
            return new EditCheck
            {
                Unchanged = new EditDone(change.Action, EditOutcome.Unchanged, entry.Path) { Entry = name },
            };
        }

        _ = ProjectTag.TryFor(change.Project, change.Environment, out var tag, out _);
        IReadOnlyList<string> changing = change.Tagging ? [tag] : matching;

        var preview = ProjectTagChange.Preview(vault, name, changing, change.Tagging, scene.ClientsPath, scene.Now)!;
        var sentences = preview.Describe();

        // Shown first to the person who allows it, so an agent that would get the item unasked is
        // the first thing read, not the last.
        IReadOnlyList<string> changes = preview.Automatic.Count > 0
            ? [sentences[^1], .. sentences.Take(sentences.Count - 1)]
            : sentences;

        return Ready(new EditPreview(change.Action, Shown(entry.Path), changes)
        {
            Entry = name,
            Tags = changing,
            TagSentences = sentences,
        });
    }

    private static EditResult ApplyAdd(Vault vault, AddItem add, EditPreview preview)
    {
        vault.CreateEntryAtPath(new VaultEntry
        {
            Title = add.Name.Title,
            Username = add.Username,
            Password = add.Password!.Text!,
            Url = add.Url,
            Notes = add.Notes,
            GroupPath = add.Name.GroupPath,
        });

        return Done(new EditDone(EditAction.Add, EditOutcome.Created, TerminalEdit.PathOf(add.Name))
        {
            SharesPath = preview.SharesPath,
        });
    }

    private static EditResult ApplySet(Vault vault, SetPassword set, EditPreview preview)
    {
        var value = set.Password!.Text!;

        if (preview.Creates)
        {
            vault.CreateEntryAtPath(new VaultEntry { GroupPath = set.Name.GroupPath, Title = set.Name.Title, Password = value });
        }
        else
        {
            vault.UpdateEntry(vault.Find(set.Name)! with { Password = value });
        }

        return Done(new EditDone(EditAction.Set, preview.Creates ? EditOutcome.Created : EditOutcome.Updated, TerminalEdit.PathOf(set.Name)));
    }

    private static EditResult ApplyFields(Vault vault, SetFields set)
    {
        vault.SetFields(set.Name, set.Fields);

        return Done(new EditDone(EditAction.SetFields, EditOutcome.FieldsSet, TerminalEdit.PathOf(set.Name))
        {
            Names = [.. set.Fields.Select(field => field.Name)],
        });
    }

    private static EditResult ApplyRemove(Vault vault, RemoveItem remove)
    {
        // Nothing removed means nothing to save. Something wrote to the file between the check and
        // here, and the honest answer is that this run did not do it.
        return vault.RemoveEntry(remove.Item) switch
        {
            DeletionOutcome.NothingMatched => Refuse(EditRefusalKind.NotFound, $"'{remove.Item}' was not removed; the vault is unchanged"),
            DeletionOutcome.Recycled => Done(new EditDone(EditAction.Remove, EditOutcome.Recycled, remove.Item)),
            _ => Done(new EditDone(EditAction.Remove, EditOutcome.Deleted, remove.Item)),
        };
    }

    private static EditResult ApplyRotate(Vault vault, RotatePassword rotate)
    {
        var path = TerminalEdit.PathOf(rotate.Name);

        return EntryRotation.Rotate(vault, rotate.Name, rotate.Recipe) switch
        {
            RotateOutcome.NotFound => Refuse(EditRefusalKind.NotFound, $"no item named {Shown(path)}"),
            RotateOutcome.Reserved => Refuse(EditRefusalKind.Invalid, $"{Shown(path)} is keypaste's own group; it cannot be written here"),
            _ => Done(new EditDone(EditAction.Rotate, EditOutcome.Rotated, path)),
        };
    }

    private static EditResult ApplyRemoveField(Vault vault, RemoveField remove)
    {
        var entry = vault.Find(remove.Item)!;

        return vault.RemoveField(EntryName.Of(entry), remove.Field)
            ? Done(new EditDone(EditAction.RemoveField, EditOutcome.FieldRemoved, entry.Path) { Names = [remove.Field] })
            : Refuse(EditRefusalKind.NotFound, $"'{Shown(entry.Path)}' has no field '{remove.Field}'");
    }

    private static EditResult ApplyEnvSet(Vault vault, EnvSetKey set, EditPreview preview)
    {
        var plan = preview.Plan!;

        if (!new EnvStore(vault).TryApply(plan, out var rejection))
        {
            return Refuse(EditRefusalKind.Invalid, Prose(rejection));
        }

        return Done(new EditDone(EditAction.EnvSet, EditOutcome.KeySet, TerminalEdit.PathOf(preview.Entry!))
        {
            Names = [set.Key],
            Keys = plan.Keys,
            Entry = preview.Entry,
            CreatesHome = plan.CreatesHome,
        });
    }

    private static EditResult ApplyEnvRemove(Vault vault, EnvRemoveKey remove, EditPreview preview)
    {
        var removal = new EnvStore(vault).Remove(remove.Project, remove.Environment, remove.Key, preview.Entry);

        return removal.Outcome switch
        {
            EnvRemoveOutcome.NothingMatched => Refuse(EditRefusalKind.NotFound, $"'{remove.Key}' was not removed; the vault is unchanged"),
            EnvRemoveOutcome.Ambiguous or EnvRemoveOutcome.Refused => Refuse(EditRefusalKind.Conflict, Prose(removal.Refusal)),
            _ => Done(new EditDone(EditAction.EnvRemove, EditOutcome.KeyRemoved, TerminalEdit.PathOf(preview.Entry!))
            {
                Names = [remove.Key],
                Entry = preview.Entry,
            }),
        };
    }

    private static EditResult ApplyPull(Vault vault, EnvPullFile pull, EditPreview preview)
    {
        var plan = preview.Plan!;

        if (!new EnvStore(vault).TryApply(plan, out var rejection))
        {
            // Everything that could be refused was checked first, so reaching here means the check
            // and the writer disagree. Nothing has been saved, so the file on disk is untouched.
            return new EditResult
            {
                Refusal = new EditRefusal(EditRefusalKind.Conflict, rejection) { Then = ["Nothing was imported."] },
            };
        }

        return Done(new EditDone(EditAction.EnvPull, EditOutcome.Imported, $"{pull.Project}/{pull.Environment}")
        {
            Keys = plan.Keys,
            CreatesHome = plan.CreatesHome,
        });
    }

    private static EditResult ApplyTag(Vault vault, TagItem change, EditPreview preview)
    {
        var name = preview.Entry!;
        _ = change.Tagging ? vault.AddTag(name, preview.Tags[0]) : vault.RemoveTags(name, preview.Tags);

        return Done(new EditDone(change.Action, change.Tagging ? EditOutcome.Tagged : EditOutcome.Untagged, TerminalEdit.PathOf(name))
        {
            Names = preview.Tags,
            Entry = name,
        });
    }

    /// <summary>The item <c>--entry</c> names, resolved; null when the option was not given.</summary>
    private static bool TryEntry(Vault vault, string? path, out EntryName? entry, out string error)
    {
        entry = null;
        error = string.Empty;

        if (path is null)
        {
            return true;
        }

        if (vault.Find(path) is not { } found)
        {
            error = $"no item '{EntryNameSanitizer.SanitizePath(path).Text}'";
            return false;
        }

        entry = EntryName.Of(found);
        return true;
    }

    private static EditCheck Ready(EditPreview preview) => new() { Preview = preview };

    private static EditCheck Refused(EditRefusalKind kind, string reason) => new() { Refusal = new EditRefusal(kind, reason) };

    private static EditResult Refuse(EditRefusalKind kind, string reason) => new() { Refusal = new EditRefusal(kind, reason) };

    private static EditResult Done(EditDone done) => new() { Done = done };

    private static string Shown(string path) => EntryNameSanitizer.SanitizePath(path).Text;

    private static string Name(string name) => EntryNameSanitizer.Sanitize(name).Text;

    private static string Prose(string text) => EntryNameSanitizer.SanitizeProse(text, _proseLength).Text;

    private static string Joined(List<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
