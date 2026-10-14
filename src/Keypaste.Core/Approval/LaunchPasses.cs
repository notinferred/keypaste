using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Approval;

/// <summary>Exactly the <c>keypaste run --session</c> request the app's Run confirmation named. Holds no value.</summary>
/// <param name="Project">The project.</param>
/// <param name="Profile">The environment.</param>
/// <param name="Directory">Where the run starts.</param>
/// <param name="Command">The command the run's request names, one argument per item.</param>
/// <param name="Preview">The names, the items each comes from and the protection the confirmation showed.</param>
public sealed record LaunchClaim(string Project, string Profile, string Directory, IReadOnlyList<string> Command, EnvPreview Preview);

/// <summary>
/// The owner's single-use launch passes: each authorises exactly the one <c>keypaste run --session</c>
/// request the app's Run confirmation named on macOS (D-0452).
/// </summary>
/// <remarks>
/// A pass is the confirmation itself, carried to the run that Terminal starts, so it is spent the first
/// time anything presents it, lasts <see cref="Lifetime"/>, and is forgotten with every other pass when
/// the unlock that holds the store ends (D-0313). It is not a grant: nothing lists it and no later run
/// can use it.
/// </remarks>
public sealed class LaunchPasses : IDisposable
{
    /// <summary>The variable that carries a pass from Terminal's launcher to <c>keypaste run</c>.</summary>
    public const string EnvironmentVariable = "KEYPASTE_LAUNCH";

    /// <summary>How many characters a pass has: 32 random bytes in unpadded base64url.</summary>
    public const int Length = 43;

    /// <summary>How long a pass lasts once issued.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Issued> _passes = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private bool _disposed;

    /// <summary>Builds an empty store.</summary>
    /// <param name="clock">The clock passes expire on.</param>
    public LaunchPasses(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
    }

    /// <summary>Whether text has a pass's shape.</summary>
    /// <param name="pass">The text.</param>
    /// <returns><see langword="true"/> for exactly <see cref="Length"/> base64url characters.</returns>
    public static bool IsWellFormed([NotNullWhen(true)] string? pass) =>
        pass is { Length: Length } && pass.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Issues a pass for one request under one session.</summary>
    /// <param name="claim">The request the confirmation named.</param>
    /// <param name="session">The session the confirmation was answered in.</param>
    /// <returns>The pass, or null once the store's unlock has ended.</returns>
    public string? Issue(LaunchClaim claim, string session)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentException.ThrowIfNullOrEmpty(session);

        var pass = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            foreach (var expired in _passes.Where(pair => pair.Value.Expires.HasExpired(_clock)).Select(pair => pair.Key).ToList())
            {
                _passes.Remove(expired);
            }

            _passes[pass] = new Issued(claim, session, Deadline.Starting(_clock, Lifetime));
        }

        return pass;
    }

    /// <summary>Spends a pass, whatever is then made of it.</summary>
    /// <param name="pass">The pass presented.</param>
    /// <param name="session">The session the presenting request was admitted under.</param>
    /// <param name="claim">What it authorises, when this returns true.</param>
    /// <returns>True only for a pass this store issued under <paramref name="session"/> that has neither been presented before nor expired.</returns>
    public bool TryTake(string pass, string session, [NotNullWhen(true)] out LaunchClaim? claim)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(session);

        claim = null;

        lock (_gate)
        {
            if (!_passes.Remove(pass, out var issued)
                || issued.Expires.HasExpired(_clock)
                || !string.Equals(issued.Session, session, StringComparison.Ordinal))
            {
                return false;
            }

            claim = issued.Claim;
            return true;
        }
    }

    /// <summary>Forgets a pass the app could not hand to Terminal.</summary>
    /// <param name="pass">The pass.</param>
    public void Withdraw(string pass)
    {
        ArgumentNullException.ThrowIfNull(pass);

        lock (_gate)
        {
            _passes.Remove(pass);
        }
    }

    /// <summary>Forgets every pass and issues no more.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _passes.Clear();
        }
    }

    private sealed record Issued(LaunchClaim Claim, string Session, Deadline Expires);
}
