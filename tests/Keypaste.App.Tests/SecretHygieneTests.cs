using System.Collections;
using System.Reflection;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// What the desktop app puts on screen, now that it puts vault contents on screen.
/// </summary>
/// <remarks>
/// <para>
/// In 4.1 this file held one blanket claim — no destination surfaces anything from inside the vault
/// — and said in as many words that the day 4.2 put an entry list on screen it would fail, and
/// whoever wrote that list would have to decide what belongs there. This is that decision, written
/// down as assertions.
/// </para>
/// <para>
/// <b>The blanket claim was not given an allow-set, and that is the point.</b> Moving four of five
/// sentinels into an allowed column would leave a test that passes for an implementation with no
/// list, no detail pane and no copy button — the surviving sentinel never reaches a view model
/// anyway, because the copy path reads the password out of the open vault and hands it straight to
/// the clipboard. So the blanket claim is replaced by four two-sided ones, each saying both what
/// must be present and what must be absent, and by one new invariant that is genuinely total:
/// <b>after a lock, every surface built while unlocked holds no sentinel of any kind, including the
/// ones it was allowed to show a moment earlier.</b>
/// </para>
/// <para>
/// <b>What may appear where.</b> A list row carries a title and a group, which is what
/// <c>keypaste ls</c> prints. The detail pane widens to username, URL and notes for the one entry a
/// person selected — <c>keypaste get</c>'s scope minus the password. A password appears nowhere, in
/// any state, including after Copy.
/// </para>
/// </remarks>
public sealed class SecretHygieneTests
{
    /// <summary>
    /// The strings that must never reach a screen.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> for the reason <see cref="Core.Audit.AuditText"/>
    /// gives and <c>Keypaste.Mcp.Tests</c>' own sentinels follow: <c>.editorconfig</c> applies
    /// <c>_camelCase</c> to every private field, constants included, so this repository has no
    /// <c>private const</c> anywhere. Worth knowing that only <c>dotnet format</c> enforces it —
    /// <c>dotnet build</c> does not, so a local build can be green while CI is not.
    /// </remarks>
    internal const string Master = "correct horse battery staple";

    internal const string SentinelPassword = "SENTINEL-PASSWORD-a17f3c";
    internal const string SentinelUsername = "SENTINEL-USERNAME-b28e4d";
    internal const string SentinelUrl = "https://SENTINEL-URL-c39f5e.example";
    internal const string SentinelNotes = "SENTINEL-NOTES-d40a6f";
    internal const string SentinelTitle = "SENTINEL-TITLE-e51b70";
    internal const string SentinelGroup = "SENTINEL-GROUP-a904c2";

    /// <summary>
    /// The password of a second entry, in the same group, whose title <em>is</em> on the list.
    /// </summary>
    /// <remarks>
    /// Load-bearing, and the reason the count is not five. "The list shows names and no field
    /// value" asserted against the selected entry's password alone would pass for an implementation
    /// that read every entry's password into every row and happened to render only the title — the
    /// rows would carry it, <c>ToString()</c> would surface it, and a binding typo would put it on
    /// screen. This entry is never selected, so its password's absence is a claim about the list
    /// rather than about selection.
    /// </remarks>
    internal const string SentinelUnselectedPassword = "SENTINEL-OTHER-PASSWORD-f62c81";

    internal const string SentinelUnselectedTitle = "SENTINEL-OTHER-TITLE-30bd19";

    internal const string SentinelProject = "SENTINEL-PROJECT-3ac71d";
    internal const string SentinelEnvKey = "SENTINEL_ENV_KEY_9B2E";
    internal const string SentinelEnvValue = "SENTINEL-ENV-VALUE-7d5e08";

    /// <summary>
    /// A variable in a second project, whose card is on screen and never opened.
    /// </summary>
    /// <remarks>
    /// The env half of <see cref="SentinelUnselectedPassword"/>'s argument. A card that read its
    /// project's values in order to say "3 variables" would carry this one, and "the cards hold
    /// names and counts" would be a claim about the project somebody happened to open.
    /// </remarks>
    internal const string SentinelOtherEnvValue = "SENTINEL-OTHER-ENV-VALUE-11c4b9";

    internal const string SentinelOtherProject = "SENTINEL-OTHER-PROJECT-6e0a44";

    private static readonly string[] _everySentinel =
    [
        SentinelPassword,
        SentinelUsername,
        SentinelUrl,
        SentinelNotes,
        SentinelTitle,
        SentinelGroup,
        SentinelUnselectedPassword,
        SentinelUnselectedTitle,
        SentinelProject,
        SentinelEnvKey,
        SentinelEnvValue,
        SentinelOtherEnvValue,
        SentinelOtherProject,
    ];

    /// <summary>The strings with no legitimate surface anywhere, in any state.</summary>
    /// <remarks>
    /// A password, a value in a project nobody opened, and the master password. The one string here
    /// with a legitimate moment on screen is <see cref="SentinelEnvValue"/>, which appears while
    /// somebody holds it — so it is asserted separately rather than blanket-forbidden, and its
    /// transience is what that test checks.
    /// </remarks>
    private static readonly string[] _neverAnywhere =
    [
        SentinelPassword,
        SentinelUnselectedPassword,
        SentinelOtherEnvValue,
        Master,
    ];

    /// <summary>
    /// No destination surfaces a password, on any screen, in any state.
    /// </summary>
    /// <remarks>
    /// What survives of 4.1's blanket claim, narrowed to the strings that never have a reason to be
    /// anywhere. Every destination is still walked, and every property is still reflected over, so a
    /// screen added in six months is covered without anybody remembering this file exists.
    /// </remarks>
    [Fact]
    public void No_destination_surfaces_a_password()
    {
        using var fixture = new SentinelVault();
        using var session = Unlocked(fixture);
        using var shell = new ShellViewModel(session, fixture.Home, approverFromEnvironment: null);

        foreach (var destination in Destinations.All)
        {
            shell.Current = destination;

            foreach (var text in Surface(shell).Concat(Surface(shell.Content)))
            {
                foreach (var sentinel in _neverAnywhere)
                {
                    Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
                }
            }
        }
    }

    /// <summary>
    /// The list holds names and no field value — including for an entry nobody selected.
    /// </summary>
    [Fact]
    public void The_entry_list_holds_names_and_no_field_value()
    {
        using var fixture = new SentinelVault();
        using var session = Unlocked(fixture);
        using var shell = new ShellViewModel(session, fixture.Home, approverFromEnvironment: null);

        shell.Current = Destinations.All[0];
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);

        // First, that the list actually listed something. Without this the sweep below passes for a
        // screen that renders nothing at all, which is the shape 4.1 already had.
        //
        // Four rows, not two: an environment variable is an ordinary entry under env/<project>, so
        // `keypaste ls` lists it and so does this. The screens differ in what they do with it, not
        // in whether they can see it (D-0014).
        Assert.Equal(4, entries.Rows.Count);
        Assert.Contains(entries.Rows, row => row.Title == SentinelTitle);
        Assert.Contains(entries.Rows, row => row.Title == SentinelEnvKey);
        Assert.Contains(entries.Rows, row => row.Title == SentinelUnselectedTitle);
        Assert.Contains(entries.Groups, group => group.Name == SentinelGroup);

        // And then that nothing from inside an entry came with them.
        foreach (var text in Surface(entries))
        {
            foreach (var sentinel in new[]
            {
                SentinelPassword,
                SentinelUnselectedPassword,
                SentinelUsername,
                SentinelUrl,
                SentinelNotes,
                SentinelEnvValue,
                SentinelOtherEnvValue,
            })
            {
                Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Selecting an entry widens the surface to the fields that were asked for, and no further.
    /// </summary>
    [Fact]
    public void The_detail_pane_shows_the_chosen_entry_and_never_its_password()
    {
        using var fixture = new SentinelVault();
        using var session = Unlocked(fixture);
        using var shell = new ShellViewModel(session, fixture.Home, approverFromEnvironment: null);

        shell.Current = Destinations.All[0];
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.Selected = entries.Rows.Single(row => row.Title == SentinelTitle);

        var detail = entries.Detail;
        Assert.NotNull(detail);

        // The widening, asserted rather than assumed: these are on screen on purpose.
        Assert.Equal(SentinelUsername, detail.Username);
        Assert.Equal(SentinelUrl, detail.Url);
        Assert.Equal(SentinelNotes, detail.Notes);

        // And the line it stops at.
        foreach (var text in Surface(detail))
        {
            Assert.DoesNotContain(SentinelPassword, text, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelUnselectedPassword, text, StringComparison.Ordinal);
        }

        // The mask says how long it is and nothing about what it is.
        Assert.Equal(SentinelPassword.Length, detail.PasswordLength);
        Assert.DoesNotContain(SentinelPassword, detail.PasswordMask, StringComparison.Ordinal);
    }

    /// <summary>
    /// The project cards hold names and counts, and no value — not even from a project nobody opened.
    /// </summary>
    [Fact]
    public void The_project_cards_hold_names_and_counts_and_no_value()
    {
        using var fixture = new SentinelVault();
        using var session = Unlocked(fixture);
        using var shell = new ShellViewModel(session, fixture.Home, approverFromEnvironment: null);

        shell.Current = Destinations.All[1];
        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);

        // First, that there are cards at all.
        Assert.Contains(SentinelProject, env.Projects);
        Assert.Contains(SentinelOtherProject, env.Projects);
        Assert.Equal(1, env.CountIn(SentinelProject));

        foreach (var text in Surface(env))
        {
            Assert.DoesNotContain(SentinelEnvValue, text, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelOtherEnvValue, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An open project shows its variable names and masks, and holds no value at rest.
    /// </summary>
    [Fact]
    public void An_open_project_holds_names_and_masks_and_no_value()
    {
        using var fixture = new SentinelVault();
        using var session = Unlocked(fixture);
        using var shell = new ShellViewModel(session, fixture.Home, approverFromEnvironment: null);

        shell.Current = Destinations.All[1];
        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);
        env.OpenCommand.Execute(SentinelProject);

        var project = env.OpenProject;
        Assert.NotNull(project);

        var row = Assert.Single(project.Variables);
        Assert.Equal(SentinelEnvKey, row.Key);
        Assert.Equal(SentinelEnvValue.Length, row.MaskedLength);

        foreach (var text in Surface(env).Concat(Surface(project)).Concat(Surface(row)))
        {
            foreach (var sentinel in new[] { SentinelEnvValue, SentinelOtherEnvValue })
            {
                Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Revealing is one value at a time, and it stops the moment the hold does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value legitimately reaches a screen here, which is what makes this the one sentinel that
    /// is not blanket-forbidden. What has to hold instead is narrower: a reveal names which key it
    /// is showing rather than what it holds, only one row can be revealing, and concealing gives the
    /// slot back. The characters themselves never enter a view model at all — they go from
    /// <see cref="EnvProjectViewModel.Reveal"/> straight to the control that draws them.
    /// </para>
    /// <para>
    /// That the control does not publish them to the accessibility bus is
    /// <c>RevealedValueTests</c>'s job, against a real visual tree.
    /// </para>
    /// </remarks>
    [Fact]
    public void Revealing_is_one_value_at_a_time_and_ends_with_the_hold()
    {
        using var fixture = new SentinelVault();
        using var session = Unlocked(fixture);
        using var shell = new ShellViewModel(session, fixture.Home, approverFromEnvironment: null);

        shell.Current = Destinations.All[1];
        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);
        env.OpenCommand.Execute(SentinelProject);

        var project = env.OpenProject!;
        var row = project.Variables.Single();

        Assert.Null(project.RevealedKey);

        // The value comes back to whoever asked — that is the feature — and the view model records
        // only which key it was.
        Assert.Equal(SentinelEnvValue, ((IRevealSource)row).Reveal());
        Assert.Equal(SentinelEnvKey, project.RevealedKey);

        foreach (var text in Surface(env).Concat(Surface(project)).Concat(Surface(row)))
        {
            Assert.DoesNotContain(SentinelEnvValue, text, StringComparison.Ordinal);
        }

        ((IRevealSource)row).Conceal();
        Assert.Null(project.RevealedKey);
    }

    /// <summary>
    /// A copied password reaches the clipboard and no view model on the way.
    /// </summary>
    /// <remarks>
    /// The failure this rules out is the friendly one: a toast reading
    /// <c>Copied — SENTINEL-PASSWORD-a17f3c</c>, or a "last copied" field kept so the button can be
    /// undone. Both are one line, both look harmless, and both put a password in the visual tree.
    /// </remarks>
    [Fact]
    public async Task A_copied_password_reaches_the_clipboard_and_no_view_model()
    {
        using var fixture = new SentinelVault();
        using var session = Unlocked(fixture);

        var clipboard = new FakeClipboard();
        using var shell = new ShellViewModel(
            session, fixture.Home, approverFromEnvironment: null, applyTheme: null,
            clipboard: clipboard, clock: new ManualClock());

        shell.Current = Destinations.All[0];
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.Selected = entries.Rows.Single(row => row.Title == SentinelTitle);

        await entries.Detail!.CopyPasswordCommand.ExecuteAsync();

        // The positive half: the copy happened. A sweep for a password that was never copied is a
        // sweep for a string nothing produced.
        Assert.Equal(SentinelPassword, clipboard.Content);

        foreach (var text in Surface(shell).Concat(Surface(shell.Content)).Concat(Surface(entries.Detail)))
        {
            Assert.DoesNotContain(SentinelPassword, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// After a lock, nothing built while unlocked holds anything — not even what it was allowed to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invariant that replaces 4.1's blanket claim, and the first stage in which it can be
    /// broken. <c>ShellViewModel</c>'s own rule is that everything it built is disposed on lock, and
    /// 4.1 asserted that against an empty room.
    /// </para>
    /// <para>
    /// <b>Two details decide whether this is worth anything.</b> The sweep holds a direct reference
    /// to the entries view model captured <em>before</em> the lock, because <c>Dispose</c> nulls
    /// <c>Content</c> and a sweep starting at the shell would find an empty graph and pass for an
    /// object that is still alive. And it counts properties that refuse to answer, because a view
    /// model reading lazily through a disposed vault throws from every getter and sweeps perfectly
    /// clean — "found no sentinel" and "asked no question" have to be told apart.
    /// </para>
    /// <para>
    /// The <c>shell.Dispose()</c> below is what <c>App.ShowUnlock</c> does when the session raises
    /// <c>Locked</c>; there is no application here to do it.
    /// </para>
    /// </remarks>
    [Fact]
    public void Nothing_built_while_unlocked_survives_the_lock()
    {
        using var fixture = new SentinelVault();
        using var session = Unlocked(fixture);
        using var shell = new ShellViewModel(session, fixture.Home, approverFromEnvironment: null);

        shell.Current = Destinations.All[0];
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.Selected = entries.Rows.Single(row => row.Title == SentinelTitle);
        var detail = entries.Detail!;

        var (answeredBefore, _) = Probe(entries);

        session.Lock(VaultLockReason.Manual);
        shell.Dispose();

        foreach (var (model, name) in new (object, string)[] { (entries, "entries"), (detail, "detail") })
        {
            var (answered, refused) = Probe(model);

            // "Nothing found" must not be able to mean "nothing asked". A view model that reads
            // lazily through a disposed vault throws from every getter and sweeps perfectly clean.
            Assert.True(
                refused == 0,
                $"{name} refused {refused} properties after the lock; a silent throw is not an empty screen");

            Assert.True(answered > 0, $"{name} answered nothing at all after the lock");

            foreach (var text in Surface(model))
            {
                foreach (var sentinel in _everySentinel)
                {
                    Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
                }
            }
        }

        // Every property that answered before still answers. The strings they return are shorter —
        // a cleared list is empty — but no property has gone quiet.
        Assert.Equal(answeredBefore, Probe(entries).Answered);
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

    private static AppVaultSession Unlocked(SentinelVault fixture)
    {
        var session = new AppVaultSession(new ManualClock());

        using (var master = TempVault.Secret(Master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.VaultFile, master.Value));
        }

        return session;
    }

    /// <summary>Every string a view model exposes, however it is shaped.</summary>
    private static IEnumerable<string> Surface(object? model) =>
        Walk(model, strict: false, out _);

    /// <summary>
    /// How many of a model's properties answered, and how many refused.
    /// </summary>
    /// <remarks>
    /// <see cref="Surface"/> skips a property that throws, which is right while the vault is open —
    /// a view model may legitimately refuse a question in some state. It is wrong after a lock,
    /// where "no sentinel found" and "no property answered" are indistinguishable, and the second is
    /// how this whole file would go quietly vacuous.
    /// </remarks>
    private static (int Answered, int Refused) Probe(object model)
    {
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var answered = 0;
        var refused = 0;

        foreach (var property in model.GetType().GetProperties(Flags))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                property.GetValue(model);
                answered++;
            }
            catch (TargetInvocationException)
            {
                refused++;
            }
        }

        return (answered, refused);
    }

    private static IEnumerable<string> Walk(object? model, bool strict, out Func<int> refused)
    {
        var count = 0;
        refused = () => count;

        return model is null ? [] : Enumerate();

        IEnumerable<string> Enumerate()
        {
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
                    if (strict)
                    {
                        count++;
                    }

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

                                // One level deeper for the things a list holds, because a row is
                                // exactly where a field value would hide behind a tidy ToString().
                                foreach (var inner in Walk(item, strict: false, out _))
                                {
                                    yield return inner;
                                }
                            }
                        }

                        break;

                    case not null:
                        yield return value.ToString() ?? string.Empty;
                        break;
                }
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
                GroupPath = SentinelGroup,
            });

            // A second entry, in the same group, that no test selects. Its title being on the list
            // is what makes its password's absence a claim about the list.
            vault.AddEntry(new VaultEntry
            {
                Title = SentinelUnselectedTitle,
                Password = SentinelUnselectedPassword,
                GroupPath = SentinelGroup,
            });

            var store = new EnvStore(vault);
            store.TrySet(SentinelProject, SentinelEnvKey, SentinelEnvValue, out _);

            // A second project, whose card is drawn and whose table is never opened.
            store.TrySet(SentinelOtherProject, "OTHER_KEY", SentinelOtherEnvValue, out _);

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
