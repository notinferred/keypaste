namespace Keypaste.Browser;

/// <summary>A box key pair whose secret key is held in memory the collector never moves, and zeroed on disposal.</summary>
internal sealed class BoxKeyPair : IDisposable
{
    private readonly byte[] _publicKey;

    private readonly byte[] _secretKey;

    private bool _disposed;

    internal BoxKeyPair(byte[] publicKey, byte[] secretKey)
    {
        _publicKey = publicKey;
        _secretKey = secretKey;
    }

    internal ReadOnlySpan<byte> PublicKey => _publicKey;

    internal ReadOnlySpan<byte> SecretKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _secretKey;
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_secretKey);
        _disposed = true;
    }
}
