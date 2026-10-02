# Agent access, team data and recovery: review of 2 October 2026

Collected on 2026-10-02 against the repository at `b76eb61`. This record keeps what a second review that day found about how secrets tools let AI agents find credentials, tie a credential to a service, keep personal and organization data apart and recover lost access, and the decisions it led to (D-0405 to D-0408, [PRODUCT](../PRODUCT.md) v1.10). It is evidence for later decisions, not a description of the product today, and it is not revised; a later review is a new file.

keypaste facts come from the repository: THREATS T-4, T-8, T-14, T-35 and T-36, `src/Keypaste.Mcp/Tools/ToolText.cs`, docs/ARCHITECTURE.md and PRODUCT v1.9. Competitor facts come from the vendor pages listed under [Sources](#sources), read that day.

## What an agent sees

| Product | What the agent sees | How a value is used |
|---|---|---|
| keypaste | Entry titles and group paths inside the bridge's `--expose` globs, `env/**` by default, cleaned and fenced as untrusted data; a listing never carries a username, URL, note or value | One field after a person's answer or a rule they wrote, into the model's context; or injected into a command whose output is scrubbed (T-35) |
| 1Password Environments MCP | Environment and variable names; the server "cannot return secret values … even if an agent requests them" | A mounted `.env` read by the authorized process, after a prompt per environment that lasts until 1Password locks |
| Infisical Agent Vault | A session token scoped to one access bundle, not the services, credentials or bundle contents | The proxy matches the host and attaches the credential |
| Infisical MCP server | Secret values from `list-secrets` and `get-secret` | Into the model |
| Bitwarden MCP server | Whole items, passwords and notes included, with create, edit and delete; its README warns of exactly this | Into the model |
| Bitwarden Agent Access SDK, early preview | The agent asks by domain or item ID and receives the username, password, TOTP, URI and notes | Returned, or injected into a child process, over a Noise tunnel the person pairs with `aac listen` |
| Keeper KSM MCP | Titles, record types and folders in lists; search covers notes, usernames and URLs; values masked | Unmasking needs a confirmation, which Keeper's documentation says "defeats the security purpose" |
| Phase | Keys; `config` values always, `secret` values by a choice made once at `phase ai enable`, `sealed` values never | Injected by `phase run` |
| Varlock | The repository's `.env.schema`: names, types, validation and descriptions, never values | A proxy adds them in transit |
| KeePassXC | Nothing; it has no AI integration | — |

The products that keep values from agents give them names or nothing. Where an agent gets descriptions, as in Varlock, the project writes them in its repository rather than the vault holding them.

## How a credential is tied to a service

- Infisical's Agent Vault defines a service as its hosts, an authentication scheme (bearer, basic or pass-through), a credential, the methods and paths it allows, custom headers and substitutions. An access bundle groups services, and a session is a time-bound grant to one bundle.
- Bitwarden's Agent Access looks a credential up by domain, or by item ID.
- 1Password's Secure Agentic Autofill, with Browserbase, maps credentials to the target website, which also guards against phishing. The raw credential never enters the model's context, and each request is approved.

In each, the broker matches the host and the model does not choose by URL. This informed D-0405.

## Personal and organization data

- 1Password gives each vault a random vault key, encrypted with the public key of each member who may use it. Personal and business accounts sit in one app.
- Bitwarden encrypts organization items with an Organization Symmetric Key, which is encrypted with each member's RSA public key; personal items use the member's own key, and one vault view shows both.
- KeePassXC opens separate databases in tabs. An AutoOpen group in one database opens the others when it unlocks, and KeeShare shares a group through container files merged by entry history.
- Infisical and Phase are team platforms whose secrets live in organization projects on their servers; Phase offers personal overrides of a shared value.

This informed D-0406 and D-0407.

## Rules shown to agents

None of the products checked tells an agent which items are pre-approved; Infisical's agent cannot see its bundle's contents. keypaste's release under a standing rule says that a rule allowed it and never names the rule (`ToolText.ReleasedByPolicy`).

## Recovering lost access

| Method | Products | How it works | A copy of the key outside the person's devices? |
|---|---|---|---|
| Emergency kit | 1Password | The sign-in address, email, Secret Key, a space for the account password and a setup QR code | No |
| Recovery code | 1Password | Regains access after a forgotten password or a lost kit | Not checked |
| Recovery phrase | Proton, 12 words | "A second password used to encrypt a copy of your encryption key" | Yes |
| Recovery phrase | Keeper, 24 BIP39 words with an email code | Resets the master password | Yes, implied, since the reset keeps the vault |
| Recovery file | Proton | "A local encrypted backup of your encryption key" | No |
| Trusted contact | Bitwarden Premium emergency access | A named contact gets access after a waiting period | Yes |
| Organization admin | 1Password owners, organizers and admins; Bitwarden Enterprise account recovery; Keeper account transfer | An admin sets a new password or moves the vault | Yes |
| None | KeePass | A lost master password loses the vault | — |

PRODUCT law 3.1 forbids any copy of the vault master key outside the local process, "including through … an encrypted server backup". A keypaste cloud vault can therefore offer only a kit the person keeps and a device still signed in, and a team recovers a member by wrapping its project keys to the member's new key pair. This informed D-0408.

## Decisions taken from this review

- D-0405: at X.1 an agent asks for a host, not an entry.
- D-0406: one store per team that the app keeps, never inside the personal vault.
- D-0407: the three ways a credential leaves a vault.
- D-0408: PRODUCT v1.10, with the free cloud vault, its recovery, server access by choice and the cloud's tiers.

## Sources

- Infisical: [Agent Vault, how it works](https://infisical.com/docs/documentation/platform/agent-vault/how-it-works), [MCP server](https://npmjs.com/package/@infisical/mcp)
- 1Password: [Environments MCP server](https://www.1password.dev/environments/mcp-server), [Secure Agentic Autofill](https://blog.1password.com/closing-the-credential-risk-gap-for-browser-use-ai-agents), [security white paper](https://1password.com/files/1password-white-paper.pdf), [Emergency Kit](https://support.1password.com/emergency-kit/), [recovery codes](https://1password.com/blog/introducing-1password-recovery-codes), [account recovery](https://support.1password.com/recovery)
- Bitwarden: [MCP server](https://github.com/bitwarden/mcp-server), [Agent Access SDK](https://github.com/bitwarden/agent-access), [security white paper](https://bitwarden.com/help/bitwarden-security-white-paper/), [emergency access](https://bitwarden.com/help/emergency-access/), [account recovery](https://bitwarden.com/help/admin-reset/)
- Keeper: [KSM MCP](https://pkg.go.dev/github.com/keeper-security/ksm-mcp), [24-word recovery phrases](https://www.keepersecurity.com/blog/2023/04/24/keeper-introduces-24-word-recovery-phrases/)
- Proton: [data recovery and end-to-end encryption](https://proton.me/blog/data-recovery-end-to-end-encryption)
- Phase: [AI agents](https://docs.phase.dev/integrations/agents/opencode), [documentation](https://docs.phase.dev/)
- Varlock: [varlock.dev](https://varlock.dev/)
- KeePassXC: [user guide](https://keepassxc.org/docs/KeePassXC_UserGuide), [quick start with AutoOpen and KeeShare](https://git.warze.org/Warze/keepassxc/src/tag/2.5.0/docs/QUICKSTART.md)
