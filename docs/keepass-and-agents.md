<a id="your-keepass-vault-cant-talk-to-ai--and-everyone-is-pasting-secrets-into-chats-instead"></a>

# KeePass vaults and agent access

> CLI launch essay. The cited reports and announcements describe their stated 2025–2026 periods; they are not an exhaustive current market inventory. Recheck comparisons before publishing a new campaign. [PRODUCT](PRODUCT.md) owns the focused local password-manager direction, [FEATURES](FEATURES.md) owns the current capability inventory, and [RELEASE](RELEASE.md) owns current downloads.

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

`keypaste-mcp` is the bridge launched by your client; it holds no vault and forwards requests. You start `keypaste agent` in a terminal to unlock the vault and approve releases. Requests cannot trigger a master-password prompt, avoiding prompts another program could imitate.

An agent cannot even name an entry you did not expose: the default is the `env/` subtree and nothing else, and widening it takes a glob you typed into your own client config.

The approver resolves the entry, rechecks exposure, considers a live grant and recent refusal, then consults policy or prompts when required. It reads the field for release only after authorization. The vault is already unlocked; refusal does not imply that no plaintext existed in approver memory.

Approval releases one field and permits reuse on the same connection for its lifetime. Silence for forty-five seconds denies the request. Every tool call is appended to `~/.keypaste/audit.jsonl` before the response. The returned value is excluded, but names and reason excerpts remain logged metadata. Calls are refused if the log cannot be written.

## What it looks like

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  client   claude-code
  entry    env/demo/STRIPE_KEY
  field    password

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

[d] deny  [o] once  [h] 1 hour  45s ›
```

One keystroke later the deploy runs, and the exchange is two lines you can read back:

```
2 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           client       entry                decision  method
  2026-07-27 09:57:42  claude-code  -                    granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/STRIPE_KEY  granted   prompt
```

`exposure` records a listing allowed by configured globs; `prompt` records a request a person reviewed. [The demo](demo.md) shows the flow in about sixty seconds.

<a id="what-this-does-not-do"></a>

## Limits

`request_credential` returns the credential as text and structured data. A client may send those results to a remote model, retain them in its session file and put the value on a command line. TTL bounds cached approval reuse; it cannot erase those copies or revoke the credential at its provider. The local audit records the release, not every later use.

keypaste removes control characters from the reason, caps its length and labels its author, but cannot verify the claim. Policy rules release matching fields without human review. Anyone who can write the audit file can recompute its hash chain. A hostile process running as your user is outside these protections.

<a id="the-stance"></a>

## Availability

The local vault works without an account or network and opens in KeePassXC. A permanent CI gate checks compatibility in both directions against a real `keepassxc-cli` on all three operating systems. The source is licensed under AGPL-3.0.

It is also pre-1.0 and says so: unsigned binaries, no released GUI, and a terminal prompt rather than a native dialog; [RELEASE](RELEASE.md) records that dated status. [`kprun`](https://github.com/numikel/kprun) is another KDBX-oriented project to evaluate for process injection. keypaste combines a KDBX file you own, account-free local use, explicit approvals with bounded reuse or rules you wrote, and a local audit. The focused plan is a local desktop password manager with one unlock session for native MCP approvals and project env launches. That session is not implemented: the current desktop and terminal approver unlock separately, and locking the desktop does not stop the approver. Hosted services and broader integrations are optional ideas in [BACKLOG](BACKLOG.md).
