using Keypaste.Core.Audit;
using Keypaste.Core.Sharing;

namespace Keypaste.Core.Tests;

/// <summary>A vault holding entries worth sharing, a fake share server and an audit log, all temporary.</summary>
internal sealed class ShareFixture : IDisposable
{
    internal const string Master = "correct horse battery staple";
    internal const string StripeValue = "sk_live_51HxTheValueThatMustNotLeak";
    internal const string ChasePassword = "hunter2-but-longer";

    internal static readonly EntryName Stripe = new("env/acme-api", "STRIPE_SECRET_KEY");
    internal static readonly EntryName Chase = new("Banking", "Chase");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-share-tests-").FullName;

    internal ShareFixture()
    {
        using var vault = Vault.Create(VaultPath, Master);
        vault.AddEntry(new VaultEntry { GroupPath = Stripe.GroupPath, Title = Stripe.Title, Password = StripeValue });
        vault.AddEntry(new VaultEntry
        {
            GroupPath = Chase.GroupPath,
            Title = Chase.Title,
            Username = "sam@acme.dev",
            Password = ChasePassword,
            Url = "https://chase.com/",
        });
        vault.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Tokens, Title = "abc", Password = "verifier" });
        vault.Save();
    }

    internal string VaultPath => Path.Combine(_directory, "vault.kdbx");

    internal string AuditPath => Path.Combine(_directory, "home", "audit.jsonl");

    internal FakeShareServer Server { get; } = new();

    internal ManualClock Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 14, 40, 0, TimeSpan.Zero));

    /// <summary>Stands in for the audit log when a test wants it unwritable.</summary>
    internal bool AuditBroken { get; set; }

    /// <summary>Runs as the service opens the log, which is after the record was saved.</summary>
    internal Action? OnAudit { get; set; }

    internal Vault Open() => Vault.Open(VaultPath, Master);

    internal ShareService Service(Uri? endpoint = null) =>
        new(new ShareClient(Server, endpoint ?? ShareEndpoint.Default), Clock, OpenAudit);

    internal static ShareRequest Request(EntryName? entry = null, string field = "password", int views = 1, string? passphrase = null, string? to = null)
    {
        SecretBuffer? buffer = null;
        if (passphrase is not null)
        {
#pragma warning disable CA2000 // The request carries it; a test's buffer is reclaimed with the test.
            buffer = new SecretBuffer();
#pragma warning restore CA2000
            buffer.Append(passphrase);
        }

        return new ShareRequest(entry ?? Stripe, field, TimeSpan.FromHours(24), views, buffer, to);
    }

    internal string AuditText()
    {
        if (!File.Exists(AuditPath))
        {
            return string.Empty;
        }

        using var stream = new FileStream(AuditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Every field of every entry in the file on disk, read by a fresh open.</summary>
    internal string EverythingOnDisk()
    {
        using var vault = Open();
        return string.Join("\n", vault.ReadEntries().Select(e => string.Join("|", e.GroupPath, e.Title, e.Username, e.Password, e.Url, e.Notes)));
    }

    private AuditLog? OpenAudit()
    {
        OnAudit?.Invoke();
        return !AuditBroken && AuditLog.TryOpen(AuditPath, Clock, out var log, out _) ? log : null;
    }

    public void Dispose()
    {
        Server.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
