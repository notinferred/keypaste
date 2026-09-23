using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Ownership;

/// <summary>
/// One process's exclusive hold on a vault, taken before the vault is opened and kept while it is
/// unlocked.
/// </summary>
/// <remarks>
/// <para>
/// The open <c>.lock</c> handle is the claim, as with the audit log's sidecar: the operating system
/// closes it when the holder dies, so a crash leaves nothing to clean up. The <c>.owner</c> file
/// beside it only names the holder for a refusal and is never trusted to decide anything.
/// </para>
/// <para>
/// Claims live under keypaste's home, so a process given a different <c>KEYPASTE_HOME</c> is a
/// separate installation that does not see them (THREATS.md T-29).
/// </para>
/// </remarks>
public sealed class VaultClaim : IDisposable
{
    /// <summary>The directory under keypaste's home that holds claims.</summary>
    public const string DirectoryName = "sessions";

    private static readonly UnixFileMode _ownerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly UnixFileMode _ownerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly FileStream _lock;
    private readonly string _ownerPath;
    private bool _disposed;

    private VaultClaim(FileStream held, string ownerPath, VaultIdentity vault, VaultOwner owner)
    {
        _lock = held;
        _ownerPath = ownerPath;
        Vault = vault;
        Owner = owner;
    }

    /// <summary>The vault this claim holds.</summary>
    public VaultIdentity Vault { get; }

    /// <summary>This process, as another one is told about it.</summary>
    public VaultOwner Owner { get; }

    /// <summary>Takes the claim on a vault, or says who holds it.</summary>
    /// <param name="home">keypaste's home directory.</param>
    /// <param name="vaultPath">The vault about to be opened or created.</param>
    /// <param name="kind">What kind of process this is.</param>
    /// <param name="claim">The claim, when it was free.</param>
    /// <param name="refusal">Why not, as a sentence naming the holder when one is known.</param>
    /// <returns><see langword="true"/> when this process now holds the vault.</returns>
    /// <remarks>A claim that cannot be recorded at all is refused rather than skipped.</remarks>
    public static bool TryAcquire(
        string home,
        string vaultPath,
        OwnerKind kind,
        [NotNullWhen(true)] out VaultClaim? claim,
        [NotNullWhen(false)] out string? refusal)
    {
        claim = null;
        refusal = null;

        var vault = VaultIdentity.Of(home, vaultPath);
        var directory = Path.Combine(home, DirectoryName);
        var lockPath = Path.Combine(directory, vault.Key + ".lock");
        var ownerPath = Path.Combine(directory, vault.Key + ".owner");

        FileStream? held = null;

        try
        {
            CreateDirectory(directory);

            try
            {
                held = new FileStream(lockPath, Options(FileMode.OpenOrCreate, FileAccess.Write, FileShare.None));
            }
            catch (IOException) when (File.Exists(lockPath))
            {
                refusal = $"this vault is already unlocked in {Holder(ownerPath)}. Lock it there first.";
                return false;
            }

            var owner = new VaultOwner(kind, Environment.ProcessId, vault.Path);

            using (var stream = new FileStream(ownerPath, Options(FileMode.Create, FileAccess.Write, FileShare.Read)))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(Describe(owner));
            }

            claim = new VaultClaim(held, ownerPath, vault, owner);
            held = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusal = $"keypaste could not record that this process holds the vault, so it was not opened: {ex.Message}";
            return false;
        }
        finally
        {
            held?.Dispose();
        }
    }

    /// <summary>Releases the vault for another process.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The name goes before the hold, so a process that takes the claim next never has its own
        // description deleted.
        try
        {
            File.Delete(_ownerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale description names nobody once the lock is free: the next holder overwrites it.
        }

        _lock.Dispose();
    }

    private static string Describe(VaultOwner owner) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"kind={owner.Kind}\npid={owner.ProcessId}\nvault={owner.VaultPath}\n");

    private static string Holder(string ownerPath)
    {
        try
        {
            OwnerKind? kind = null;
            int? pid = null;
            var vault = string.Empty;

            foreach (var line in File.ReadAllLines(ownerPath))
            {
                var separator = line.IndexOf('=', StringComparison.Ordinal);

                if (separator < 0)
                {
                    continue;
                }

                var value = line[(separator + 1)..];

                switch (line[..separator])
                {
                    case "kind" when Enum.TryParse<OwnerKind>(value, out var parsed):
                        kind = parsed;
                        break;
                    case "pid" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number):
                        pid = number;
                        break;
                    case "vault":
                        vault = value;
                        break;
                }
            }

            return kind is { } k && pid is { } p ? new VaultOwner(k, p, vault).Describe() : VaultOwner.Unknown;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return VaultOwner.Unknown;
        }
    }

    private static FileStreamOptions Options(FileMode mode, FileAccess access, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = access, Share = share };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = _ownerOnlyFile;
        }

        return options;
    }

    private static void CreateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, _ownerOnlyDirectory);
        }
    }
}
