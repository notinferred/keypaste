using System.Text.Json.Serialization;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;

namespace Keypaste.Core.Ipc;

/// <summary>Writes the approver frames below without reflection, so the trim and AOT analyzers accept it.</summary>
/// <remarks>
/// Each frame is a view of its message that names the wire members in wire order: the generator
/// writes properties in declaration order, and a member that is null is left out unless it says
/// otherwise.
/// </remarks>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Serialization,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NamesRequestFrame))]
[JsonSerializable(typeof(NamesReplyFrame))]
[JsonSerializable(typeof(FindRequestFrame))]
[JsonSerializable(typeof(FindReplyFrame))]
[JsonSerializable(typeof(AttachRequestFrame))]
[JsonSerializable(typeof(AttachReplyFrame))]
[JsonSerializable(typeof(CredentialRequestFrame))]
[JsonSerializable(typeof(CredentialReplyFrame))]
[JsonSerializable(typeof(SetRequestFrame))]
[JsonSerializable(typeof(SetReplyFrame))]
[JsonSerializable(typeof(EnvRequestFrame))]
[JsonSerializable(typeof(EnvReplyFrame))]
[JsonSerializable(typeof(AttachedFrame))]
[JsonSerializable(typeof(LockReplyFrame))]
[JsonSerializable(typeof(RulesChangeRequestFrame))]
[JsonSerializable(typeof(RulesChangeReplyFrame))]
[JsonSerializable(typeof(SaveRequestFrame))]
[JsonSerializable(typeof(SaveReplyFrame))]
[JsonSerializable(typeof(ItemChangeRequestFrame))]
[JsonSerializable(typeof(ItemChangeReplyFrame))]
[JsonSerializable(typeof(TokenEnvRequestFrame))]
[JsonSerializable(typeof(RunRequestFrame))]
[JsonSerializable(typeof(RunReplyFrame))]
[JsonSerializable(typeof(WithdrawFrame))]
[JsonSerializable(typeof(EditRequestFrame))]
[JsonSerializable(typeof(EditReplyFrame))]
[JsonSerializable(typeof(BrowserPairRequestFrame))]
[JsonSerializable(typeof(BrowserCheckRequestFrame))]
[JsonSerializable(typeof(BrowserListRequestFrame))]
[JsonSerializable(typeof(BrowserReleaseRequestFrame))]
[JsonSerializable(typeof(BrowserShowRequestFrame))]
[JsonSerializable(typeof(BrowserReplyFrame))]
[JsonSerializable(typeof(BrowserListReplyFrame))]
[JsonSerializable(typeof(BrowserFillReplyFrame))]
internal sealed partial class ApproverJsonContext : JsonSerializerContext
{
}

internal sealed class NamesRequestFrame(NamesRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.NamesKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public IReadOnlyList<string> Exposure => request.Exposure;
    public string? ClientLabel => request.ClientLabel;
}

internal sealed class NamesReplyFrame(NamesReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.NamesKind;
    public bool Unlocked => reply.VaultUnlocked;
    public string Reason => reply.Reason;
    public bool Complete => reply.Complete;
    public string? Session => reply.Session;
    public int? Method => (int?)reply.Refusal;
    public bool Recorded => reply.Recorded;
    public IEnumerable<NameRow> Names => NameRow.Of(reply.Names);
}

internal sealed class NameRow(ListedEntry entry)
{
    public string Group => entry.Name.GroupPath;
    public string Title => entry.Name.Title;
    public IReadOnlyList<string> Fields => entry.Fields;
    public IReadOnlyList<string> Tags => entry.Tags;

    public static IEnumerable<NameRow> Of(IEnumerable<ListedEntry> entries) => entries.Select(entry => new NameRow(entry));
}

internal sealed class FindRequestFrame(FindRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.FindKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public IReadOnlyList<string> Exposure => request.Exposure;
    public string? ClientLabel => request.ClientLabel;
    public string? Text => request.Text;
    public string? Host => request.Host;
    public int? ItemKind => (int?)request.Kind;
    public string? RelatedTo => request.RelatedTo;
    public string? Project => request.Project;
}

internal sealed class FindReplyFrame(FindReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.FindKind;
    public bool Unlocked => reply.VaultUnlocked;
    public string Reason => reply.Reason;
    public bool Complete => reply.Complete;
    public string? Session => reply.Session;
    public int? Method => (int?)reply.Refusal;
    public bool Recorded => reply.Recorded;
    public IEnumerable<FoundRow> Found => FoundRow.Of(reply.Found);
    public IEnumerable<FoundProjectRow> Projects => reply.Projects.Select(project => new FoundProjectRow(project));
}

internal sealed class FoundProjectRow(AccessProject project)
{
    public string Name => project.Name;
    public IReadOnlyList<string> Environments => project.Environments;
    public int Items => project.Items;
}

internal sealed class FoundRow(FoundAccess found)
{
    public string Group => found.Entry.Listing.Name.GroupPath;
    public string Title => found.Entry.Listing.Name.Title;
    public int Kind => (int)found.Entry.Kind;
    public string? Origin => found.Entry.Origin?.ToString();
    public string? Username => found.Entry.Username;
    public IReadOnlyList<string> Fields => found.Entry.Listing.Fields;
    public IReadOnlyList<string> Tags => found.Entry.Listing.Tags;
    public IEnumerable<LinkRow> Links => found.Links.Select(link => new LinkRow(link));

    public static IEnumerable<FoundRow> Of(IEnumerable<FoundAccess> found) => found.Select(item => new FoundRow(item));
}

internal sealed class LinkRow(AccessLink link)
{
    public string Group => link.Name.GroupPath;
    public string Title => link.Name.Title;
    public string? Site => link.Site;
    public IReadOnlyList<string> Projects => link.Projects;
    public bool SameAccount => link.SameAccount;
    public bool References => link.References;
    public bool ReferencedBy => link.ReferencedBy;
}

internal sealed class AttachRequestFrame(AttachRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.AttachKind;
    public string Vault => request.Vault;
    public string? ClientName => request.Client?.Name;
    public string? ClientVersion => request.Client?.Version;
    public string? ClientLabel => request.Client?.Label;
    public string? ClientFolder => request.Client?.Folder;
    public IReadOnlyList<string>? Exposure => request.Exposure;
    public string? Browser => request.Browser;
}

internal sealed class AttachReplyFrame(AttachReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.AttachKind;
    public string? Session => reply.Session;
    public int? Method => (int?)reply.Refusal;
    public string Reason => reply.Reason;
    public string? OwnerVersion => reply.OwnerVersion;
    public string? DatabaseHash => reply.DatabaseHash;
    public IReadOnlyList<string>? BrowserFeatures => reply.BrowserFeatures;
}

internal sealed class CredentialRequestFrame(CredentialRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.CredentialKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Entry => request.Entry;
    public string Field => request.Field;
    public string? Attachment => request.Attachment;
    public string Reason => request.Reason;
    public IReadOnlyList<string> Exposure => request.Exposure;
    public string? Client => request.ClientName;
    public string? ClientVersion => request.ClientVersion;
    public string? ClientLabel => request.ClientLabel;
}

internal sealed class CredentialReplyFrame(CredentialReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.CredentialKind;
    public string Decision => reply.Decision == AuditDecision.Granted ? "granted" : "denied";
    public int Method => (int)reply.Method;
    public string Reason => reply.Reason;
    public int CodeSeconds => reply.CodeSeconds;
    public string? Entry => reply.Entry;
    public string? Session => reply.Session;
    public bool Recorded => reply.Recorded;
    public string? Value => reply.Value;
}

internal sealed class SetRequestFrame(SetRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.SetKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public IEnumerable<SetItemRow> Items => request.Items.Select(item => new SetItemRow(item));
    public string Reason => request.Reason;
    public IReadOnlyList<string> Exposure => request.Exposure;
    public string? Client => request.ClientName;
    public string? ClientVersion => request.ClientVersion;
    public string? ClientLabel => request.ClientLabel;
}

internal sealed class SetItemRow(SetItem item)
{
    public string Entry => item.Entry;
    public string Field => item.Field;
}

internal sealed class SetReplyFrame(SetReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.SetKind;
    public string Decision => reply.Decision == AuditDecision.Granted ? "granted" : "denied";
    public int Method => (int)reply.Method;
    public string Reason => reply.Reason;
    public string? Session => reply.Session;
    public bool Recorded => reply.Recorded;
    public IEnumerable<SetFieldRow> Fields => reply.Fields.Select(released => new SetFieldRow(released));
}

internal sealed class SetFieldRow(SetField released)
{
    public string Entry => released.Entry;
    public string Field => released.Field;
    public string Value => released.Value;
    public int CodeSeconds => released.CodeSeconds;
}

internal sealed class EnvRequestFrame(EnvRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.EnvProfileKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Project => request.Project;
    public IReadOnlyList<string> Command => request.Command;
    public string Directory => request.Directory;
    public string Profile => request.Profile;
    public IReadOnlyList<string>? Keys => request.Keys;
    public IReadOnlyList<string>? FileLines => request.FileLines;
    public string? Launch => request.Launch;
}

internal sealed class EnvReplyFrame(EnvReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.EnvKind;
    public string Project => reply.Set.Project;
    public string Profile => reply.Set.Profile;
    public int Outcome => (int)reply.Set.Outcome;
    public string Reason => reply.Reason;
    public IEnumerable<ProblemRow> Problems => ProblemRow.Of(reply.Set);
    public IEnumerable<VariableRow>? Variables => VariableRow.Of(reply.Set);
}

internal sealed class ProblemRow(EnvProblem problem)
{
    public string Key => problem.Key;
    public string Reason => problem.Reason;

    public static IEnumerable<ProblemRow> Of(EnvResolved set) => set.Problems.Select(problem => new ProblemRow(problem));
}

internal sealed class VariableRow(EnvVariable variable)
{
    public string Key => variable.Key;
    public string Value => variable.Value;

    /// <summary>The set's values, or null for a set that was not released, so a refusal has no member to carry one in.</summary>
    public static IEnumerable<VariableRow>? Of(EnvResolved set) =>
        set.Outcome == EnvOutcome.Resolved ? set.Variables.Select(variable => new VariableRow(variable)) : null;

    /// <summary>The texts filled into a released set's values, or null when none were or the set was not released, so a frame without one is written as before.</summary>
    public static IEnumerable<VariableRow>? FillsOf(EnvResolved set) =>
        set.Outcome == EnvOutcome.Resolved && set.Fills.Count > 0 ? set.Fills.Select(fill => new VariableRow(fill)) : null;
}

internal sealed class AttachedFrame(string kind, string vault, string session)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind => kind;
    public string Vault => vault;
    public string Session => session;
}

internal sealed class WithdrawFrame
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.WithdrawKind;
}

internal sealed class RulesChangeRequestFrame(RulesChangeRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.RulesKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Agent => request.Agent;
    public string Change => ApproverProtocol.ChangeWord(request.Change);
    public string Target => request.Target;
}

internal sealed class RulesChangeReplyFrame(RulesChangeReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.RulesKind;
    public bool Changed => reply.Changed;
    public string Reason => reply.Reason;
    public bool Recorded => reply.Recorded;
}

internal sealed class LockReplyFrame(LockReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.LockKind;
    public bool Locked => reply.Locked;
    public string Reason => reply.Reason;
}

internal sealed class TokenEnvRequestFrame(TokenEnvRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.TokenEnvKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Token => request.Token;
    public string Project => request.Project;
    public string Profile => request.Profile;
    public IReadOnlyList<string> Command => request.Command;
    public string Directory => request.Directory;
}

internal sealed class RunRequestFrame(RunRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.RunKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Program => request.Program;
    public IReadOnlyList<string> Command => request.Command;
    public string Directory => request.Directory;
    public string? Project => request.Project;
    public string Profile => request.Profile;
    public IReadOnlyList<string>? Keys => request.Keys;
    public IEnumerable<ReferenceRow>? References => request.References?.Select(reference => new ReferenceRow(reference));
    public string Reason => request.Reason;
    public IReadOnlyList<string> Exposure => request.Exposure;
    public string? ClientName => request.ClientName;
    public string? ClientVersion => request.ClientVersion;
    public string? ClientLabel => request.ClientLabel;
}

internal sealed class ReferenceRow(RunReference reference)
{
    public string Name => reference.Name;
    public string Ref => reference.Reference;
}

internal sealed class RunReplyFrame(RunReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.RunKind;
    public string Project => reply.Set.Project;
    public string Profile => reply.Set.Profile;
    public int Outcome => (int)reply.Set.Outcome;
    public string Reason => reply.Reason;
    public int Method => (int)reply.Method;
    public IReadOnlyList<string> Entries => reply.Entries;
    public string? Session => reply.Session;
    public bool Recorded => reply.Recorded;
    public IEnumerable<ProblemRow> Problems => ProblemRow.Of(reply.Set);
    public IEnumerable<VariableRow>? Variables => VariableRow.Of(reply.Set);
    public IEnumerable<VariableRow>? Fills => VariableRow.FillsOf(reply.Set);
}

internal sealed class EditRequestFrame(EditRequest request)
{
    private readonly TerminalEdit _edit = request.Edit;

    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.EditKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Action => ApproverProtocol.ActionWord(_edit.Action);
    public string? Group => NameOf(_edit)?.GroupPath;
    public string? Title => NameOf(_edit)?.Title;

    public string? Item => _edit switch
    {
        RemoveItem remove => remove.Item,
        RemoveField removal => removal.Item,
        TagItem tag => tag.Item,
        _ => null,
    };

    public string? Field => (_edit as RemoveField)?.Field;
    public string? Username => (_edit as AddItem)?.Username;
    public string? Url => (_edit as AddItem)?.Url;
    public string? Notes => (_edit as AddItem)?.Notes;

    // A secret generated from a recipe goes as the recipe, so the holder generates it and no value crosses.
    public string? Value => SecretOf(_edit) is { Recipe: null, Text: { } text } ? text : null;

    public RecipeFrame? Generate => _edit is RotatePassword rotate
        ? new RecipeFrame(rotate.Recipe)
        : SecretOf(_edit)?.Recipe is { } recipe ? new RecipeFrame(recipe) : null;

    public IEnumerable<FieldFrame>? Fields => (_edit as SetFields)?.Fields.Select(write => new FieldFrame(write.Name, write.Value ?? string.Empty));
    public bool? Plain => _edit is SetFields fields ? fields.Plain : null;

    public string? Project => _edit switch
    {
        EnvSetKey set => set.Project,
        EnvRemoveKey remove => remove.Project,
        EnvPullFile pull => pull.Project,
        TagItem tag => tag.Project,
        _ => null,
    };

    public string? Environment => _edit switch
    {
        EnvSetKey set => set.Environment,
        EnvRemoveKey remove => remove.Environment,
        EnvPullFile pull => pull.Environment,
        TagItem tag => tag.Environment,
        _ => null,
    };

    public string? Key => _edit switch
    {
        EnvSetKey set => set.Key,
        EnvRemoveKey remove => remove.Key,
        _ => null,
    };

    public string? Entry => _edit switch
    {
        EnvSetKey set => set.Entry,
        EnvRemoveKey remove => remove.Entry,
        EnvPullFile pull => pull.Entry,
        _ => null,
    };

    public string? Dotenv => (_edit as EnvPullFile)?.Text;

    private static EntryName? NameOf(TerminalEdit edit) => edit switch
    {
        AddItem add => add.Name,
        SetPassword set => set.Name,
        SetFields fields => fields.Name,
        RotatePassword rotate => rotate.Name,
        _ => null,
    };

    private static NewSecret? SecretOf(TerminalEdit edit) => edit switch
    {
        AddItem add => add.Password,
        SetPassword set => set.Password,
        EnvSetKey env => env.Value,
        _ => null,
    };
}

internal sealed class RecipeFrame(SecretRecipe recipe)
{
    public string Kind => recipe.Kind == SecretRecipeKind.Words ? "words" : "characters";
    public int? Length => recipe.Kind == SecretRecipeKind.Characters ? recipe.Characters.Length : null;
    public int? Alphabet => recipe.Kind == SecretRecipeKind.Characters ? (int)recipe.Characters.Alphabet : null;
    public bool? NoLookalikes => recipe.Kind == SecretRecipeKind.Characters ? recipe.Characters.ExcludeLookalikes : null;
    public int? Words => recipe.Kind == SecretRecipeKind.Words ? recipe.Words.WordCount : null;
    public string? Separator => recipe.Kind == SecretRecipeKind.Words ? new string(recipe.Words.Separator, 1) : null;
}

internal sealed class FieldFrame(string name, string value)
{
    public string Name => name;
    public string Value => value;
}

internal sealed class EditReplyFrame(EditReply reply, bool withKeys)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.EditKind;
    public string Decision => reply.Decision == AuditDecision.Granted ? "granted" : "denied";
    public int Method => (int)reply.Method;
    public string Reason => reply.Reason;
    public bool Recorded => reply.Recorded;
    public string? Refusal => reply.Refusal is { } kind ? ApproverProtocol.RefusalWord(kind) : null;
    public EditDoneFrame? Done => reply.Done is { } done ? new EditDoneFrame(done, withKeys) : null;
}

internal sealed class EditDoneFrame(EditDone done, bool withKeys)
{
    public string Action => ApproverProtocol.ActionWord(done.Action);
    public int Outcome => (int)done.Outcome;
    public string Item => done.Item;
    public IReadOnlyList<string> Names => done.Names;
    public NameFrame? Entry => done.Entry is { } entry ? new NameFrame(entry) : null;
    public bool SharesPath => done.SharesPath;
    public bool CreatesHome => done.CreatesHome;

    // Grouped by item, so a reply names each key once and stays near the size of the request that carried them.
    public IEnumerable<KeysFrame>? Keys => withKeys && done.Keys.Count > 0
        ? done.Keys.GroupBy(key => key.Entry).Select(group => new KeysFrame(group.Key, [.. group]))
        : null;
}

internal sealed class NameFrame(EntryName name)
{
    public string Group => name.GroupPath;
    public string Title => name.Title;
}

internal sealed class KeysFrame(EntryName entry, IReadOnlyList<EnvKeyWrite> keys)
{
    public string Group => entry.GroupPath;
    public string Title => entry.Title;
    public IEnumerable<string> New => keys.Where(key => key.Change == EnvWriteChange.New).Select(key => key.Key);
    public IEnumerable<string> Updated => keys.Where(key => key.Change == EnvWriteChange.Replaces).Select(key => key.Key);
    public IEnumerable<string> Unchanged => keys.Where(key => key.Change == EnvWriteChange.Unchanged).Select(key => key.Key);
}

internal sealed class BrowserPairRequestFrame(BrowserPairRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserPairKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string IdentificationKey => request.IdentificationKey;
}

internal sealed class BrowserCheckRequestFrame(BrowserCheckRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserCheckKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Pairing => request.Pairing;
    public string IdentificationKey => request.IdentificationKey;
}

internal sealed class BrowserListRequestFrame(BrowserListRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserListKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Pairing => request.Pairing;
    public string IdentificationKey => request.IdentificationKey;
    public string Url => request.Url;
    public string? SubmitUrl => request.SubmitUrl;
    public string? TopUrl => request.TopUrl;

    // Written only when false, so a list that may ask is the frame it always was.
    public bool? AsksTrust => request.AsksTrust ? null : false;
}

internal sealed class BrowserReleaseRequestFrame(BrowserReleaseRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserReleaseKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Pairing => request.Pairing;
    public string IdentificationKey => request.IdentificationKey;
    public string Url => request.Url;
    public string? SubmitUrl => request.SubmitUrl;
    public string Uuid => request.Uuid;
    public string? TopUrl => request.TopUrl;
    public int Fill => (int)request.Fill;
    public bool Continued => request.Continued;
}

internal sealed class BrowserShowRequestFrame(BrowserShowRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserShowKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public int What => (int)request.What;
    public string VaultName => request.VaultName;
    public string Url => request.Url;
}

internal sealed class BrowserReplyFrame(BrowserReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserReplyKind;
    public int Outcome => (int)reply.Outcome;
    public string Reason => reply.Reason;
    public string? Pairing => reply.Pairing;
}

internal sealed class BrowserListReplyFrame(BrowserListReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserListKind;
    public int Outcome => (int)reply.Outcome;
    public string Reason => reply.Reason;
    public string Site => reply.Site;
    public string Warning => reply.Warning;
    public IReadOnlyList<string> LockedVaults => reply.LockedVaults;
    public IEnumerable<BrowserLoginNameRow> Names => reply.Names.Select(name => new BrowserLoginNameRow(name));
}

internal sealed class BrowserLoginNameRow(BrowserLoginName name)
{
    public string Uuid => name.Uuid;
    public string Title => name.Title;
    public string Username => name.Username;
    public string Vault => name.Vault;
}

internal sealed class BrowserFillReplyFrame(BrowserFillReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserReleaseKind;
    public int Outcome => (int)reply.Outcome;
    public string Reason => reply.Reason;
    public IEnumerable<BrowserLoginRow> Logins => reply.Logins.Select(login => new BrowserLoginRow(login));
    public string ContinueSite => reply.ContinueSite;
    public int ContinueSeconds => reply.ContinueSeconds;
}

internal sealed class BrowserLoginRow(BrowserLogin login)
{
    public string Login => login.Login;
    public string Password => login.Password;
    public string Name => login.Name;
    public string Uuid => login.Uuid;
    public string Totp => login.Totp;
}

internal sealed class SaveRequestFrame(SaveRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.SaveKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Item => request.Item;
    public string Field => request.Field;
    public string Project => request.Project;
    public string Environment => request.Environment;
    public string Key => request.Key;
    public string Reason => request.Reason;
    public IReadOnlyList<string> Exposure => request.Exposure;
    public string? Client => request.ClientName;
    public string? ClientVersion => request.ClientVersion;
    public string? ClientLabel => request.ClientLabel;
}

internal sealed class SaveReplyFrame(SaveReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.SaveKind;
    public string Decision => reply.Decision == AuditDecision.Granted ? "granted" : "denied";
    public int Method => (int)reply.Method;
    public string Reason => reply.Reason;
    public bool Recorded => reply.Recorded;
    public string? Where => reply.Where;
    public string? Session => reply.Session;
}

internal sealed class ItemChangeRequestFrame(ItemChangeRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.ItemChangeKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Entry => request.Entry;
    public string Change => request.Change;
    public string Expires => request.Expires;
    public string Reason => request.Reason;
    public IReadOnlyList<string> Exposure => request.Exposure;
    public string? Client => request.ClientName;
    public string? ClientVersion => request.ClientVersion;
    public string? ClientLabel => request.ClientLabel;
}

internal sealed class ItemChangeReplyFrame(ItemChangeReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.ItemChangeKind;
    public string Decision => reply.Decision == AuditDecision.Granted ? "granted" : "denied";
    public int Method => (int)reply.Method;
    public string Reason => reply.Reason;
    public bool Recorded => reply.Recorded;
    public string? Item => reply.Item;
    public string? Session => reply.Session;
}
