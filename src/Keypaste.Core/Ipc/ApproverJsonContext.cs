using System.Text.Json.Serialization;
using Keypaste.Core.Audit;

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
}

internal sealed class NamesReplyFrame(NamesReply reply, IEnumerable<NameRow> names, bool complete)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.NamesKind;
    public bool Unlocked => reply.VaultUnlocked;
    public string Reason => reply.Reason;
    public bool Complete => complete;
    public string? Session => reply.Session;
    public int? Method => (int?)reply.Refusal;
    public IEnumerable<NameRow> Names => names;
}

internal sealed class NameRow(ListedEntry entry)
{
    public string Group => entry.Name.GroupPath;
    public string Title => entry.Name.Title;
    public IReadOnlyList<string> Fields => entry.Fields;
    public IReadOnlyList<string> Tags => entry.Tags;
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
}

internal sealed class AttachReplyFrame(AttachReply reply)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind { get; } = ApproverProtocol.AttachKind;
    public string? Session => reply.Session;
    public int? Method => (int?)reply.Refusal;
    public string Reason => reply.Reason;
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
}

internal sealed class AttachedFrame(string kind, string vault, string session)
{
    public int V { get; } = ApproverProtocol.Version;
    public string Kind => kind;
    public string Vault => vault;
    public string Session => session;
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
    public bool Locking => reply.Locking;
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
    public IEnumerable<ProblemRow> Problems => ProblemRow.Of(reply.Set);
    public IEnumerable<VariableRow>? Variables => VariableRow.Of(reply.Set);
}
