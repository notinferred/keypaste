using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Projects;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>A project's profiles on the Env Sets screen: the matrix, the profile edits go to, its references and its run command.</summary>
public sealed class EnvProfilesTests : IDisposable
{
    private const string _prodValue = "prod-value-sentinel";

    private readonly TempVault _fixture = new();
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock());

    public EnvProfilesTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            var store = new EnvStore(vault);
            store.TrySet("acme-api", "DATABASE_URL", "dev-db", out _);
            store.TrySet("acme-api", "prod", "DATABASE_URL", _prodValue, out _);
            store.TrySet("acme-api", "prod", "SENTRY_DSN", "prod-sentry", out _);
            vault.Save();
        }

        _session = new AppVaultSession(new ManualClock(), home: _fixture.Home);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    public void Dispose()
    {
        _session.Dispose();
        _countdown.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Matrix_ReflectsTheVault()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        Assert.Equal([new EnvProfileInfo("dev", false), new EnvProfileInfo("prod", true)], project.Profiles);
        var matrix = Assert.IsType<EnvMatrix>(project.Matrix);
        Assert.Equal(["DATABASE_URL", "SENTRY_DSN"], matrix.Rows.Select(row => row.Key));
        Assert.Equal([EnvCellState.Missing, EnvCellState.Set], matrix.Row("SENTRY_DSN")!.Cells.Select(cell => cell.State));
        Assert.Empty(project.ProfileProblems);
    }

    [Fact]
    public void SelectingAProfile_EditsThatProfile()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        project.SelectedProfile = "staging";
        Assert.Empty(project.Variables);

        project.BeginAddCommand.Execute(null);
        project.NewKey = "STAGING_ONLY";
        project.ConfirmAddCommand.Execute(null);
        Assert.Null(screen.Error);

        Assert.Equal(["STAGING_ONLY"], project.Variables.Select(row => row.Key));
        Assert.Equal(["dev", "staging", "prod"], project.Profiles.Select(profile => profile.Name));

        project.SelectedProfile = "prod";
        Assert.Equal(["DATABASE_URL", "SENTRY_DSN"], project.Variables.Select(row => row.Key));
        project.BeginRemove(project.Variables.Single(row => row.Key == "SENTRY_DSN"));
        project.ConfirmRemoveCommand.Execute(null);

        project.SelectedProfile = "Prod";
        Assert.Equal("prod", project.SelectedProfile);

        using var vault = Vault.Open(_fixture.Path_, TempVault.Password);
        Assert.NotNull(vault.Find(new EntryName("env/acme-api/staging", "STAGING_ONLY")));
        Assert.Null(vault.Find(new EntryName("env/acme-api", "STAGING_ONLY")));
        Assert.Null(vault.Find(new EntryName("env/acme-api/prod", "SENTRY_DSN")));
        Assert.Equal("dev-db", vault.Find(new EntryName("env/acme-api", "DATABASE_URL"))?.Password);
    }

    [Fact]
    public void ReferencePreview_SwitchesWithTheProfile()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        Assert.EndsWith("DATABASE_URL=kp://acme-api/dev/DATABASE_URL\n", project.ReferencePreview, StringComparison.Ordinal);

        project.SelectedProfile = "prod";
        Assert.EndsWith(
            "DATABASE_URL=kp://acme-api/prod/DATABASE_URL\nSENTRY_DSN=kp://acme-api/prod/SENTRY_DSN\n",
            project.ReferencePreview,
            StringComparison.Ordinal);
        Assert.DoesNotContain(_prodValue, project.ReferencePreview, StringComparison.Ordinal);

        var path = Path.Combine(_fixture.Home, EnvReferenceFile.FileName);
        Assert.StartsWith("Wrote 2 references", project.ExportReferences(path), StringComparison.Ordinal);
        Assert.Equal(project.ReferencePreview, File.ReadAllText(path));
        Assert.Contains("already exists", project.ExportReferences(path), StringComparison.Ordinal);
        Assert.StartsWith("Wrote", project.ExportReferences(path, replace: true), StringComparison.Ordinal);
        Assert.Contains("is a vault", project.ExportReferences(_fixture.Path_, replace: true), StringComparison.Ordinal);
        Assert.DoesNotContain(_prodValue, File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void RunCommand_NamesTheProfile()
    {
        using var screen = new EnvSetsViewModel(_session, _countdown);
        var project = Open(screen);

        Assert.Equal("keypaste run -p dev acme-api -- npm start", project.RunCommand);

        project.SelectedProfile = "prod";
        Assert.Equal("keypaste run -p prod acme-api -- npm start", project.RunCommand);
        Assert.Equal("prod", project.Launch.Profile);
        Assert.Equal("prod", project.Import.Profile);

        var directory = Directory.CreateTempSubdirectory("keypaste-profiles-run-").FullName;
        try
        {
            Assert.True(ProjectMappings.Save(
                KeypasteHome.ProjectsPath(_fixture.Home),
                [new ProjectMapping(Path.GetFullPath(_fixture.Path_), "acme-api", directory, "dotnet run")]));

            screen.OpenCommand.Execute("acme-api");
            Assert.Equal("keypaste run -p dev acme-api -- dotnet run", screen.OpenProject!.RunCommand);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EntryDetail_ShowsItsReference()
    {
        using var entries = new EntriesViewModel(_session, _countdown);

        entries.Selected = entries.Rows.Single(row => row.GroupPath == "env/acme-api/prod" && row.Title == "DATABASE_URL");
        var detail = entries.Detail!;

        Assert.Equal("kp://acme-api/prod/DATABASE_URL", detail.Reference);
        var profiles = Assert.IsType<EnvMatrixRow>(detail.Profiles);
        Assert.Equal(["dev", "prod"], profiles.Cells.Select(cell => cell.Profile));
        Assert.True(EnvProfileNames.IsProtected(profiles.Cells[1].Profile));

        entries.Selected = entries.Rows.Single(row => row.Title == "example");
        Assert.Equal("kp:///example", entries.Detail!.Reference);
        Assert.Null(entries.Detail.Profiles);
    }

    private static EnvProjectViewModel Open(EnvSetsViewModel screen)
    {
        screen.OpenCommand.Execute("acme-api");
        return screen.OpenProject!;
    }
}
