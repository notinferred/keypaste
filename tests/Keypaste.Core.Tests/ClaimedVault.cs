using System.Globalization;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Ownership;

namespace Keypaste.Core.Tests;

/// <summary>Opens, creates and restores vaults for writing under a claim taken in a home of the tests' own.</summary>
/// <remarks>
/// A claim on a file another live one already holds moves to the next home, so two opens of one file in a test meet as
/// keypaste under two homes does, never refusing each other. A claim is never released: it lasts as long as its vault,
/// and the runtime closes its lock once nothing references either. The homes are removed when the test process ends.
/// </remarks>
internal static class ClaimedVault
{
    private const int _homes = 1000;

    private static readonly string _root = Created();

    /// <summary>A claim on <paramref name="path"/> in the first of the tests' homes where nothing holds it.</summary>
    /// <param name="path">The vault, which need not exist yet.</param>
    internal static VaultClaim Claim(string path)
    {
        var refusal = string.Empty;

        for (var home = 0; home < _homes; home++)
        {
            if (VaultClaim.TryAcquire(Path.Combine(_root, home.ToString(CultureInfo.InvariantCulture)), path, OwnerKind.CommandLine, out var claim, out refusal))
            {
                return claim;
            }
        }

        throw new InvalidOperationException(refusal);
    }

    // Each claim lasts as long as the vault it was taken for, which the caller disposes.
#pragma warning disable CA2000
    internal static Vault Create(string path, ReadOnlySpan<char> masterPassword) =>
        Vault.Create(Claim(path), path, masterPassword);

    internal static Vault CreateWith(string path, ReadOnlySpan<char> masterPassword, string? keyfilePath, HardwareKey? hardwareKey = null) =>
        Vault.CreateWith(Claim(path), path, masterPassword, keyfilePath, hardwareKey);

    internal static Vault Open(string path, ReadOnlySpan<char> masterPassword) =>
        Vault.Open(Claim(path), path, masterPassword);

    internal static Vault Open(string path, ReadOnlySpan<char> masterPassword, string? keyfilePath) =>
        Vault.Open(Claim(path), path, masterPassword, keyfilePath);

    internal static Vault Open(string path, ReadOnlySpan<char> masterPassword, string? keyfilePath, HardwareKey? hardwareKey) =>
        Vault.Open(Claim(path), path, masterPassword, keyfilePath, hardwareKey);

    internal static Vault Open(string path, VaultKeyParts parts, HardwareKey? hardwareKey) =>
        Vault.Open(Claim(path), path, parts, hardwareKey);

    internal static VaultCreationOutcome TryCreate(
        string path, ReadOnlySpan<char> password, ReadOnlySpan<char> confirmation, out Vault? created, out string failure) =>
        VaultCreation.TryCreate(Claim(path), path, password, confirmation, out created, out failure);

    internal static VaultCreationOutcome TryCreate(
        string path, ReadOnlySpan<char> password, ReadOnlySpan<char> confirmation, string? keyfilePath, out Vault? created, out string failure) =>
        VaultCreation.TryCreate(Claim(path), path, password, confirmation, keyfilePath, out created, out failure);

    internal static VaultCreationOutcome TryCreate(
        string path,
        ReadOnlySpan<char> password,
        ReadOnlySpan<char> confirmation,
        string? keyfilePath,
        HardwareKey? hardwareKey,
        out Vault? created,
        out string failure) =>
        VaultCreation.TryCreate(Claim(path), path, password, confirmation, keyfilePath, hardwareKey, out created, out failure);
#pragma warning restore CA2000

    internal static VaultRestoreReport Restore(VaultBackupSummary validated) =>
        Restore(validated, beforeReplacing: null, waitBetweenAttempts: null);

    internal static VaultRestoreReport Restore(
        VaultBackupSummary validated, Action<string>? beforeReplacing, Action<int>? waitBetweenAttempts)
    {
        using var claim = Claim(validated.VaultPath);
        return VaultBackups.Restore(claim, validated, beforeReplacing, waitBetweenAttempts);
    }

    private static string Created()
    {
        var root = Directory.CreateTempSubdirectory("keypaste-test-claims-").FullName;

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A lock still open on Windows keeps its home until the runner is cleaned.
            }
        };

        return root;
    }
}
