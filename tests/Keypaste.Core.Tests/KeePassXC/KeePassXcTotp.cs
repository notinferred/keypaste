using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>Compares keypaste's one-time code for an item with the one <c>keepassxc-cli show --totp</c> computes from the same file.</summary>
internal static class KeePassXcTotp
{
    /// <summary>Asserts both give the same code, read within one 30-second period, and returns it.</summary>
    internal static string AssertSameCode(Vault vault, EntryName name, string path)
    {
        using var spec = vault.ReadTotp(name, out var refusal) ?? throw new InvalidOperationException(refusal);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var period = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            var ours = spec.Code(DateTimeOffset.UtcNow).Value;
            var theirs = KeePassXcCli.Run("show", path, $"{name.GroupPath}/{name.Title}", "-t").TrimEnd('\n');
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 == period)
            {
                Assert.Equal(theirs, ours);
                return ours;
            }
        }

        Assert.Fail("keypaste's and KeePassXC's codes could not be read within one period twice running");
        return "";
    }
}
