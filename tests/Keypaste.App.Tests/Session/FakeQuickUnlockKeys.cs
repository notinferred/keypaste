using System.Security.Cryptography;
using Keypaste.App.Session;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Windows Hello or Touch ID without a person: the HMAC-SHA256 of each challenge under a key of its own, which gives one
/// key for one challenge as both do, answering as a test sets it and remembering which challenges it was asked to forget.
/// </summary>
internal sealed class FakeQuickUnlockKeys(string name = "Windows Hello") : IQuickUnlockKeys
{
    private readonly HashSet<string> _forgotten = new(StringComparer.Ordinal);
    private readonly HashSet<string> _made = new(StringComparer.Ordinal);
    private byte[] _key = RandomNumberGenerator.GetBytes(32);
    private int _asked;

    public string Name => name;

    /// <summary>Whether the keys are set up.</summary>
    internal bool Available { get; set; } = true;

    /// <summary>What the person does when asked.</summary>
    internal KeyOutcome Response { get; set; } = KeyOutcome.Released;

    /// <summary>What happens while the person is being asked, before they answer.</summary>
    internal Action? WhileAsking { get; set; }

    /// <summary>How many times the person was asked.</summary>
    internal int Asked => Volatile.Read(ref _asked);

    /// <summary>How many keys were made and not yet forgotten, as Touch ID would keep them in the keychain.</summary>
    internal int Kept
    {
        get
        {
            lock (_made)
            {
                return _made.Count;
            }
        }
    }

    /// <summary>How many times a key was forgotten.</summary>
    internal int Forgotten
    {
        get
        {
            lock (_made)
            {
                return _forgotten.Count;
            }
        }
    }

    public bool IsAvailable() => Available;

    public KeyOutcome Make(byte[] challenge, out byte[]? key)
    {
        var outcome = Ask(challenge, out key);

        if (outcome == KeyOutcome.Released)
        {
            lock (_made)
            {
                _made.Add(Convert.ToHexString(challenge));
            }
        }

        return outcome;
    }

    public KeyOutcome Answer(byte[] challenge, out byte[]? key) => Ask(challenge, out key);

    public void Forget(byte[] challenge)
    {
        var account = Convert.ToHexString(challenge);

        lock (_made)
        {
            _made.Remove(account);
            _forgotten.Add(account);
        }
    }

    /// <summary>The keys made again, as resetting Windows Hello's PIN or changing Touch ID's fingerprints does, so no earlier key comes back.</summary>
    internal void ReplaceKey() => _key = RandomNumberGenerator.GetBytes(32);

    private KeyOutcome Ask(byte[] challenge, out byte[]? key)
    {
        Interlocked.Increment(ref _asked);
        WhileAsking?.Invoke();

        key = Response == KeyOutcome.Released ? HMACSHA256.HashData(_key, challenge) : null;
        return Response;
    }
}
