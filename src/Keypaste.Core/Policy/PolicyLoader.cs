using System.Globalization;

namespace Keypaste.Core.Policy;

/// <summary>Why a policy file is, or is not, in force.</summary>
public enum PolicyStatus
{
    /// <summary>There is no file. Nothing is pre-authorized, which is keypaste's default state.</summary>
    Absent = 0,

    /// <summary>The file exists and declares no rules.</summary>
    Empty = 1,

    /// <summary>The file was read and every rule in it is in force.</summary>
    InForce = 2,

    /// <summary>Something is wrong with the file, so none of it applies.</summary>
    Rejected = 3,
}

/// <summary>The result of looking for a policy file.</summary>
/// <remarks>
/// <see cref="Rules"/> is <see cref="PolicyDocument.None"/> for every status but
/// <see cref="PolicyStatus.InForce"/>, so there is exactly one way for a caller to end up holding
/// rules and no way to hold them by accident.
/// </remarks>
public sealed class PolicyLoad
{
    private PolicyLoad(PolicyStatus status, string path, PolicyDocument rules, string digest, string reason)
    {
        Status = status;
        Path = path;
        Rules = rules;
        Digest = digest;
        Reason = reason;
    }

    /// <summary>What happened.</summary>
    public PolicyStatus Status { get; }

    /// <summary>The path that was looked at.</summary>
    public string Path { get; }

    /// <summary>The rules in force, or <see cref="PolicyDocument.None"/>.</summary>
    public PolicyDocument Rules { get; }

    /// <summary>
    /// A short hash of the exact bytes that were parsed, or an empty string when none were.
    /// </summary>
    /// <remarks>
    /// Printed by the approver at startup and by <c>keypaste policy ls</c>. The approver reads the
    /// file once and holds those rules for its whole session, so without a digest the two can
    /// silently disagree — <c>policy ls</c> showing a file edited since the approver started — and
    /// nobody could tell by looking (THREATS.md T-15).
    /// </remarks>
    public string Digest { get; }

    /// <summary>One sentence for the operator, whatever the status.</summary>
    public string Reason { get; }

    /// <summary>Whether any rule is in force.</summary>
    public bool HasRules => Rules.Rules.Count > 0;

    internal static PolicyLoad Absent(string path) =>
        new(PolicyStatus.Absent, path, PolicyDocument.None, string.Empty,
            $"no file at {path}, so every request is shown to you");

    internal static PolicyLoad Empty(string path, string digest) =>
        new(PolicyStatus.Empty, path, PolicyDocument.None, digest,
            $"no rules in {path}, so every request is shown to you");

    internal static PolicyLoad InForce(string path, PolicyDocument rules, string digest) =>
        new(PolicyStatus.InForce, path, rules, digest,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{rules.Rules.Count} rule{(rules.Rules.Count == 1 ? string.Empty : "s")} from {path} [{digest}]"));

    internal static PolicyLoad Rejected(string path, string problem) =>
        new(PolicyStatus.Rejected, path, PolicyDocument.None, string.Empty,
            $"{path} is NOT in force - {problem}");
}

/// <summary>Reads the policy file, and refuses it whole rather than partly.</summary>
/// <remarks>
/// <para>
/// <b>Nothing here is fatal.</b> Every failure — missing, unreadable, malformed, too permissive —
/// produces the same thing as far as an agent is concerned: no rules, so every request reaches a
/// person. Refusing to start the approver instead would turn a typo, or a planted <c>chmod 000</c>,
/// into a denial of service on the human's own vault, and the release direction is already the safe
/// one (docs/PRODUCT.md law 3.7).
/// </para>
/// <para>
/// <b>The size is checked before the read and the bytes are validated after it, from memory.</b>
/// Checking then re-opening would leave a window in which the file changed between the check and
/// the use; reading first and asking questions of what is already in hand closes it. It also means
/// a four-gigabyte <c>policy.toml</c> is never loaded into the process holding an unlocked vault.
/// </para>
/// <para>
/// <b>A policy file writable by anyone but its owner is refused, and never repaired.</b>
/// <c>AuditLog</c> tightens the permissions on <em>its</em> file, which is right for a file keypaste
/// writes: narrowing it before the first write is complete and honest. This is a file keypaste is
/// about to <em>obey</em>. Silently repairing it would be a race — between the change and the read,
/// whoever had write access still has it — and it would destroy the evidence that something was
/// wrong with an authorization document. On Windows there is no check at all rather than a partial
/// one, for the reason SECURITY.md already gives about the audit log: a check that passes on a
/// world-writable directory is worse than none, because it implies one happened.
/// </para>
/// </remarks>
public static class PolicyLoader
{
    /// <summary>How many hex characters of the digest are shown.</summary>
    internal const int DigestLength = 8;

    private static readonly UnixFileMode _writableByOthers = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    /// <summary>Reads a policy file, if there is one.</summary>
    /// <param name="path">Where to look.</param>
    /// <returns>What was found, and why it is or is not in force.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static PolicyLoad Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var full = System.IO.Path.GetFullPath(path);

        if (!File.Exists(full))
        {
            return PolicyLoad.Absent(full);
        }

        if (TooPermissive(full, out var permissionProblem))
        {
            return PolicyLoad.Rejected(full, permissionProblem);
        }

        byte[] bytes;

        try
        {
            // Length first, so an enormous file is refused rather than read. Checked against the
            // reader's own cap so there is one number, not two that could drift apart.
            var length = new FileInfo(full).Length;

            if (length > Toml.MaximumBytes)
            {
                return PolicyLoad.Rejected(
                    full,
                    $"it is larger than {Toml.MaximumBytes / 1024} KiB, which is not a policy file");
            }

            bytes = File.ReadAllBytes(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return PolicyLoad.Rejected(full, $"it could not be read: {ex.Message}");
        }

        var digest = Fingerprint(bytes);

        if (!Toml.TryDecode(bytes, out var text, out var decodeError))
        {
            return PolicyLoad.Rejected(full, decodeError);
        }

        if (!Toml.TryParse(text, out var syntax, out var parseError))
        {
            return PolicyLoad.Rejected(full, parseError);
        }

        if (!PolicyDocument.TryCreate(syntax, out var rules, out var ruleError))
        {
            return PolicyLoad.Rejected(full, ruleError);
        }

        return rules.Rules.Count == 0
            ? PolicyLoad.Empty(full, digest)
            : PolicyLoad.InForce(full, rules, digest);
    }

    /// <summary>Whether the file or its directory can be written by somebody other than its owner.</summary>
    /// <remarks>
    /// The directory matters as much as the file: anyone who can write <c>~/.keypaste</c> can replace
    /// <c>policy.toml</c> wholesale, which is the same authority by a different route. The file's own
    /// mode is read through any symlink, deliberately — what matters is the mode of the bytes about
    /// to be read, not of the pointer to them.
    /// </remarks>
    private static bool TooPermissive(string path, out string problem)
    {
        problem = string.Empty;

        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        if ((File.GetUnixFileMode(path) & _writableByOthers) != 0)
        {
            problem = "it is writable by users other than its owner; run chmod 600 on it";
            return true;
        }

        var directory = System.IO.Path.GetDirectoryName(path);

        if (directory is { Length: > 0 }
            && Directory.Exists(directory)
            && (File.GetUnixFileMode(directory) & _writableByOthers) != 0)
        {
            problem = $"{directory} is writable by users other than its owner; run chmod 700 on it";
            return true;
        }

        return false;
    }

    private static string Fingerprint(ReadOnlySpan<byte> bytes) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))[..DigestLength]}";
}
