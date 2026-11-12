using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>An env set KeePassXC made is refused whole while an entry in it has expired, and released as exactly what is left once KeePassXC removes it.</summary>
/// <remarks>Expiry can only be written by KeePassXC, through <c>keepassxc-cli import</c>. What a child receives is <c>verify-run-injection.sh</c>'s.</remarks>
public sealed class RunTests
{
    [Fact]
    public void A_set_with_an_entry_KeePassXC_expired_is_refused_naming_only_it_and_the_rest_is_released_once_KeePassXC_removes_it()
    {
        using var file = new KeePassXcVault();
        var xml = Path.Combine(file.Directory_, "run.xml");
        File.WriteAllText(xml, $"""
            <?xml version="1.0" encoding="utf-8" standalone="yes"?>
            <KeePassFile><Meta><Generator>keypaste-tests</Generator><RecycleBinEnabled>True</RecycleBinEnabled></Meta><Root>
            <Group><UUID>AAAAAAAAAAAAAAAAAAAAAQ==</UUID><Name>Root</Name>
            <Group><UUID>AAAAAAAAAAAAAAAAAAAAAw==</UUID><Name>gate</Name>
            {Entry("AAAAAAAAAAAAAAAAAAAAEA==", "Valid", "VALID", "valid-value", expires: "2999-01-01T00:00:00Z")}
            {Entry("AAAAAAAAAAAAAAAAAAAAEQ==", "Expired", "EXPIRED", "expired-value", expires: "2020-01-02T03:04:05Z")}
            {Entry("AAAAAAAAAAAAAAAAAAAAEg==", "Odd", "bad-name", "badname-value", expires: null)}
            {Entry("AAAAAAAAAAAAAAAAAAAAEw==", "Recycled", "RECYCLED", "recycled-value", expires: null)}
            </Group></Group></Root></KeePassFile>
            """);
        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{KeePassXcCli.Password}", ["import", "-q", "-p", xml, file.Path_]).Exit);
        KeePassXcCli.Run("rm", file.Path_, "gate/Recycled");

        using (var vault = file.Open())
        {
            var refused = EnvResolution.Resolve(vault, "gate", TimeProvider.System);

            Assert.Equal(EnvOutcome.Unusable, refused.Outcome);
            var problem = Assert.Single(refused.Problems);
            Assert.Equal("EXPIRED", problem.Key);
            Assert.Contains("2020-01-02 03:04:05Z", problem.Reason, StringComparison.Ordinal);
            Assert.Empty(refused.Variables);
        }

        KeePassXcCli.Run("rm", file.Path_, "gate/Expired");

        using (var vault = file.Open())
        {
            var released = EnvResolution.Resolve(vault, "gate", TimeProvider.System);

            Assert.Equal(EnvOutcome.Resolved, released.Outcome);
            Assert.Equal([new EnvVariable("VALID", "valid-value")], released.Variables);
        }
    }

    private static string Entry(string uuid, string title, string field, string value, string? expires) =>
        $"<Entry><UUID>{uuid}</UUID><Tags>env:gate</Tags><Times><Expires>{(expires is null ? "False" : "True")}</Expires>"
        + $"<ExpiryTime>{expires ?? "2999-01-01T00:00:00Z"}</ExpiryTime></Times>"
        + $"<String><Key>Title</Key><Value>{title}</Value></String>"
        + $"<String><Key>{field}</Key><Value ProtectInMemory=\"True\">{value}</Value></String></Entry>";
}
