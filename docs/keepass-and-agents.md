# Your KeePass vault can't talk to AI — and everyone is pasting secrets into chats instead

> CLI launch essay. The cited reports and announcements describe their stated 2025–2026 periods;
> they are not an exhaustive current market inventory. Recheck comparisons before publishing a new
> campaign. [PRODUCT](PRODUCT.md) owns the broader password-manager direction, [FEATURES](FEATURES.md)
> owns the dated capability comparison, and [RELEASE](RELEASE.md) owns current downloads.

The failure mode is not the one people picture. Ask a coding agent to deploy something and it does not usually go rummaging through your disk for credentials. It does something far more ordinary: it stops, and it politely asks *you* to paste the key into the chat window.

And you do. [Harmonic Security](https://www.harmonic.security/resources/what-22-million-enterprise-ai-prompts-reveal-about-shadow-ai-in-2025), a data-loss-prevention vendor watching its customers' browsers, found 5,903 instances of credentials in 22.4 million enterprise AI prompts sent during 2025; among exposures from coding tools, 12.8% were API keys or tokens. The supply side matches. GitGuardian counted [28.65 million new hardcoded secrets](https://blog.gitguardian.com/the-state-of-secrets-sprawl-2026/) in public GitHub commits that year, with secrets for AI services up 81%. Further down that report is the number aimed squarely at this crowd: 24,008 secrets sitting in MCP configuration files, 2,117 of them still valid — largely because the setup guides tell you to put the key in the config.

## Your password manager probably can, actually

**Password managers already offer agent integrations.** The linked materials describe [1Password's Environments MCP server](https://www.1password.dev/environments/mcp-server), [Bitwarden's March 2026 Agent Access SDK announcement](https://bitwarden.com/blog/introducing-agent-access-sdk/) and [Keeper's MCP server](https://github.com/Keeper-Security/keeper-mcp-golang-docker). Their approval and secret-delivery models differ; evaluate the version and deployment you would actually use before comparing them.

These integrations do not establish that an existing `.kdbx` file can be used directly with the same workflow. Hosting also varies: [Bitwarden supports self-hosted organizations](https://bitwarden.com/help/self-host-an-organization/), so vendor-held storage is not a universal requirement. keypaste's concrete contribution is a local, account-free approval bridge over the KDBX file you already own; it does not depend on claiming that no other agent integration exists.

## Why the vault stayed offline

Local vault ownership comes with an established security model. [KeePass's security documentation](https://keepass.info/help/base/security.html) discusses keyloggers, clipboard monitoring, process memory and attacks on encrypted database files. KeePassXC also documents a [build option to disable networking](https://keepassxc.org/docs/#faq-security-no-network). That option is distinct from claiming that every default build has no network functionality.

But the ecosystem already answered this essay's question once. When browsers needed credentials out of a KeePassXC vault, the first attempt was an HTTP server on localhost. KeePassXC [replaced it in 2018](https://keepassxc.org/blog/2018-02-28-2.3-released/) with an extension that reaches the vault [through a Unix domain socket or a named pipe](https://github.com/keepassxreboot/keepassxc-browser) — no network, no port — and a [Confirm Access dialog](https://github.com/keepassxreboot/keepassxc/blob/develop/docs/topics/BrowserIntegration.adoc) where you tick which credentials the page may have, with Remember offered as an option rather than assumed.

Per-request human approval over a local channel is not something keypaste invented. It is the KDBX ecosystem's existing answer to *another program wants a credential*, and an agent is another program.

## What arrived instead

**The other program got far more capable, and far more suggestible.** In August 2025 a compromised release of the `nx` build tool [harvested 2,349 credentials into 1,079 attacker-created repositories](https://blog.gitguardian.com/the-nx-s1ngularity-attack-inside-the-credential-leak/) in a fifteen-hour window — and went looking for locally installed AI CLIs, invoking Claude, Gemini and Q [with their safety flags turned off](https://www.wiz.io/blog/s1ngularity-supply-chain-attack) to hunt for more on its behalf. A third of the infected machines had one. Six months later, [CVE-2026-21852](https://github.com/advisories/GHSA-jh7p-qr78-84p7): Claude Code applied a repository's own settings file, which could point `ANTHROPIC_BASE_URL` at someone else's server, and sent the user's API key there before showing the trust prompt. Cloning a repository was enough. And an agent that reads an untrusted string can be [talked into acting on it](https://invariantlabs.ai/blog/mcp-github-vulnerability).

None of that is an argument that agents are bad. It is an argument about blast radius. An agent's reach is whatever it can read, and the thing we hand it is a `.env` file: every credential the project has ever needed, in plaintext, all at once, with no record of which ones were used.

## One credential, one question, one line in a log

**keypaste makes releasing a credential something that happens one at a time, in front of you.** It is two processes, and the split is the design. `keypaste-mcp` is the MCP server your client launches; it holds no vault and decides nothing. `keypaste agent` is the one you start yourself, in your own terminal, and it is the only thing in the agent path that ever sees your master password. Nothing an agent does can make a password prompt appear — not because a check forbids it, but because there is no vault code in the bridge at all. Which matters, since any program on your machine can draw a convincing one.

An agent cannot even *name* an entry you did not expose: the default is the `env/` subtree and nothing else, and widening it takes a glob you typed into your own client config.

Inside the approver, the order is the security property. Resolve the entry, re-check it against the exposure globs, look for a live grant, honour a refusal you just gave, consult your policy file if you wrote one — and read the field for release only after authorization succeeds. The vault is already unlocked in this process; refusing authorization avoids reading the requested field for release, not all prior vault decryption.

What you approve is one field of one entry, with cached reuse scoped to that connection and lifetime. The default answer is no, and so is silence: forty-five seconds and it is denied. Every tool call — granted, denied, malformed — appends a hash-chained line to `~/.keypaste/audit.jsonl` before the answer goes back. The returned field value is not added to the log; names and reason excerpts are logged metadata, so keep secrets out of them. If the log cannot be written, the call is refused.

## What it looks like

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  client   claude-code
  entry    env/demo/STRIPE_KEY
  field    password
  for      300 seconds

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

Approve? [y/N]
```

One keystroke later the deploy runs, and the exchange is two lines you can read back:

```
2 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           client       entry                decision  method
  2026-07-27 09:57:42  claude-code  -                    granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/STRIPE_KEY  granted   prompt
```

`exposure` is a listing your globs allowed; `prompt` means a person was shown that exact request and answered it. [Claude asks for a key, you approve, the deploy runs](demo.md) is the whole thing end to end, in about sixty seconds.

## What this does not do

`request_credential` returns the credential as text and structured data. A client may send those results to a remote model, retain them in its session file and put the value on a command line. TTL bounds cached approval reuse; it cannot erase those copies or revoke the credential at its provider. The local audit records the release, not every later use.

The reason the agent gives you is a claim. keypaste strips the control characters, caps the length and labels whose words they are — it cannot tell you whether the sentence is true. Write a policy rule to stop being asked about a routine case and no human sees those requests at all: the point of the rule, and the cost of it. The audit log is tamper-evident, not tamper-proof — anyone who can write the file can recompute the chain. And a process already running as your user is outside all of this, everywhere in keypaste.

## The stance

Your vault is a file on your disk. No account to create, no service holding your secrets, and nothing here needs a network. It is an ordinary KDBX4 file, so it opens in KeePassXC — proved in both directions against a real `keepassxc-cli` on all three operating systems, on every push, by a gate nobody is allowed to soften. If keypaste disappears tomorrow, your data does not. It is AGPL-3.0, because a tool that handles secrets should not ask to be trusted on faith.

It is also pre-1.0 and says so: unsigned binaries, no released GUI, and a terminal prompt rather than a native dialog; [RELEASE](RELEASE.md) records that dated status. [`kprun`](https://github.com/numikel/kprun) is another KDBX-oriented project to evaluate for process injection. keypaste combines a KDBX file you own, account-free local use, explicit approvals with bounded reuse or rules you wrote, and a local audit. Managed hosting is planned as an optional experience; it does not remove that local path.

The vault stayed offline for good reasons. The agents did not wait. This is an attempt to connect the two without giving up the reasons.
