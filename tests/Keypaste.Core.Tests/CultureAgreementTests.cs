using System.Globalization;
using System.Reflection;
using System.Text;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Keypaste.Core.Tokens;
using Keypaste.Infrastructure;
using Xunit;
using static System.FormattableString;

namespace Keypaste.Core.Tests;

/// <summary>
/// A rule, a name and a file mean the same in the desktop, which has a culture and ICU, and the CLI, which runs invariant.
/// </summary>
/// <remarks>
/// The expected values here are literal, and <c>verify.sh</c> runs this class both invariant and with ICU under Turkish, German and Arabic, so both modes answer to one list.
/// A test that writes under one culture and reads under another needs ICU, and is skipped without it.
/// </remarks>
public sealed class CultureAgreementTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-culture-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static bool HasIcu
    {
        get
        {
            try
            {
                return CultureInfo.GetCultureInfo("tr-TR").Name.Length > 0;
            }
            catch (CultureNotFoundException)
            {
                return false;
            }
        }
    }

    private static T Under<T>(string culture, Func<T> act)
    {
        var (before, beforeUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);

        try
        {
            return act();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (before, beforeUi);
        }
    }

    [Fact]
    public void A_culture_named_for_this_run_is_the_one_in_force()
    {
        if (Environment.GetEnvironmentVariable(TestCulture.Variable) is { Length: > 0 } named)
        {
            Assert.Equal(named, CultureInfo.CurrentCulture.Name, StringComparer.Ordinal);
        }
    }

    [Theory]
    [InlineData("prod", true)]
    [InlineData("PROD", true)]
    [InlineData("production", true)]
    [InlineData("PRODUCTION", true)]
    [InlineData("Prod-EU", true)]
    [InlineData("PROD-INDIA", true)]
    [InlineData("staging", false)]
    [InlineData("PRODUCTİON", false)]
    [InlineData("productıon", false)]
    public void A_protected_environment_is_the_same_name_under_every_culture(string name, bool isProtected) =>
        Assert.Equal(isProtected, EnvProfileNames.IsProtected(name));

    [Theory]
    [InlineData("LD_PRELOAD", true)]
    [InlineData("ld_preload", true)]
    [InlineData("pythonpath", true)]
    [InlineData("PYTHONPATH", true)]
    [InlineData("http_proxy", true)]
    [InlineData("HTTP_PROXY", true)]
    [InlineData("git_ssh_command", true)]
    [InlineData("perl5opt", true)]
    [InlineData("bash_env", true)]
    [InlineData("baſh_env", false)]
    [InlineData("gıt_ssh_command", false)]
    [InlineData("PATH", false)]
    public void A_variable_that_changes_how_programs_start_is_named_alike(string name, bool changes) =>
        Assert.Equal(changes, RunRequestRules.ChangesHowProgramsStart(name));

    [Fact]
    public void Upper_case_leaves_the_long_s_and_the_dotless_i_alone()
    {
        Assert.Equal("ſ", InvariantUpper.Of("ſ"), StringComparer.Ordinal);
        Assert.Equal("TITLE", InvariantUpper.Of("title"), StringComparer.Ordinal);
        Assert.Equal("ı", InvariantUpper.Of("ı"), StringComparer.Ordinal);
    }

    [Fact]
    public void Only_the_kelvin_sign_and_nothing_else_outside_ascii_folds_into_ascii()
    {
        var into = new List<int>();

        for (var c = 0x80; c < 0x10000; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var text = ((char)c).ToString();

            if (char.IsAscii(InvariantUpper.Of((char)c)) || char.IsAscii(text.ToLowerInvariant()[0]))
            {
                into.Add(c);
            }

            for (var ascii = 'A'; ascii <= 'Z'; ascii++)
            {
                if (string.Equals(text, ascii.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Fail(Invariant($"U+{c:X4} equals {ascii} ignoring case"));
                }
            }
        }

        Assert.Equal([0x212A], into);
    }

    [Fact]
    public void A_name_rule_answers_alike()
    {
        Assert.True(TagRules.IsValid("env:INDIA:prod", out _));
        Assert.True(ProjectTag.TryFor("title", "prod", out var tag, out _));
        Assert.Equal("env:title:prod", tag, StringComparer.Ordinal);
        Assert.True(FieldNameRules.IsStandard("TITLE"));
        Assert.True(FieldNameRules.IsStandard("title"));
        Assert.False(FieldNameRules.IsStandard("tıtle"));
        Assert.True(KpReferences.TryParse(KpReferences.For(new EntryName("INFRA", "TITLE"), "password"), out var reference, out var error), error);
        Assert.Equal("password", Assert.IsType<EntryReference>(reference).Field, StringComparer.Ordinal);
        Assert.Equal(new EntryName("INFRA", "TITLE"), EntryName.Parse("INFRA/TITLE"));
    }

    [Fact]
    public void Toml_and_dotenv_read_the_same()
    {
        var text = "[[window]]\nwidth = 1280\nname = \"İI\"\n";
        Assert.True(Toml.TryParse(text, out var document, out var error), error);
        Assert.Equal(1280, Assert.Single(document.Tables).Pairs.Single(pair => pair.Key == "width").Value.Number);

        Assert.True(DotEnv.TryParse("TITLE=value\nindia=1\n", out var env));
        Assert.Equal(["TITLE", "india"], env.Variables.Select(variable => variable.Key));
    }

    [Fact]
    public void Dates_kept_in_a_kdbx_3_file_read_as_written()
    {
        var timeUtil = Assembly.Load("KeePassLib").GetType("KeePassLib.Utility.TimeUtil")!;
        object?[] arguments = ["2026-11-05T10:30:00Z", null];

        Assert.True((bool)timeUtil.GetMethod("TryDeserializeUtc")!.Invoke(null, arguments)!);

        var read = (DateTime)arguments[1]!;
        Assert.Equal(new DateTime(2026, 11, 5, 10, 30, 0, DateTimeKind.Utc), read);
        Assert.Equal("2026-11-05T10:30:00Z", timeUtil.GetMethod("SerializeUtc")!.Invoke(null, [read]));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    [InlineData("fa-IR")]
    public void An_audit_log_is_byte_identical_and_its_chain_holds(string culture)
    {
        Assert.SkipUnless(HasIcu, "needs ICU");

        var invariant = Under(string.Empty, () => WriteLog("invariant.jsonl"));
        var cultured = Under(culture, () => WriteLog(culture + ".jsonl"));

        Assert.Equal(invariant, cultured);
        Assert.True(Under(culture, () =>
        {
            Assert.True(AuditReader.TryRead(Path.Combine(_directory, culture + ".jsonl"), out var entries, out var unreadable, out var failure), failure);
            Assert.Equal(0, unreadable);
            return entries.Count == 3 && entries[0].At == new DateTimeOffset(2026, 7, 26, 14, 3, 11, 482, TimeSpan.Zero);
        }));
    }

    private byte[] WriteLog(string name)
    {
        var path = Path.Combine(_directory, name);
        Assert.True(AuditLog.TryOpen(path, new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 3, 11, 482, TimeSpan.Zero)), out var log, out var error), error);

        using (log)
        {
            foreach (var (client, seconds) in new[] { ("TITLE", 30), ("india", 3600), ("İı", 90061) })
            {
                Assert.True(log!.TryAppend(
                    new AuditRecord
                    {
                        Tool = "request_credential",
                        Client = new AuditClient(client, "1.2.3", "work"),
                        Args = AuditArgs.ForCredentialRequest("env/INDIA/Title", "Password", "for -1.5"),
                        Decision = AuditDecision.Granted,
                        Method = AuditMethod.Exposure,
                        Reason = Invariant($"released for {seconds}s"),
                        Exposure = ["env/**"],
                    },
                    out error), error);
            }
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_saved_entrys_tags_keep_one_order_whatever_culture_wrote_them(string writer)
    {
        Assert.SkipUnless(HasIcu, "needs ICU");

        var path = Path.Combine(_directory, "vault.kdbx");
        var name = new EntryName("INFRA", "TITLE");

        Under(writer, () =>
        {
            using var vault = Vault.Create(path, EnvStoreTests.MasterPassword);
            vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = "s3cret" });
            Assert.True(vault.AddTag(name, "\u00E4"));
            Assert.True(vault.AddTag(name, "z"));
            vault.Save();
            return true;
        });

        foreach (var reader in new[] { writer, string.Empty })
        {
            var tags = Under(reader, () =>
            {
                using var vault = Vault.Open(path, EnvStoreTests.MasterPassword);
                return vault.Tags(name);
            });

            Assert.Equal(["z", "\u00E4"], tags);
        }
    }

    [Theory]
    [InlineData("tr-TR", "ar-SA")]
    [InlineData("ar-SA", "de-DE")]
    [InlineData("de-DE", "th-TH")]
    [InlineData("th-TH", "tr-TR")]
    public void A_vault_and_a_token_written_under_one_culture_read_back_under_another(string writer, string reader)
    {
        Assert.SkipUnless(HasIcu, "needs ICU");

        var path = Path.Combine(_directory, "vault.kdbx");
        var now = new DateTimeOffset(2026, 7, 26, 14, 3, 11, TimeSpan.Zero);
        var expires = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var name = new EntryName("INFRA", "TITLE");

        var token = Under(writer, () =>
        {
            using var vault = Vault.Create(path, EnvStoreTests.MasterPassword);
            vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = "s3cret", Username = "İI" });
            vault.SetExpiryUnchecked(name, expires);

            Assert.True(TokenScope.TryParse("read:acme-api/staging/*", out var scope, out _));
            Assert.True(new TokenStore(vault).TryCreate("ci", [scope], TimeSpan.FromDays(30), allowProd: false, now, out _, out var created, out var error), error);
            vault.Save();
            return created;
        });

        Under(reader, () =>
        {
            using var vault = Vault.Open(path, EnvStoreTests.MasterPassword);
            var entry = vault.Find(name);
            Assert.NotNull(entry);
            Assert.Equal("s3cret", entry.Password, StringComparer.Ordinal);
            Assert.Equal("İI", entry.Username, StringComparer.Ordinal);
            Assert.Equal(expires, entry.Expires);
            Assert.Equal(TokenCheck.Valid, new TokenStore(vault).Verify(token, now.AddDays(29), out _));
            return true;
        });
    }

    [Theory]
    [InlineData("de-DE", "ar-SA")]
    [InlineData("ar-SA", "tr-TR")]
    [InlineData("tr-TR", "de-DE")]
    public void An_app_toml_written_under_one_culture_reads_back_under_another(string writer, string reader)
    {
        Assert.SkipUnless(HasIcu, "needs ICU");

        var path = Path.Combine(_directory, "app.toml");
        var settings = AppSettings.Default with { Window = new WindowLayout(-12, -7, 1280, 800, WindowMode.Normal, SidebarHidden: false, ZoomPercent: 150) };

        var written = Under(writer, () =>
        {
            Assert.True(AppSettings.Save(path, settings));
            return File.ReadAllText(path, Encoding.UTF8);
        });

        Assert.Contains("-12", written, StringComparison.Ordinal);
        Assert.Equal(settings.Window, Under(reader, () => AppSettings.Load(path)).Window);
    }
}
