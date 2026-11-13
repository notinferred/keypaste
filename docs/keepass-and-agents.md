<a id="your-keepass-vault-cant-talk-to-ai--and-everyone-is-pasting-secrets-into-chats-instead"></a>

# KeePass vaults and agent access

> This essay describes the source build headed for keypaste 1.0.0, which is not yet published. The published `v0.3.0` has `keypaste agent`, a separate `keypaste-mcp` binary, an `Approve? [y/N]` prompt whose `y` keeps a grant for up to 300 seconds, and no `keypaste import`. [FEATURES](FEATURES.md) lists what each version has. The reports cited cover their stated 2025–2026 periods and are not a current market inventory.

A coding agent that needs a credential may ask you to paste it into the chat, exposing it to the client and its retained conversation.

[Harmonic Security](https://www.harmonic.security/resources/what-22-million-enterprise-ai-prompts-reveal-about-shadow-ai-in-2025) reported 5,903 credential exposures in 22.4 million enterprise AI prompts observed during 2025; 12.8% of coding-tool exposures were API keys or tokens. GitGuardian reported [28.65 million new hardcoded secrets](https://blog.gitguardian.com/the-state-of-secrets-sprawl-2026/) in public GitHub commits that year, an 81% increase in AI-service secrets, and 24,008 secrets in MCP configuration files, including 2,117 still-valid values.

<a id="your-password-manager-probably-can-actually"></a>

## Existing integrations

Password managers already offer agent integrations. The linked materials describe [1Password's Environments MCP server](https://www.1password.dev/environments/mcp-server), [Bitwarden's March 2026 Agent Access SDK announcement](https://bitwarden.com/blog/introducing-agent-access-sdk/) and [Keeper's MCP server](https://github.com/Keeper-Security/keeper-mcp-golang-docker). Their approval and secret-delivery models differ; evaluate the version and deployment you would actually use before comparing them.

Direct use of an existing KDBX file depends on the integration. Hosting also varies: [Bitwarden supports self-hosted organizations](https://bitwarden.com/help/self-host-an-organization/). keypaste provides an account-free local approval bridge over a KDBX file.

## Why the vault stayed offline

Local vault ownership comes with an established security model. [KeePass's security documentation](https://keepass.info/help/base/security.html) discusses keyloggers, clipboard monitoring, process memory and attacks on encrypted database files. KeePassXC also documents a [build option to disable networking](https://keepassxc.org/docs/#faq-security-no-network). That option is distinct from claiming that every default build has no network functionality.

KeePassXC [replaced its localhost HTTP browser integration in 2018](https://keepassxc.org/blog/2018-02-28-2.3-released/) with an extension using a [Unix domain socket or named pipe](https://github.com/keepassxreboot/keepassxc-browser). Its [Confirm Access dialog](https://github.com/keepassxreboot/keepassxc/blob/develop/docs/topics/BrowserIntegration.adoc) lets users select credentials and optionally remember approval.

This provides an existing KDBX model for another program requesting credentials over a local channel.

## What arrived instead

In August 2025, a compromised `nx` release [harvested 2,349 credentials into 1,079 attacker-created repositories](https://blog.gitguardian.com/the-nx-s1ngularity-attack-inside-the-credential-leak/) over fifteen hours. It invoked locally installed Claude, Gemini and Q CLIs [with safety flags disabled](https://www.wiz.io/blog/s1ngularity-supply-chain-attack) to search for more; a third of infected machines had one. [CVE-2026-21852](https://github.com/advisories/GHSA-jh7p-qr78-84p7) later described Claude Code applying repository settings that redirected `ANTHROPIC_BASE_URL` and sent an API key before its trust prompt. Agents can also [act on instructions embedded in untrusted data](https://invariantlabs.ai/blog/mcp-github-vulnerability).

Giving an agent a plaintext `.env` exposes all credentials in that file without an individual release record. Limiting each release reduces that access.

<a id="one-credential-one-question-one-line-in-a-log"></a>

## Local approvals

Your MCP client starts `keypaste mcp`, a bridge that holds no vault and forwards requests. You unlock the vault where you approve: in `keypaste approve`, which you start in a terminal, or in the desktop app. An agent cannot make keypaste ask for the master password, so no other program can imitate that prompt through it.

An agent cannot name an item outside what it can see. `keypaste setup` lets an agent see every item's title, group, tags and URL, and its [agent settings](policy.md) decide which values it may have: each kind of value is Ask, Allow or Block, Ask by default, and Allow takes effect only once you confirm it where the vault is unlocked. A glob in the client's configuration can only narrow what the agent sees.

Each request is answered by those settings or by you: Deny, Allow once, or for a labelled agent Always allow or Always block for that item's field. Silence for 45 seconds denies it. Every call is written to a local, hash-chained audit log before the answer, without the released value, and a call is refused if the log cannot be written. [Approving an agent's request](approvals.md) explains each choice.

## What it looks like

<sub>The 1.0.0 build's terminal approver; the published <code>v0.3.0</code> asks <code>Approve? [y/N]</code>.</sub>

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  agent    claude-code (claude-code)
  item     env/demo/.env
  field    STRIPE_KEY

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

[d] deny  [o] once  [a] always  [b] block  45s ›
```

One keystroke later the deploy runs, and the exchange is two lines you can read back:

```
2 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           agent        item           decision  method
  2026-07-27 09:57:42  claude-code  -              granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/.env  granted   prompt
```

`exposure` records a listing of what the agent can see and `prompt` a request a person reviewed. [The demo](demo.md) shows the flow in about sixty seconds.

## Moving from another KeePass file

An existing KDBX file can stay where it is: `keypaste import old.kdbx --in-place` checks that it unlocks and remembers it, and from then on `--vault old.kdbx`, `KEYPASTE_VAULT` or `keypaste use old.kdbx` opens it like any other vault. With no vault named or chosen, `--in-place` is the default.

To bring it into the vault you already use, name that vault. The file is opened read-only and never written; a `<name>.keyx` or `<name>.key` beside it is used as its key file unless `--source-keyfile none` says otherwise.

```
keypaste import old.kdbx --vault mine.kdbx --dry-run
keypaste import old.kdbx --vault mine.kdbx --into old
```

Each top-level group lands under `--into`, which defaults to the file's name, including an `env` group, whose entries keep the tags that put them in projects. Entries keep their custom fields, attachments, tags, icons, times and history, with new identities. The file's recycle bin and keypaste's reserved `.keypaste` group stay behind. `--dry-run` shows every row and where it will land; a row that cannot land stops the whole import before anything is written. The desktop's Import from another app does the same and also takes other password managers' exports.

<a id="what-this-does-not-do"></a>

## Limits

A released value goes to the agent's client, which may send it to a remote model, keep it in a session file or put it on a command line; keypaste cannot erase those copies or revoke the credential at its provider, and its log records the release, not every later use. keypaste labels the agent's reason as the agent's claim and cannot verify it. [SECURITY](../SECURITY.md) and [THREATS](../THREATS.md) state the guarantees and limits.

<a id="the-stance"></a>

## Availability

The vault works without an account or network and opens in KeePassXC; [FEATURES](FEATURES.md#keepassxc-compatibility) says what the compatibility tests check. The source is licensed under AGPL-3.0. Today's download is the pre-1.0 CLI and MCP bridge `v0.3.0`, unsigned, with the terminal approver; the desktop app comes with 1.0.0 ([RELEASE](RELEASE.md)). [`kprun`](https://github.com/numikel/kprun) is another KDBX-oriented project to evaluate for process injection.
