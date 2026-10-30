using System.Text.Json.Serialization;
using Keypaste.Core.Audit;
using Keypaste.Core.Editing;

namespace Keypaste.Core.Ipc;

/// <summary>Writes the approver frames below without reflection, so the trim and AOT analyzers accept it (D-0019).</summary>
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
[JsonSerializable(typeof(AttachRequestFrame))]
[JsonSerializable(typeof(AttachReplyFrame))]
[JsonSerializable(typeof(CredentialRequestFrame))]
[JsonSerializable(typeof(CredentialReplyFrame))]
[JsonSerializable(typeof(EnvRequestFrame))]
[JsonSerializable(typeof(EnvReplyFrame))]
[JsonSerializable(typeof(AttachedFrame))]
[JsonSerializable(typeof(GrantsReplyFrame))]
[JsonSerializable(typeof(RevokeGrantsRequestFrame))]
[JsonSerializable(typeof(RevokeGrantsReplyFrame))]
[JsonSerializable(typeof(LockReplyFrame))]
[JsonSerializable(typeof(TokenEnvRequestFrame))]
[JsonSerializable(typeof(RunRequestFrame))]
[JsonSerializable(typeof(RunReplyFrame))]
[JsonSerializable(typeof(WithdrawFrame))]
[JsonSerializable(typeof(AccessListRequestFrame))]
[JsonSerializable(typeof(AccessListReplyFrame))]
[JsonSerializable(typeof(AccessRowFrame))]
[JsonSerializable(typeof(AccessChangeRequestFrame))]
[JsonSerializable(typeof(AccessChangeReplyFrame))]
[JsonSerializable(typeof(EditRequestFrame))]
[JsonSerializable(typeof(EditReplyFrame))]
[JsonSerializable(typeof(BrowserPairRequestFrame))]
[JsonSerializable(typeof(BrowserCheckRequestFrame))]
[JsonSerializable(typeof(BrowserFillRequestFrame))]
[JsonSerializable(typeof(BrowserReplyFrame))]
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

internal sealed class AttachRequestFrame(AttachRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.AttachKind;
    public string Vault => request.Vault;
    public string? ClientName => request.Client?.Name;
    public string? ClientVersion => request.Client?.Version;
    public string? ClientLabel => request.Client?.Label;
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
}

internal sealed class CredentialRequestFrame(CredentialRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.CredentialKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Entry => request.Entry;
    public string Field => request.Field;
    public string Reason => request.Reason;
    public int TtlSeconds => request.TtlSeconds;
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
    public int TtlSeconds => reply.TtlSeconds;
    public string? Entry => reply.Entry;
    public string? Session => reply.Session;
    public bool Recorded => reply.Recorded;
    public string? Value => reply.Value;
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

    /// <summary>The texts filled into a released set's values (D-0465), or null when none were or the set was not released, so a frame without one is written as before.</summary>
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

internal sealed class GrantsReplyFrame(GrantsReply reply, IEnumerable<GrantRow> grants, bool complete)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.GrantsKind;
    public bool Answered => reply.Answered;
    public bool Complete => complete;
    public string Reason => reply.Reason;
    public IEnumerable<GrantRow> Grants => grants;
}

internal sealed class GrantRow(GrantSummary grant)
{
    public string Id => grant.Id;
    public string Kind => grant.Kind;
    public string Client => grant.Client;
    public string Scope => grant.Scope;
    public string Field => grant.Field;
    public int SecondsLeft => grant.SecondsLeft;
}

internal sealed class RevokeGrantsRequestFrame(RevokeGrantsRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.RevokeGrantsKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public IReadOnlyList<string> Ids => request.Ids;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Client => request.Client;
    public bool All => request.All;
}

internal sealed class RevokeGrantsReplyFrame(RevokeGrantsReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.RevokeGrantsKind;
    public int Revoked => reply.Revoked;
    public string Reason => reply.Reason;
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
    public int GrantedSeconds => reply.GrantedSeconds;
    public IReadOnlyList<string> Entries => reply.Entries;
    public string? Session => reply.Session;
    public bool Recorded => reply.Recorded;
    public IEnumerable<ProblemRow> Problems => ProblemRow.Of(reply.Set);
    public IEnumerable<VariableRow>? Variables => VariableRow.Of(reply.Set);
    public IEnumerable<VariableRow>? Fills => VariableRow.FillsOf(reply.Set);
}

internal sealed class AccessListRequestFrame(AccessListRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.AccessListKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public int From => request.From;
}

internal sealed class AccessListReplyFrame(AccessListReply reply, IEnumerable<AccessRowFrame> agents, bool complete)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.AccessListKind;
    public bool Answered => reply.Answered;
    public bool Complete => complete;
    public string Reason => reply.Reason;
    public IEnumerable<AccessRowFrame> Agents => agents;
}

internal sealed class AccessRowFrame(AccessRow row)
{
    public string Name => row.Name;
    public string Record => row.Record;
    public bool Understood => row.Understood;
    public int? ProjectsNotShown => row.ProjectsNotShown > 0 ? row.ProjectsNotShown : null;
}

internal sealed class AccessChangeRequestFrame(AccessChangeRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.AccessChangeKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Agent => request.Agent;
    public string Reason => request.Reason;
    public string Origin => ApproverProtocol.OriginWord(request.Origin);
    public string? Project => request.Project;
    public string? Mode => request.Mode;
    public IReadOnlyList<string>? See => request.See;
    public string? Values => request.Values;
    public string? Run => request.Run;
    public string? PerHour => request.PerHour;
    public string? Until => request.Until;
    public bool Reset => request.Reset;
}

internal sealed class AccessChangeReplyFrame(AccessChangeReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.AccessChangeKind;
    public string Decision => reply.Decision == AuditDecision.Granted ? "granted" : "denied";
    public int Method => (int)reply.Method;
    public string Reason => reply.Reason;
    public bool Recorded => reply.Recorded;
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

internal sealed class BrowserFillRequestFrame(BrowserFillRequest request)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserFillKind;
    public string Vault => request.Vault;
    public string Session => request.Session;
    public string Pairing => request.Pairing;
    public string IdentificationKey => request.IdentificationKey;
    public string Url => request.Url;
    public string? SubmitUrl => request.SubmitUrl;
}

internal sealed class BrowserReplyFrame(BrowserReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserReplyKind;
    public int Outcome => (int)reply.Outcome;
    public string Reason => reply.Reason;
    public string? Pairing => reply.Pairing;
}

internal sealed class BrowserFillReplyFrame(BrowserFillReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.BrowserFillKind;
    public int Outcome => (int)reply.Outcome;
    public string Reason => reply.Reason;
    public IEnumerable<BrowserLoginRow> Logins => reply.Logins.Select(login => new BrowserLoginRow(login));
}

internal sealed class BrowserLoginRow(BrowserLogin login)
{
    public string Login => login.Login;
    public string Password => login.Password;
    public string Name => login.Name;
    public string Uuid => login.Uuid;
}
