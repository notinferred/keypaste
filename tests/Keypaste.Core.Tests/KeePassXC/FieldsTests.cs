using System.Security.Cryptography;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>The custom fields, attachments and one-time codes keypaste writes or reads are the ones KeePassXC reads or wrote.</summary>
/// <remarks>Which names keypaste refuses, what a listing shows and how a save is stamped are the tests' of those calls.</remarks>
public sealed class FieldsTests
{
    private static readonly EntryName _stripe = new("api", "Stripe");

    [Fact]
    public void A_new_field_is_protected_and_a_changed_plain_one_stays_plain_and_three_written_together_make_one_revision()
    {
        using var file = Seeded();
        using (var vault = file.Open())
        {
            Assert.True(vault.SetFields(_stripe, [new FieldWrite("STRIPE_SECRET_KEY", "sk-new"), new FieldWrite("plain", "plain-changed")]));
            vault.Save();
        }

        var entry = KeePassXcVault.Entry(file.Current(), "Stripe");
        Assert.True(KeePassXcVault.IsProtected(entry, "STRIPE_SECRET_KEY"));
        Assert.False(KeePassXcVault.IsProtected(entry, "plain"));
        Assert.Equal(["sk-new", "plain-changed"], KeePassXcCli.Show(file.Path_, "api/Stripe", "STRIPE_SECRET_KEY", "plain"));

        var before = Revisions(file);
        using (var vault = file.Open())
        {
            Assert.True(vault.SetFields(_stripe, [new FieldWrite("A", "three-a"), new FieldWrite("B", "three-b"), new FieldWrite("C", "three-c")]));
            vault.Save();
        }

        Assert.Equal(before + 1, Revisions(file));
        Assert.Equal(["three-a", "three-b", "three-c"], KeePassXcCli.Show(file.Path_, "api/Stripe", "A", "B", "C"));
    }

    [Fact]
    public void KeePassXC_reads_each_attachment_change_as_one_revision_and_its_own_attachment_stays_byte_for_byte()
    {
        using var file = Seeded();
        var own = Path.Combine(file.Directory_, "blob.bin");
        File.WriteAllBytes(own, RandomNumberGenerator.GetBytes(4096));
        KeePassXcCli.Run("attachment-import", file.Path_, "api/Stripe", "blob.bin", own);
        var added = RandomNumberGenerator.GetBytes(2048);
        var replaced = RandomNumberGenerator.GetBytes(3072);
        var before = Revisions(file);

        Change(file, vault => vault.AddAttachment(_stripe, "notes.bin", new MemoryStream(added), out _));
        Assert.Equal(added, Exported(file, "notes.bin"));
        Change(file, vault => vault.ReplaceAttachment(_stripe, "notes.bin", new MemoryStream(replaced), out _));
        Assert.Equal(replaced, Exported(file, "notes.bin"));
        Change(file, vault => vault.RemoveAttachment(_stripe, "notes.bin", out _));

        Assert.NotEqual(0, KeePassXcCli.Try(KeePassXcCli.Password, ["attachment-export", "-q", file.Path_, "api/Stripe", "notes.bin", Path.Combine(file.Directory_, "gone")]).Exit);
        Assert.Equal(before + 3, Revisions(file));
        Assert.Contains("<Key>notes.bin</Key>", KeePassXcCli.Export(file.Path_).ToString(System.Xml.Linq.SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(own), Exported(file, "blob.bin"));

        var saved = Path.Combine(file.Directory_, "saved.bin");
        using (var vault = file.Open())
        {
            Assert.Equal(AttachmentSaveOutcome.Saved, vault.SaveAttachment(_stripe, "blob.bin", saved, out var failure));
        }

        Assert.Equal(File.ReadAllBytes(own), File.ReadAllBytes(saved));
        Assert.Equal(0, File.ReadAllBytes(file.Path_)[8]);
    }

    [Fact]
    public void Keypaste_gives_the_code_KeePassXC_gives_for_every_one_time_code_form_KeePassXC_reads()
    {
        using var file = new KeePassXcVault();
        var seed = Path.Combine(file.Directory_, "totp.xml");
        File.WriteAllText(seed, """
            <?xml version="1.0" encoding="utf-8" standalone="yes"?>
            <KeePassFile><Meta><Generator>keypaste-tests</Generator></Meta><Root><Group><UUID>AAAAAAAAAAAAAAAAAAAAAQ==</UUID><Name>Root</Name>
            <Group><UUID>AAAAAAAAAAAAAAAAAAAAAg==</UUID><Name>totp</Name>
            <Entry><UUID>AAAAAAAAAAAAAAAAAAAAEA==</UUID><String><Key>Title</Key><Value>Sha1</Value></String>
              <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/Sha1?secret=JBSWY3DPEHPK3PXP&amp;period=30&amp;digits=6&amp;issuer=fields</Value></String></Entry>
            <Entry><UUID>AAAAAAAAAAAAAAAAAAAAEQ==</UUID><String><Key>Title</Key><Value>Sha256</Value></String>
              <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/Sha256?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA&amp;algorithm=SHA256&amp;digits=8</Value></String></Entry>
            <Entry><UUID>AAAAAAAAAAAAAAAAAAAAEg==</UUID><String><Key>Title</Key><Value>Steam</Value></String>
              <String><Key>otp</Key><Value ProtectInMemory="True">otpauth://totp/Steam?secret=JBSWY3DPEHPK3PXP&amp;encoder=steam&amp;digits=5</Value></String></Entry>
            <Entry><UUID>AAAAAAAAAAAAAAAAAAAAEw==</UUID><String><Key>Title</Key><Value>Pair</Value></String>
              <String><Key>TOTP Seed</Key><Value ProtectInMemory="True">JBSWY3DPEHPK3PXP</Value></String><String><Key>TOTP Settings</Key><Value>30;8</Value></String></Entry>
            <Entry><UUID>AAAAAAAAAAAAAAAAAAAAFA==</UUID><String><Key>Title</Key><Value>PairSteam</Value></String>
              <String><Key>TOTP Seed</Key><Value ProtectInMemory="True">JBSWY3DPEHPK3PXP</Value></String><String><Key>TOTP Settings</Key><Value>30;S</Value></String></Entry>
            <Entry><UUID>AAAAAAAAAAAAAAAAAAAAFQ==</UUID><String><Key>Title</Key><Value>KeeOtp</Value></String>
              <String><Key>otp</Key><Value ProtectInMemory="True">key=JBSWY3DPEHPK3PXP&amp;size=8&amp;step=30</Value></String></Entry>
            <Entry><UUID>AAAAAAAAAAAAAAAAAAAAFg==</UUID><String><Key>Title</Key><Value>KeePass2</Value></String>
              <String><Key>TimeOtp-Secret-Base32</Key><Value ProtectInMemory="True">JBSWY3DPEHPK3PXP</Value></String>
              <String><Key>TimeOtp-Algorithm</Key><Value>HMAC-SHA-256</Value></String><String><Key>TimeOtp-Length</Key><Value>7</Value></String></Entry>
            </Group></Group></Root></KeePassFile>
            """);
        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{KeePassXcCli.Password}", ["import", "-q", "-p", seed, file.Path_]).Exit);

        // KeePassXC reads KeePass 2's TimeOtp-* fields from 2.7.10 on; before that it gives that item no code at all.
        var readsTimeOtp = KeePassXcCli.Version >= new Version(2, 7, 10);
        using var vault = file.Open();
        foreach (var title in new[] { "Sha1", "Sha256", "Steam", "Pair", "PairSteam", "KeeOtp" })
        {
            KeePassXcTotp.AssertSameCode(vault, new EntryName("totp", title), file.Path_);
        }

        if (readsTimeOtp)
        {
            KeePassXcTotp.AssertSameCode(vault, new EntryName("totp", "KeePass2"), file.Path_);
        }
        else
        {
            Assert.NotEqual(0, KeePassXcCli.Try(KeePassXcCli.Password, ["show", "-q", "-t", file.Path_, "totp/KeePass2"]).Exit);
        }
    }

    private static KeePassXcVault Seeded()
    {
        var file = new KeePassXcVault();
        using var vault = file.Create();
        vault.AddEntry(new VaultEntry { GroupPath = "api", Title = "Stripe", Password = "stripe-pw" });
        vault.Save();
        vault.SetFields(_stripe, [new FieldWrite("plain", "plain-value", Protect: false)]);
        vault.Save();
        return file;
    }

    private static void Change(KeePassXcVault file, Func<Vault, bool> change)
    {
        using var vault = file.Open();
        Assert.True(change(vault));
        vault.Save();
    }

    private static int Revisions(KeePassXcVault file) =>
        KeePassXcCli.Export(file.Path_).Descendants("Entry")
            .First(e => KeePassXcVault.StringValue(e, "Title") == "Stripe")
            .Element("History")?.Elements("Entry").Count() ?? 0;

    private static byte[] Exported(KeePassXcVault file, string attachment)
    {
        var path = Path.Combine(file.Directory_, $"{attachment}.{Guid.NewGuid():n}");
        KeePassXcCli.Run("attachment-export", file.Path_, "api/Stripe", attachment, path);
        return File.ReadAllBytes(path);
    }
}
