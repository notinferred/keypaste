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
/// <c>sun_path</c> length problem to discover on somebody's long home directory (CORE.md law 3.9).
/// </para>
/// <para>
/// <b>The name carries a per-user discriminator</b> because .NET's Unix emulation puts the socket
/// at a predictable path under the shared temporary directory. Without it, two users on one machine
/// would collide, and the second would be unable to start their approver at all.
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

    /// <summary>What every default pipe name starts with.</summary>
    public const string Prefix = "keypaste-agent-";

    /// <summary>The number of hex characters of user discriminator in a default name.</summary>
    public const int DiscriminatorLength = 16;

    /// <summary>The longest name this will accept, since a pipe name is also a path component.</summary>
    public const int MaximumLength = 96;

    /// <summary>Which pipe the approver listens on and the bridge connects to.</summary>
    /// <param name="fromFlag">A name given on the command line, or null.</param>
    /// <param name="fromEnvironment">The value of <see cref="EnvironmentVariable"/>, or null.</param>
    /// <returns>The pipe name. The flag wins, then the environment, then the per-user default.</returns>
    /// <exception cref="ArgumentException">An explicit name is empty, over-long, or has a path separator in it.</exception>
    /// <remarks>
    /// The same shape as <see cref="VaultLocation.TryResolve"/> and
    /// <see cref="Audit.KeypasteHome.Resolve"/>: flag, then environment, then a default, with an
    /// empty value counting as unset. One rule written once (CORE.md law 4.3).
    /// </remarks>
    public static string Resolve(string? fromFlag, string? fromEnvironment)
    {
        if (fromFlag is { Length: > 0 })
        {
            return Checked(fromFlag, nameof(fromFlag));
        }

        if (fromEnvironment is { Length: > 0 })
        {
            return Checked(fromEnvironment, nameof(fromEnvironment));
        }

        return Prefix + Discriminator();
    }

    /// <summary>A stable, non-secret discriminator for the current user.</summary>
    /// <returns>Hex characters derived from the user's profile directory.</returns>
    /// <remarks>
    /// Derived from the profile path rather than from a user name because that path already differs
    /// per user on every platform keypaste supports, and because a name can contain characters a
    /// pipe name cannot. It is a namespacing device and nothing else: it is not secret, and knowing
    /// it grants nothing, exactly as with <see cref="EntryHandle"/>.
    /// </remarks>
    private static string Discriminator()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(profile), digest);

        return Convert.ToHexStringLower(digest[..(DiscriminatorLength / 2)]);
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
