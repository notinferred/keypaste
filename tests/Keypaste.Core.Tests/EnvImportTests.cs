using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A <c>.env</c> is planned by name before anything is written, and applying the plan writes every
/// new and replaced variable and nothing else (E.1b).
/// </summary>
public sealed class EnvImportTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-env-import-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_plan_names_each_key_as_new_replacing_or_unchanged_and_never_holds_a_value_in_its_keys()
    {
        using var vault = Opened(store =>
        {
            Set(store, "dev", "SAME", "same-value");
            Set(store, "dev", "OLD", "old-value");
        });
        var store = new EnvStore(vault);

        var plan = EnvImport.Plan(store, "dev", Parsed("SAME=same-value\nOLD=new-value\nFRESH=fresh-value\n"));

        Assert.Null(plan.Refusal);
        Assert.Equal(
            [
                new EnvImportKey("FRESH", EnvImportChange.New),
                new EnvImportKey("OLD", EnvImportChange.Replaces),
                new EnvImportKey("SAME", EnvImportChange.Unchanged),
            ],
            plan.Keys);
        Assert.Equal(["FRESH"], plan.Created);
        Assert.Equal(["OLD"], plan.Updated);
        Assert.Equal(1, plan.Unchanged);
        Assert.True(plan.WritesAnything);
    }

    [Fact]
    public void Applying_writes_new_and_replaced_variables_and_leaves_an_unchanged_one_without_a_revision()
    {
        using var vault = Opened(store =>
        {
            Set(store, "dev", "SAME", "same-value");
            Set(store, "dev", "OLD", "old-value");
        });
        var store = new EnvStore(vault);
        var before = vault.ReadHistory(new EntryName("env/dev", "SAME"))!.Count;
        var plan = EnvImport.Plan(store, "dev", Parsed("SAME=same-value\nOLD=new-value\nFRESH=fresh-value\n"));

        Assert.True(EnvImport.TryApply(store, plan, out var rejection), rejection);
        vault.Save();

        Assert.Equal(
            [new EnvVariable("FRESH", "fresh-value"), new EnvVariable("OLD", "new-value"), new EnvVariable("SAME", "same-value")],
            store.Read("dev"));
        Assert.Equal(before, vault.ReadHistory(new EntryName("env/dev", "SAME"))!.Count);
        Assert.Contains(vault.ReadHistory(new EntryName("env/dev", "OLD"))!, revision => revision.Fields.Password == "old-value");
    }

    [Fact]
    public void Names_that_differ_only_in_case_refuse_the_whole_import_before_anything_is_written()
    {
        using var vault = Opened(store => Set(store, "dev", "Token", "stored"));
        var store = new EnvStore(vault);

        var inFile = EnvImport.Plan(store, "dev", Parsed("KEY=a\nkey=b\n"));
        var againstVault = EnvImport.Plan(store, "dev", Parsed("NEW=a\nTOKEN=b\n"));

        Assert.Equal("the file sets both 'KEY' and 'key', which differ only in case", inFile.Refusal);
        Assert.Equal("the project already has 'Token', which differs from 'TOKEN' only in case", againstVault.Refusal);
        Assert.False(againstVault.WritesAnything);
        Assert.False(EnvImport.TryApply(store, againstVault, out _));
        Assert.Equal([new EnvVariable("Token", "stored")], store.Read("dev"));
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

    private static void Set(EnvStore store, string project, string key, string value) =>
        Assert.NotEqual(EnvSetOutcome.Rejected, store.TrySet(project, key, value, out _));

    private Vault Opened(Action<EnvStore> build)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");

        using (var created = Vault.Create(path, EnvStoreTests.MasterPassword))
        {
            build(new EnvStore(created));
            created.Save();
        }

        return Vault.Open(path, EnvStoreTests.MasterPassword);
    }
}
