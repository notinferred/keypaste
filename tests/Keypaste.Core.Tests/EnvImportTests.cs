using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A <c>.env</c> is planned by name before anything is written: a key the environment has is
/// replaced where it lives, a new one goes on the home entry or the entry named, and applying the
/// plan writes each entry once and nothing else (E.1b, D-0413).
/// </summary>
public sealed class EnvImportTests : IDisposable
{
    private static readonly EntryName _home = new("env/dev", ".env");
    private static readonly EntryName _stripe = new("services", "Stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-env-import-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_plan_names_each_key_with_its_entry_as_new_replacing_or_unchanged()
    {
        using var vault = Opened(created =>
        {
            ProjectVariables.Set(created, "dev", "SAME", "same-value");
            ProjectVariables.Set(created, "dev", "OLD", "old-value");
            TaggedStripe(created);
        });

        var plan = EnvImport.Plan(new EnvStore(vault), "dev", Parsed("SAME=same-value\nOLD=new-value\nSTRIPE_KEY=sk-new\nFRESH=fresh-value\n"));

        Assert.Null(plan.Refusal);
        Assert.Equal(
            [
                new EnvKeyWrite("FRESH", EnvWriteChange.New, _home, "FRESH"),
                new EnvKeyWrite("OLD", EnvWriteChange.Replaces, _home, "OLD"),
                new EnvKeyWrite("SAME", EnvWriteChange.Unchanged, _home, "SAME"),
                new EnvKeyWrite("STRIPE_KEY", EnvWriteChange.Replaces, _stripe, "STRIPE_KEY"),
            ],
            plan.Keys);
        Assert.Equal(["FRESH"], plan.Created);
        Assert.Equal(["OLD", "STRIPE_KEY"], plan.Updated);
        Assert.Equal(1, plan.Unchanged);
        Assert.False(plan.CreatesHome);
        Assert.True(plan.WritesAnything);
    }

    [Fact]
    public void Applying_writes_each_entry_once_and_leaves_an_unchanged_one_without_a_revision()
    {
        using var vault = Opened(created =>
        {
            ProjectVariables.Set(created, "dev", "SAME", "same-value");
            TaggedStripe(created);
        });
        var store = new EnvStore(vault);
        var home = vault.ReadHistory(_home)!.Count;
        var stripe = vault.ReadHistory(_stripe)!.Count;
        var plan = EnvImport.Plan(store, "dev", "dev", Parsed("SAME=same-value\nSTRIPE_KEY=sk-new\nSTRIPE_WEBHOOK=whsec-new\nFRESH=fresh-value\n"), _stripe);

        Assert.True(store.TryApply(plan, out var rejection), rejection);
        vault.Save();

        Assert.Equal(
            [
                new EnvVariable("FRESH", "fresh-value"),
                new EnvVariable("SAME", "same-value"),
                new EnvVariable("STRIPE_KEY", "sk-new"),
                new EnvVariable("STRIPE_WEBHOOK", "whsec-new"),
            ],
            EnvResolution.Resolve(vault, "dev", TimeProvider.System).Variables);
        Assert.Equal(home, vault.ReadHistory(_home)!.Count);
        Assert.Equal(stripe + 1, vault.ReadHistory(_stripe)!.Count);
        Assert.Equal(["FRESH", "STRIPE_KEY", "STRIPE_WEBHOOK"], vault.Fields(_stripe)!.Select(field => field.Name));
        Assert.Equal(["SAME"], vault.Fields(_home)!.Select(field => field.Name));
    }

    [Fact]
    public void Names_that_differ_only_in_case_refuse_the_whole_import_before_anything_is_written()
    {
        using var vault = Opened(created => ProjectVariables.Set(created, "dev", "TOKEN", "stored"));
        var store = new EnvStore(vault);

        var inFile = EnvImport.Plan(store, "dev", Parsed("KEY=a\nkey=b\n"));
        var againstVault = EnvImport.Plan(store, "dev", Parsed("NEW=a\nToken=b\n"));

        Assert.Equal("the file sets both 'KEY' and 'key', which differ only in case", inFile.Refusal);
        Assert.Equal("'dev/dev' already has 'TOKEN', which differs from 'Token' only in case (env/dev/.env)", againstVault.Refusal);
        Assert.False(againstVault.WritesAnything);
        Assert.False(store.TryApply(againstVault, out _));
        Assert.Equal([new EnvVariable("TOKEN", "stored")], EnvResolution.List(vault, "dev", "dev").Variables);
    }

    [Fact]
    public void A_new_key_no_field_can_be_named_refuses_the_whole_import()
    {
        using var vault = Opened(_ => { });
        var store = new EnvStore(vault);

        var plan = EnvImport.Plan(store, "dev", Parsed("GOOD=1\nlower_case=2\n"));

        Assert.StartsWith("'lower_case' cannot be a project's variable", plan.Refusal, StringComparison.Ordinal);
        Assert.False(plan.WritesAnything);
        Assert.Empty(vault.ReadEntries());
    }

    [Fact]
    public void A_reference_file_from_env_export_is_refused_and_the_vault_keeps_its_values()
    {
        using var vault = Opened(created =>
        {
            ProjectVariables.Set(created, "dev", "API_KEY", "real-key");
            ProjectVariables.Set(created, "dev", "DATABASE_URL", "postgres://real");
        });
        var store = new EnvStore(vault);
        var exported = EnvReferenceFile.Format("dev", EnvProfileNames.Default, ["API_KEY", "DATABASE_URL"]);

        var plan = EnvImport.Plan(store, "dev", Parsed(exported));

        Assert.NotNull(plan.Refusal);
        Assert.Contains(EnvReferenceFile.FileName, plan.Refusal, StringComparison.Ordinal);
        Assert.False(plan.WritesAnything);
        Assert.False(store.TryApply(plan, out _));
        Assert.Equal(
            [new EnvVariable("API_KEY", "real-key"), new EnvVariable("DATABASE_URL", "postgres://real")],
            EnvResolution.List(vault, "dev", "dev").Variables);
    }

    [Fact]
    public void A_file_with_problems_is_never_planned()
    {
        using var vault = Opened(_ => { });
        Assert.False(DotEnv.TryParse("GOOD=1\nBAD LINE\n", out var document));

        Assert.Throws<ArgumentException>(() => EnvImport.Plan(new EnvStore(vault), "dev", document));
    }

    private static DotEnvDocument Parsed(string text)
    {
        Assert.True(DotEnv.TryParse(text, out var document));
        return document;
    }

    private static void TaggedStripe(Vault vault)
    {
        vault.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = "login" });
        Assert.True(vault.SetFields(_stripe, [new FieldWrite("STRIPE_KEY", "sk-old")]));
        Assert.True(vault.AddTag(_stripe, "env:dev"));
    }

    private Vault Opened(Action<Vault> build)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");

        using (var created = Vault.Create(path, EnvStoreTests.MasterPassword))
        {
            build(created);
            created.Save();
        }

        return Vault.Open(path, EnvStoreTests.MasterPassword);
    }
}
