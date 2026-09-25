namespace Keypaste.Core.Clients;

/// <summary>
/// <c>clients.toml</c> as the vault's owner reads it at each request: re-parsed whenever its bytes
/// change, so an edit applies to the next request with no restart (D-0360).
/// </summary>
/// <remarks>
/// The file is hashed on every request rather than trusted to change its length or write time,
/// which a same-length edit inside the timestamp's granularity would not. A file that cannot be read
/// or parsed answers nothing: a policy a person wrote narrower must not silently widen.
/// </remarks>
/// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
public sealed class ClientPolicySource(string path)
{
    private const int _maximumBytes = 64 * 1024;

    private readonly Lock _gate = new();
    private byte[]? _digest;
    private ClientPolicies? _policies;
    private string _problem = string.Empty;

    /// <summary>The file read.</summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The policy a bridge with this label is held to now.</summary>
    /// <param name="label">The raw label, or null.</param>
    /// <param name="policy">Its policy, when the file could be used.</param>
    /// <param name="problem">Why it could not be, otherwise empty.</param>
    /// <returns>Whether the file is absent or well formed.</returns>
    public bool TryFor(string? label, out ClientPolicy policy, out string problem)
    {
        policy = ClientPolicy.SessionGrants;

        lock (_gate)
        {
            if (!TryRead(out problem))
            {
                return false;
            }

            policy = _policies!.For(label);
            return true;
        }
    }

    /// <summary>Every row in force now.</summary>
    /// <param name="policies">The rows, when the file could be used.</param>
    /// <param name="problem">Why it could not be, otherwise empty.</param>
    /// <returns>Whether the file is absent or well formed.</returns>
    public bool TryCurrent(out ClientPolicies? policies, out string problem)
    {
        lock (_gate)
        {
            policies = TryRead(out problem) ? _policies : null;
            return policies is not null;
        }
    }

    private bool TryRead(out string problem)
    {
        byte[] bytes;

        try
        {
            if (!File.Exists(Path))
            {
                _digest = null;
                _policies = ClientPolicies.Empty;
                _problem = string.Empty;
                problem = string.Empty;
                return true;
            }

            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length > _maximumBytes)
            {
                return Refuse($"{Path}: the file is larger than {_maximumBytes} bytes", out problem);
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refuse($"{Path}: it could not be read: {ex.Message}", out problem);
        }

        var digest = SHA256.HashData(bytes);

        if (_digest is not null && CryptographicOperations.FixedTimeEquals(digest, _digest))
        {
            problem = _problem;
            return _policies is not null;
        }

        _digest = digest;

        if (ClientPolicies.TryParse(bytes, out var parsed, out var parseProblem))
        {
            _policies = parsed;
            _problem = string.Empty;
            problem = string.Empty;
            return true;
        }

        _policies = null;
        _problem = $"{Path}: {parseProblem}";
        problem = _problem;
        return false;
    }

    private bool Refuse(string why, out string problem)
    {
        _digest = null;
        _policies = null;
        _problem = why;
        problem = why;
        return false;
    }
}
