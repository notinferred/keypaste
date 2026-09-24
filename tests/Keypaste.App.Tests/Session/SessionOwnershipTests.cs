using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The app owns the vault it unlocks: a second owner is refused by name before the password is
/// tried, and the unlocked vault is served to <c>keypaste-mcp</c> on its endpoint (U.1).
/// </summary>
public sealed class SessionOwnershipTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_vault_another_session_holds_is_refused_before_the_password_is_tried()
    {
        using var fixture = new TempVault();
        using var first = new AppVaultSession(new ManualClock(), home: fixture.Home);
        using var second = new AppVaultSession(new ManualClock(), home: fixture.Home);

        Assert.Equal(UnlockOutcome.Opened, Unlock(first, fixture.Path_, TempVault.Password));

        Assert.Equal(UnlockOutcome.HeldElsewhere, Unlock(second, fixture.Path_, "not-the-password"));
        Assert.Equal(UnlockOutcome.HeldElsewhere, Unlock(second, fixture.Path_, TempVault.Password));
        Assert.False(second.IsUnlocked);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"the keypaste desktop app (process {Environment.ProcessId})"),
            second.HeldElsewhere,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Locking_gives_the_vault_up_and_the_next_unlock_is_a_new_session()
    {
        using var fixture = new TempVault();
        using var first = new AppVaultSession(new ManualClock(), home: fixture.Home);
        using var second = new AppVaultSession(new ManualClock(), home: fixture.Home);

        Unlock(first, fixture.Path_, TempVault.Password);
        var before = first.SessionId;
        first.Lock(VaultLockReason.Manual);

        Assert.Null(first.SessionId);
        Assert.Equal(UnlockOutcome.Opened, Unlock(second, fixture.Path_, TempVault.Password));
        Assert.NotNull(second.SessionId);
        Assert.NotEqual(before, second.SessionId);
    }

    [Fact]
    public void A_wrong_password_does_not_keep_the_vault_held()
    {
        using var fixture = new TempVault();
        using var first = new AppVaultSession(new ManualClock(), home: fixture.Home);
        using var second = new AppVaultSession(new ManualClock(), home: fixture.Home);

        Assert.Equal(UnlockOutcome.WrongPassword, Unlock(first, fixture.Path_, "not-the-password"));
        Assert.Equal(UnlockOutcome.Opened, Unlock(second, fixture.Path_, TempVault.Password));
    }

    [Fact]
    public async Task The_unlock_screen_names_the_process_holding_the_vault()
    {
        using var fixture = new TempVault();
        using var first = new AppVaultSession(new ManualClock(), home: fixture.Home);
        using var second = new AppVaultSession(new ManualClock(), home: fixture.Home);
        Unlock(first, fixture.Path_, TempVault.Password);

        var unlocked = 0;
        using var model = new UnlockViewModel(second, fixture.Home, new FakeVaultFilePicker(), () => unlocked++);
        Assert.True(model.Offer(fixture.Path_));

        foreach (var c in TempVault.Password)
        {
            model.Type(c);
        }

        await model.UnlockAsync();

        Assert.Equal(0, unlocked);
        Assert.StartsWith("This vault is already unlocked in the keypaste desktop app (process", model.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_unlocked_vault_is_served_on_its_endpoint_and_stops_being_served_on_lock()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(), home: fixture.Home);
        using var host = new SessionHost(session, approverOverride: null, () => new NobodyToAsk());

        Assert.Null(host.Endpoint);
        Unlock(session, fixture.Path_, TempVault.Password);
        var endpoint = host.Endpoint;
        Assert.NotNull(endpoint);

        await using (var client = await ApproverClient.TryConnectAsync(endpoint, _connect, Token))
        {
            Assert.NotNull(client);

            var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
            Assert.NotNull(attached);
            Assert.Equal(session.SessionId, attached.Session);

            var listing = await client.ListAsync(Listing(fixture.Path_, attached.Session!), Token);
            Assert.NotNull(listing);
            Assert.Equal([new EntryName(string.Empty, "example")], listing.Names);
            Assert.Equal(session.SessionId, listing.Session);
        }

        session.Lock(VaultLockReason.Manual);

        Assert.Null(host.Endpoint);
        await using var afterLock = await ApproverClient.TryConnectAsync(endpoint, TimeSpan.FromMilliseconds(300), Token);
        Assert.Null(afterLock);
    }

    /// <summary>
    /// Nothing unlocks across the endpoint: every byte of an attachment, a listing and a credential
    /// request, both ways, carries neither the master password nor the keyfile.
    /// </summary>
    [Fact]
    public async Task The_endpoint_traffic_carries_no_master_password_or_keyfile()
    {
        using var home = new TempHome();
        var vault = Path.Combine(home.Path, "keyed.kdbx");
        var keyfile = Path.Combine(home.Path, "vault.key");
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyfile, keyBytes);
        const string master = "a master password nobody should see on a pipe";

        using var session = new AppVaultSession(new ManualClock(), home: home.Path);
        using (var password = TempVault.Secret(master))
        {
            Assert.Equal(
                VaultCreationOutcome.Created,
                session.TryCreate(vault, password.Value, password.Value, keyfile));
        }

        session.Unlocked!.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "STRIPE_KEY", Password = "entry-secret" });
        session.Unlocked.Save();

        using var host = new SessionHost(session, approverOverride: null, () => new NobodyToAsk());
        Assert.NotNull(host.Endpoint);

        var front = "keypaste-relay-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        using var recorded = new MemoryStream();
        var relay = RelayAsync(front, host.Endpoint, recorded);

        await using (var client = await ApproverClient.TryConnectAsync(front, _connect, Token))
        {
            Assert.NotNull(client);
            var attached = await client.AttachAsync(new AttachRequest(vault), Token);
            Assert.NotNull(attached);
            Assert.True(attached.Attached);

            var listing = await client.ListAsync(Listing(vault, attached.Session!), Token);
            Assert.NotNull(listing);
            Assert.True(listing.VaultUnlocked);

            var reply = await client.RequestAsync(
                new CredentialRequest
                {
                    Entry = "env/dev/STRIPE_KEY",
                    Field = "password",
                    Reason = "deploy",
                    TtlSeconds = 60,
                    Exposure = ["env/**"],
                    Vault = vault,
                    Session = attached.Session!,
                },
                Token);

            Assert.NotNull(reply);
            Assert.Equal(AuditDecision.Denied, reply.Decision);
            Assert.Equal(session.SessionId, reply.Session);
        }

        await relay.WaitAsync(_connect, Token);

        var bytes = recorded.ToArray();
        Assert.Contains("\"attach\"", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(master)));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(master)));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(keyBytes.AsSpan(0, 16)));
        Assert.DoesNotContain(Convert.ToHexString(keyBytes, 0, 16), Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(keyBytes, 0, 15), Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    private static NamesRequest Listing(string vault, string session) =>
        new(["**"]) { Vault = vault, Session = session };

    private static UnlockOutcome Unlock(AppVaultSession session, string path, string password)
    {
        using var master = TempVault.Secret(password);
        return session.TryUnlock(path, master.Value);
    }

    /// <summary>Forwards one connection from <paramref name="front"/> to <paramref name="back"/>, keeping every byte.</summary>
    private static async Task RelayAsync(string front, string back, MemoryStream recorded)
    {
        await using var server = new NamedPipeServerStream(
            front, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync(Token);

        await using var client = new NamedPipeClientStream(
            ".", back, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(Token);

        await Task.WhenAll(PumpAsync(server, client, recorded), PumpAsync(client, server, recorded));
    }

    private static async Task PumpAsync(Stream from, Stream to, MemoryStream recorded)
    {
        var buffer = new byte[4096];

        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer, Token)) > 0)
            {
                lock (recorded)
                {
                    recorded.Write(buffer, 0, read);
                }

                await to.WriteAsync(buffer.AsMemory(0, read), Token);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The other side went away, which is how a relay finishes.
        }
        finally
        {
            // Closing this direction's destination ends the pump reading from it.
            to.Close();
        }
    }
}
