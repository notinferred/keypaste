using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The drawn sidebar chooses the project Environments opened, under its vault's Environments, though the list had chosen the place as its rows were replaced.</summary>
public sealed class OpenProjectRowTests
{
    [Fact]
    public Task The_sidebar_draws_the_project_Environments_opened_as_chosen() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();

        using (var vault = Vault.Open(fixture.Path_, TempVault.Password))
        {
            ProjectVariables.Set(vault, "acme-api", "DATABASE_URL", "db-1");
            ProjectVariables.Set(vault, "acme-web", "VERCEL_TOKEN", "vt-2");
            vault.Save();
        }

        using var session = new AppVaultSession(new ManualClock(AppClock.Start));

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(session, fixture.Home, null, clock: new ManualClock(AppClock.Start));
        var window = new MainWindow { Width = 1280, Height = 800 };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        shell.Current = Destinations.Of(DestinationKind.EnvSets);
        WindowInput.Drain();

        var nav = window.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "Nav");
        Assert.Equal(new ProjectRow("acme-api", 1), nav.SelectedItem);
        window.Close();
    });
}
