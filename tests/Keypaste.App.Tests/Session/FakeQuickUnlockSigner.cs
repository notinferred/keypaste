using System.Security.Cryptography;
using Keypaste.App.Session;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Windows Hello without a person: the HMAC-SHA256 of each challenge under a key of its own, which gives one signature
/// for one challenge as Windows Hello's RSA PKCS #1 v1.5 key does, and answers as a test sets it.
/// </summary>
internal sealed class FakeQuickUnlockSigner : IQuickUnlockSigner
{
    private byte[] _key = RandomNumberGenerator.GetBytes(32);
    private int _asked;

    public string Name => "Windows Hello";

    /// <summary>Whether Windows Hello is set up.</summary>
    internal bool Available { get; set; } = true;

    /// <summary>What the person does when asked.</summary>
    internal SignOutcome Answer { get; set; } = SignOutcome.Signed;

    /// <summary>What happens while the person is being asked, before they answer.</summary>
    internal Action? WhileAsking { get; set; }

    /// <summary>How many times the person was asked.</summary>
    internal int Asked => Volatile.Read(ref _asked);

    public bool IsAvailable() => Available;

    public SignOutcome Sign(byte[] challenge, out byte[]? signature)
    {
        Interlocked.Increment(ref _asked);
        WhileAsking?.Invoke();

        signature = Answer == SignOutcome.Signed ? HMACSHA256.HashData(_key, challenge) : null;
        return Answer;
    }

    /// <summary>Windows Hello's key for keypaste made again, as resetting the PIN does, so no earlier signature comes back.</summary>
    internal void ReplaceKey() => _key = RandomNumberGenerator.GetBytes(32);
}
