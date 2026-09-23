using Keypaste.Core.Ownership;

namespace Keypaste.Core.Ipc;

/// <summary>
/// Where the bridge looks for the approver, and what stops it finding somebody else's.
/// </summary>
/// <remarks>
/// <para>
/// A .NET named pipe, on both platforms, and the reason is that the runtime does the access check
/// for us. <see cref="System.IO.Pipes.PipeOptions.CurrentUserOnly"/> restricts the pipe's ACL to
/// the current user on Windows, and on Unix — where .NET implements named pipes over a Unix domain
/// socket — it creates the socket owner-only and verifies on connect that the peer's socket is
/// owned by the same user. That is one code path, no
/// <c>System.IO.Pipes.AccessControl</c> dependency, no hand-rolled <c>PipeSecurity</c>, and no
/// <c>sun_path</c> length problem to discover on somebody's long home directory (docs/PRODUCT.md law 3.9).
/// </para>
/// <para>
/// <b>The name carries a per-user, per-vault discriminator</b> (<see cref="VaultIdentity.Key"/>)
/// because .NET's Unix emulation puts the socket at a predictable path under the shared temporary
/// directory. Without it, two users on one machine would collide.
/// </para>
/// <para>
/// <b>Residual, for THREATS.md T-10.</b> That path is predictable, so another local user can
/// pre-create it and stop your approver binding — a denial of service. What they cannot do is be
/// connected to, because the ownership check refuses. Denial of service against the approver means
/// keypaste denies every request, which is the direction law 3.7 asks for.
/// </para>
/// </remarks>
public static class ApproverEndpoint
{
    /// <summary>The environment variable naming the pipe, for when the default will not do.</summary>
    public const string EnvironmentVariable = "KEYPASTE_APPROVER";

    /// <summary>What every derived pipe name starts with.</summary>
    public const string Prefix = "keypaste-vault-";

    /// <summary>The longest name this will accept, since a pipe name is also a path component.</summary>
    public const int MaximumLength = 96;

    /// <summary>Which pipe a vault's owner listens on and a bridge for that vault connects to.</summary>
    /// <param name="fromFlag">A name given on the command line, or null.</param>
    /// <param name="fromEnvironment">The value of <see cref="EnvironmentVariable"/>, or null.</param>
    /// <param name="vault">The vault, or null when none was named.</param>
    /// <returns>
    /// The pipe name: the flag wins, then the environment, then one derived from the vault. Null when
    /// nothing names a pipe or a vault.
    /// </returns>
    /// <exception cref="ArgumentException">An explicit name is empty, over-long, or has a path separator in it.</exception>
    /// <remarks>
    /// Derived per vault, so owners of two vaults never contend for one name (D-0309). An explicit
    /// name can still reach an owner of another vault; the attachment names the vault, and that owner
    /// refuses it.
    /// </remarks>
    public static string? Resolve(string? fromFlag, string? fromEnvironment, VaultIdentity? vault)
    {
        if (fromFlag is { Length: > 0 })
        {
            return Checked(fromFlag, nameof(fromFlag));
        }

        if (fromEnvironment is { Length: > 0 })
        {
            return Checked(fromEnvironment, nameof(fromEnvironment));
        }

        return vault is null ? null : Prefix + vault.Key;
    }

    private static string Checked(string name, string argument)
    {
        if (name.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"an approver name cannot be longer than {MaximumLength} characters", argument);
        }

        foreach (var c in name)
        {
            if (c is '/' or '\\' or ':' || char.IsControl(c))
            {
                throw new ArgumentException(
                    "an approver name cannot contain a path separator or a control character", argument);
            }
        }

        return name;
    }
}
