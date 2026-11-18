using System.Collections.Frozen;
using System.Net;
using Keypaste.App.ViewModels;
using Keypaste.Core.Browser;

namespace Keypaste.App.Controls;

/// <summary>What an item's tile draws: a service's mark, an icon for what the item is, or, with neither, the title's letter.</summary>
/// <param name="Service">The Simple Icons slug of the service the item's website belongs to, or null.</param>
/// <param name="Icon">The Lucide icon of the item's kind or host, or null.</param>
internal sealed record ItemMark(string? Service, string? Icon)
{
    internal static ItemMark Letter { get; } = new(null, null);
}

/// <summary>
/// Picks an item's mark from what the vault holds, never from the network: the service its website's registrable domain belongs to,
/// then its kind, a project's variable, and the kind of host it names.
/// </summary>
internal static class ItemMarks
{
    // Each registrable domain names one service's mark in Theme/ServiceIcons.axaml; a subdomain is the same site, a lookalike is not.
    private static readonly FrozenDictionary<string, string> _services = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["airbnb.com"] = "airbnb",
        ["anthropic.com"] = "anthropic",
        ["apple.com"] = "apple",
        ["asana.com"] = "asana",
        ["atlassian.com"] = "atlassian",
        ["auth0.com"] = "auth0",
        ["backblaze.com"] = "backblaze",
        ["bitbucket.org"] = "bitbucket",
        ["bsky.app"] = "bluesky",
        ["cash.app"] = "cashapp",
        ["circleci.com"] = "circleci",
        ["claude.ai"] = "claude",
        ["cloudflare.com"] = "cloudflare",
        ["coinbase.com"] = "coinbase",
        ["datadoghq.com"] = "datadog",
        ["digitalocean.com"] = "digitalocean",
        ["discord.com"] = "discord",
        ["docker.com"] = "docker",
        ["dropbox.com"] = "dropbox",
        ["duckduckgo.com"] = "duckduckgo",
        ["ebay.com"] = "ebay",
        ["epicgames.com"] = "epicgames",
        ["etsy.com"] = "etsy",
        ["facebook.com"] = "facebook",
        ["figma.com"] = "figma",
        ["firebase.com"] = "firebase",
        ["fly.io"] = "flydotio",
        ["github.com"] = "github",
        ["gitlab.com"] = "gitlab",
        ["gmail.com"] = "gmail",
        ["godaddy.com"] = "godaddy",
        ["google.com"] = "google",
        ["grafana.com"] = "grafana",
        ["hashicorp.com"] = "hashicorp",
        ["hetzner.cloud"] = "hetzner",
        ["hetzner.com"] = "hetzner",
        ["hubspot.com"] = "hubspot",
        ["icloud.com"] = "icloud",
        ["instagram.com"] = "instagram",
        ["linear.app"] = "linear",
        ["mailchimp.com"] = "mailchimp",
        ["mailgun.com"] = "mailgun",
        ["mastodon.social"] = "mastodon",
        ["medium.com"] = "medium",
        ["miro.com"] = "miro",
        ["mongodb.com"] = "mongodb",
        ["namecheap.com"] = "namecheap",
        ["neon.tech"] = "neon",
        ["netflix.com"] = "netflix",
        ["netlify.com"] = "netlify",
        ["notion.com"] = "notion",
        ["notion.so"] = "notion",
        ["npmjs.com"] = "npm",
        ["okta.com"] = "okta",
        ["ovh.com"] = "ovh",
        ["ovhcloud.com"] = "ovh",
        ["pagerduty.com"] = "pagerduty",
        ["patreon.com"] = "patreon",
        ["paypal.com"] = "paypal",
        ["pinterest.com"] = "pinterest",
        ["planetscale.com"] = "planetscale",
        ["playstation.com"] = "playstation",
        ["postgresql.org"] = "postgresql",
        ["proton.me"] = "proton",
        ["protonmail.com"] = "proton",
        ["railway.com"] = "railway",
        ["reddit.com"] = "reddit",
        ["redis.io"] = "redis",
        ["render.com"] = "render",
        ["resend.com"] = "resend",
        ["revolut.com"] = "revolut",
        ["sentry.io"] = "sentry",
        ["shopify.com"] = "shopify",
        ["signal.org"] = "signal",
        ["spotify.com"] = "spotify",
        ["steamcommunity.com"] = "steam",
        ["steampowered.com"] = "steam",
        ["stripe.com"] = "stripe",
        ["substack.com"] = "substack",
        ["supabase.com"] = "supabase",
        ["tailscale.com"] = "tailscale",
        ["telegram.org"] = "telegram",
        ["terraform.io"] = "terraform",
        ["tiktok.com"] = "tiktok",
        ["trello.com"] = "trello",
        ["twitch.tv"] = "twitch",
        ["twitter.com"] = "x",
        ["uber.com"] = "uber",
        ["venmo.com"] = "venmo",
        ["vercel.com"] = "vercel",
        ["vultr.com"] = "vultr",
        ["whatsapp.com"] = "whatsapp",
        ["wise.com"] = "wise",
        ["wordpress.com"] = "wordpress",
        ["wordpress.org"] = "wordpress",
        ["x.com"] = "x",
        ["youtube.com"] = "youtube",
        ["zapier.com"] = "zapier",
        ["zoom.com"] = "zoom",
        ["zoom.us"] = "zoom",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every service whose mark a tile can draw.</summary>
    internal static IEnumerable<string> Services => _services.Values.Distinct(StringComparer.Ordinal);

    /// <param name="url">The item's website, as the vault holds it.</param>
    /// <param name="kind">What the item is.</param>
    /// <param name="inProject">Whether a project tag makes it one of a project's variables.</param>
    internal static ItemMark Of(string url, EntryKind kind, bool inProject)
    {
        ArgumentNullException.ThrowIfNull(url);

        var origin = WebOrigin.TryReadItem(url.Trim(), out var read) ? read : null;

        if (origin?.Site is { } site && _services.TryGetValue(site, out var service))
        {
            return new ItemMark(service, null);
        }

        var icon = kind switch
        {
            not (EntryKind.Login or EntryKind.Password) => EntryKinds.Icon(kind),
            _ when inProject => EntryKinds.Icon(EntryKind.Environment),
            _ => origin is null ? null : HostIcon(origin),
        };

        return icon is null ? ItemMark.Letter : new ItemMark(null, icon);
    }

    /// <summary>This computer, an address, or a name only the home network answers to; null for a site on the internet.</summary>
    private static string? HostIcon(WebOrigin origin)
    {
        var host = origin.Host.Trim('[', ']');

        if (host == "localhost" || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address)))
        {
            return "laptop";
        }

        if (address is not null)
        {
            return "ethernet-port";
        }

        return origin.IsSingleLabel || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal)
            || host.EndsWith(".home.arpa", StringComparison.Ordinal)
            ? "house"
            : null;
    }
}
