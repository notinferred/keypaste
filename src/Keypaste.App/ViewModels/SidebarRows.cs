using Keypaste.App.Resources;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>What one of a vault's places, or All Accounts, lists.</summary>
internal enum ItemPlace
{
    /// <summary>Every item.</summary>
    All = 0,

    /// <summary>Logins, and passwords kept on their own.</summary>
    Logins = 1,

    /// <summary>API keys and tokens.</summary>
    ApiKeys = 2,

    /// <summary>The vault's projects and their .env templates.</summary>
    Environments = 3,

    /// <summary>Notes with no password.</summary>
    Notes = 4,

    /// <summary>Servers' credentials.</summary>
    Servers = 5,

    /// <summary>Databases' credentials.</summary>
    Databases = 6,

    /// <summary>The vault's Trash.</summary>
    Trash = 7,

    /// <summary>Items tagged <see cref="ItemPlaces.FavoriteTag"/>.</summary>
    Favorites = 8,

    /// <summary>Payment cards.</summary>
    Cards = 9,
}

/// <summary>The words, icon and kinds of each place.</summary>
internal static class ItemPlaces
{
    /// <summary>The tag that makes an item a favorite, kept in the vault so it syncs and KeePassXC shows it.</summary>
    internal const string FavoriteTag = "favorite";

    /// <summary>A vault's places in sidebar order.</summary>
    internal static IReadOnlyList<ItemPlace> InOrder { get; } =
        [ItemPlace.All, ItemPlace.Logins, ItemPlace.ApiKeys, ItemPlace.Environments, ItemPlace.Cards, ItemPlace.Notes, ItemPlace.Servers, ItemPlace.Databases, ItemPlace.Trash];

    internal static string Title(ItemPlace place) => place switch
    {
        ItemPlace.Logins => Strings.Logins,
        ItemPlace.ApiKeys => Strings.ApiKeys,
        ItemPlace.Environments => Strings.Environments,
        ItemPlace.Cards => Strings.Cards,
        ItemPlace.Notes => Strings.Notes,
        ItemPlace.Servers => Strings.ServerPlace,
        ItemPlace.Databases => Strings.DatabasePlace,
        ItemPlace.Trash => Strings.Trash,
        ItemPlace.Favorites => Strings.Favorites,
        _ => Strings.AllItems,
    };

    internal static string Icon(ItemPlace place) => place switch
    {
        ItemPlace.Logins => "globe",
        ItemPlace.ApiKeys => "key",
        ItemPlace.Environments => "braces",
        ItemPlace.Cards => "credit-card",
        ItemPlace.Notes => "file-text",
        ItemPlace.Servers => "server",
        ItemPlace.Databases => "database",
        ItemPlace.Trash => "trash",
        ItemPlace.Favorites => "star",
        _ => "inbox",
    };

    /// <summary>Whether the sidebar lists the place while it holds nothing: the kinds of item a vault may not have stay out until it has one.</summary>
    internal static bool ShownEmpty(ItemPlace place) => place is ItemPlace.All or ItemPlace.Logins or ItemPlace.Environments or ItemPlace.Trash;

    /// <summary>Whether an item of <paramref name="kind"/> is listed in <paramref name="place"/>.</summary>
    internal static bool Holds(ItemPlace place, EntryKind kind) => place switch
    {
        ItemPlace.All => true,
        ItemPlace.Logins => kind is EntryKind.Login or EntryKind.Password,
        ItemPlace.ApiKeys => kind == EntryKind.ApiKey,
        ItemPlace.Cards => kind == EntryKind.Card,
        ItemPlace.Notes => kind == EntryKind.Note,
        ItemPlace.Servers => kind == EntryKind.Server,
        ItemPlace.Databases => kind == EntryKind.Database,
        _ => false,
    };

    /// <summary>The template a place holds one kind of, which its "+" opens; null for a place of every kind or of none New item makes.</summary>
    internal static ItemTemplate? TemplateOf(ItemPlace place) => place switch
    {
        ItemPlace.Logins => ItemTemplate.Login,
        ItemPlace.ApiKeys => ItemTemplate.ApiKey,
        ItemPlace.Cards => ItemTemplate.Card,
        ItemPlace.Notes => ItemTemplate.Note,
        ItemPlace.Servers => ItemTemplate.Server,
        ItemPlace.Databases => ItemTemplate.Database,
        _ => null,
    };
}

/// <summary>The local account and the vaults it holds: before accounts that sync, the one on this computer.</summary>
/// <param name="Title">On this Mac, or On this PC.</param>
/// <param name="Vaults">The vaults open in the app, the one in view first.</param>
internal sealed record AccountSection(string Title, IReadOnlyList<VaultSection> Vaults)
{
    /// <summary>What the local account is called on this platform.</summary>
    internal static string LocalTitle => OperatingSystem.IsMacOS() ? Strings.OnThisMac : Strings.OnThisPc;
}

/// <summary>One open vault in the sidebar: its title, whether it is unfolded, and how many items each of its places holds; never a value.</summary>
internal sealed class VaultSection
{
    private readonly Dictionary<ItemPlace, int> _counts = [];
    private readonly Func<string?> _heldPath;

    /// <param name="session">The vault's session.</param>
    /// <param name="heldPath">The file the open vaults know the session for while it is locked, or null.</param>
    internal VaultSection(AppVaultSession session, Func<string?>? heldPath = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        _heldPath = heldPath ?? (() => null);
    }

    internal AppVaultSession Session { get; }

    private string? _knownPath;

    /// <summary>The vault's file name without its extension, scrubbed, kept through a lock; its path is a tooltip, never a heading.</summary>
    internal string Title => VaultPath.Length > 0 ? EntryNameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(VaultPath)).Text : Strings.LockedVault;

    /// <summary>The vault's full path, for the tooltip, as last seen unlocked or as the open vaults hold it, since a locked session forgets it.</summary>
    internal string VaultPath => _knownPath = Session.VaultPath ?? _heldPath() ?? _knownPath ?? string.Empty;

    internal bool IsLocked => !Session.IsUnlocked;

    /// <summary>Whether its places show under it.</summary>
    internal bool IsExpanded { get; set; } = true;

    internal int CountOf(ItemPlace place) => _counts.GetValueOrDefault(place);

    /// <summary>Counts each place from the vault's entries and projects, dropping each entry once counted; a locked vault counts nothing.</summary>
    internal void Count()
    {
        _counts.Clear();

        if (Session.Unlocked is not { } vault)
        {
            return;
        }

        try
        {
            var tags = EntriesViewModel.TagsByName(vault);

            foreach (var entry in vault.ReadEntries())
            {
                var kind = EntryKinds.Of(entry, tags.GetValueOrDefault((entry.GroupPath, entry.Title), []));

                foreach (var place in ItemPlaces.InOrder.Where(place => ItemPlaces.Holds(place, kind)))
                {
                    _counts[place] = _counts.GetValueOrDefault(place) + 1;
                }
            }

            _counts[ItemPlace.Environments] = ProjectCatalog.Read(vault).Projects.Count;
            _counts[ItemPlace.Favorites] = vault.ReadTags().Count(tagged => tagged.Tags.Contains(ItemPlaces.FavoriteTag, StringComparer.Ordinal));
        }
        catch (ObjectDisposedException)
        {
            _counts.Clear();
        }
    }
}

/// <summary>A row of the sidebar that goes somewhere: one of a vault's places, or Favorites or All Accounts across the open vaults.</summary>
/// <param name="Vault">The vault whose place it is, or null for Favorites and All Accounts.</param>
/// <param name="Place">What it lists.</param>
/// <param name="Count">The number at its right, or empty.</param>
internal sealed record PlaceRow(VaultSection? Vault, ItemPlace Place, string Count)
{
    internal string Title => Vault is null && Place == ItemPlace.All ? Strings.AllAccounts : ItemPlaces.Title(Place);

    internal string Icon => Vault is null && Place == ItemPlace.All ? "layers" : ItemPlaces.Icon(Place);

    /// <summary>Whether it is drawn at the sidebar's top level rather than under a vault.</summary>
    internal bool IsTop => Vault is null;

    /// <summary>The row's automation id, which no language changes: Place&lt;place&gt; under a vault, All&lt;place&gt; above them.</summary>
    internal string AutomationId => $"{(IsTop ? "All" : "Place")}{Place}";

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    public override string ToString() => Count.Length > 0 ? $"{Title}, {Count}" : Title;
}

/// <summary>The heading over an account's vaults, which cannot be chosen.</summary>
/// <param name="Title">On this Mac, or On this PC.</param>
internal sealed record AccountRow(string Title)
{
    public override string ToString() => Title;
}

/// <summary>A vault's heading, which folds its places; choosing it lists every item of the vault, and a locked vault's lists none.</summary>
/// <param name="Vault">The vault.</param>
/// <param name="IsExpanded">Whether its places show.</param>
/// <param name="IsLocked">Whether it is locked, which hides its places.</param>
/// <param name="SaveMark">"merging" while another program's save is merged, "unsaved" while its file lacks a change made here, or empty.</param>
internal sealed record VaultRow(VaultSection Vault, bool IsExpanded, bool IsLocked, string SaveMark = "")
{
    internal bool HasSaveMark => SaveMark.Length > 0;

    internal string Title => Vault.Title;

    /// <summary>Whether its chevron shows: a locked vault has no places to fold.</summary>
    internal bool IsExpandable => !IsLocked;

    internal string Chevron => IsExpanded ? "chevron-down" : "chevron-right";

    internal string FoldLabel => $"{(IsExpanded ? "Hide" : "Show")} {Title}'s places";

    public override string ToString() => string.Join(", ", new[] { Title, IsLocked ? "locked" : string.Empty, SaveMark }.Where(part => part.Length > 0));
}

