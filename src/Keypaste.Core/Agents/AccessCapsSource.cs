using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Agents;

/// <summary>
/// <c>clients.toml</c> as the vault's owner reads it at each request: re-parsed whenever its bytes
/// change, so an edit applies to the next request with no restart.
/// </summary>
/// <remarks>
/// The file is hashed on every request rather than trusted to change its length or write time, which a
/// same-length edit inside the timestamp's granularity would not. A file that cannot be read or parsed
/// answers nothing, so every agent request is refused: a ceiling a person wrote must not silently lift.
/// </remarks>
/// <param name="path">From <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
public sealed class AccessCapsSource(string path)
{
    private readonly Lock _gate = new();
    private byte[]? _digest;
    private AccessCaps? _caps;
    private string _problem = string.Empty;

    /// <summary>The file read.</summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>Every ceiling in force now.</summary>
    /// <param name="caps">The ceilings, when the file could be used; <see cref="AccessCaps.Empty"/> when there is none.</param>
    /// <param name="problem">Why it could not be, otherwise empty.</param>
    /// <returns>Whether the file is absent or well formed.</returns>
    public bool TryCurrent([NotNullWhen(true)] out AccessCaps? caps, out string problem)
    {
        lock (_gate)
        {
            caps = TryRead(out problem) ? _caps : null;
            return caps is not null;
        }
    }

    private bool TryRead(out string problem)
    {
        byte[] bytes;

        try
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length > AccessCaps.MaximumBytes)
            {
                return Refuse($"{Path}: the file is larger than {AccessCaps.MaximumBytes} bytes", out problem);
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Only a path that names nothing is absent; anything else there that cannot be read refuses.
            _digest = null;
            _caps = AccessCaps.Empty;
            _problem = string.Empty;
            problem = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refuse($"{Path}: it could not be read: {ex.Message}", out problem);
        }

        var digest = SHA256.HashData(bytes);

        if (_digest is not null && CryptographicOperations.FixedTimeEquals(digest, _digest))
        {
            problem = _problem;
            return _caps is not null;
        }

        _digest = digest;

        if (AccessCaps.TryParse(bytes, out var parsed, out var parseProblem))
        {
            _caps = parsed;
            _problem = string.Empty;
            problem = string.Empty;
            return true;
        }

        _caps = null;
        _problem = $"{Path}: {parseProblem}";
        problem = _problem;
        return false;
    }

    private bool Refuse(string why, out string problem)
    {
        _digest = null;
        _caps = null;
        _problem = why;
        problem = why;
        return false;
    }
}
