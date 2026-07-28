using System.Collections;
using System.Reflection;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// Nothing the desktop app puts on screen in 4.1 contains anything from inside the vault.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>SecretHygieneTests</c> in <c>Keypaste.Mcp.Tests</c>, and for the same
/// reason: the claim "this surface does not leak a secret" is worth nothing as a sentence and
/// everything as an assertion against a real vault holding known strings.
/// </para>
/// <para>
/// <b>This is cheap only because 4.1 renders no vault content at all</b> — Entries and Env Sets are
/// empty states, the Log reads machine state rather than the vault, and Agent Activity probes a pipe.
/// That is a deliberate property of the stage and this test is what keeps it true: the day 4.2 puts
/// an entry list on screen, this fails, and whoever is writing it has to decide what belongs there
/// rather than discovering later that everything did.
/// </para>
/// <para>
/// It walks every destination and reflects over what each one exposes, rather than checking the
/// handful of properties known today. A property added in six months is covered without anybody
/// remembering to add it here.
/// </para>
/// </remarks>
public sealed class SecretHygieneTests
{
    private const string Master = "correct horse battery staple";

    private const string SentinelPassword = "SENTINEL-PASSWORD-a17f3c";
    private const string SentinelUsername = "SENTINEL-USERNAME-b28e4d";
    private const string SentinelUrl = "https://SENTINEL-URL-c39f5e.example";
    private const string SentinelNotes = "SENTINEL-NOTES-d40a6f";
    private const string SentinelTitle = "SENTINEL-TITLE-e51b70";

    private static readonly string[] _everySentinel =
    [
        SentinelPassword,
        SentinelUsername,
        SentinelUrl,
        SentinelNotes,
        SentinelTitle,
    ];

    [Fact]
    public void No_destination_renders_anything_from_inside_the_vault()
    {
        using var fixture = new SentinelVault();
        using var session = new AppVaultSession(new ManualClock());

        using (var master = TempVault.Secret(Master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.VaultFile, master.Value));
        }

        using var shell = new ShellViewModel(session, fixture.Home, approverFromEnvironment: null);

        foreach (var destination in Destinations.All)
        {
            shell.Current = destination;

            foreach (var text in Surface(shell).Concat(Surface(shell.Content)))
            {
                foreach (var sentinel in _everySentinel)
                {
                    Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
                }
            }
        }
    }

    /// <summary>
    /// The recent list records the vault's path — which is the point — and nothing from inside it.
    /// </summary>
    [Fact]
    public async Task The_recent_list_holds_the_path_and_no_field_value()
    {
        using var fixture = new SentinelVault();
        using var session = new AppVaultSession(new ManualClock());
        using var unlock = new UnlockViewModel(session, fixture.Home, () => { });

        Assert.True(unlock.Offer(fixture.VaultFile));

        foreach (var c in Master)
        {
            unlock.Type(c);
        }

        await unlock.UnlockAsync();

        Assert.True(session.IsUnlocked);

        var written = File.ReadAllText(KeypasteHome.RecentPath(fixture.Home));

        Assert.Contains("sentinel.kdbx", written, StringComparison.Ordinal);

        foreach (var sentinel in _everySentinel)
        {
            Assert.DoesNotContain(sentinel, written, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(Master, written, StringComparison.Ordinal);
    }

    /// <summary>Every string a view model exposes, however it is shaped.</summary>
    private static IEnumerable<string> Surface(object? model)
    {
        if (model is null)
        {
            yield break;
        }

        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var property in model.GetType().GetProperties(Flags))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;

            try
            {
                value = property.GetValue(model);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            switch (value)
            {
                case string text:
                    yield return text;
                    break;

                case IEnumerable items and not string:
                    foreach (var item in items)
                    {
                        if (item is string line)
                        {
                            yield return line;
                        }
                        else if (item is not null)
                        {
                            yield return item.ToString() ?? string.Empty;
                        }
                    }

                    break;

                case not null:
                    yield return value.ToString() ?? string.Empty;
                    break;
            }
        }
    }

    /// <summary>A vault whose every field holds a string that must never reach a screen.</summary>
    private sealed class SentinelVault : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "keypaste-app-hygiene", Guid.NewGuid().ToString("n"));

        internal SentinelVault()
        {
            Directory.CreateDirectory(_directory);
            VaultFile = System.IO.Path.Combine(_directory, "sentinel.kdbx");

            using var vault = Vault.Create(VaultFile, Master);

            vault.AddEntry(new VaultEntry
            {
                Title = SentinelTitle,
                Username = SentinelUsername,
                Password = SentinelPassword,
                Url = SentinelUrl,
                Notes = SentinelNotes,
            });

            vault.Save();
        }

        internal string VaultFile { get; }

        internal string Home => _directory;

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
