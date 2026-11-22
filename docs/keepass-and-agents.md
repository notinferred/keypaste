<a id="your-keepass-vault-cant-talk-to-ai--and-everyone-is-pasting-secrets-into-chats-instead"></a>

# KeePass and AI agents

A coding agent that needs a credential often asks you to paste it into the chat, where it stays. keypaste lets the agent ask your KeePass vault for one value instead, and you decide each time.

> This page describes the source build headed for 1.0.0. The published `v0.3.0` differs ([FEATURES](FEATURES.md)).

<a id="the-stance"></a>

## What stays the same

- **Your file.** The vault is a KDBX file on your computer, with no account or network. KeePassXC opens it ([compatibility tests](FEATURES.md#keepassxc-compatibility)).
- **Your items.** They stay ordinary KeePass entries ([what KeePassXC sees](replace-dotenv.md#what-does-keepassxc-see)).
- **Where it lives.** An existing file can stay where it is ([below](#using-your-keepass-file)).

<a id="one-credential-one-question-one-line-in-a-log"></a>

## What changes

**Agents can ask.** Your AI tool starts `keypaste mcp`, a bridge that holds no vault. You unlock the vault where you approve: in `keypaste approve` or the desktop app. An agent cannot make keypaste ask for the master password.

**An agent sees names, not values.** It sees each item's title, group, tags and URL, and cannot name an item outside that.

**You decide each value.** The agent's [settings](policy.md) set each kind of value to Ask, Allow or Block, and Ask is the default. Silence for 45 seconds denies ([Approvals](approvals.md)).

```
────────────────────────────────────────────────────────────
keypaste: an agent is asking for a credential.

  agent    claude-code (claude-code)
  item     .env
  group    env/demo
  field    STRIPE_KEY

  the agent says it needs this because:
    deploy the billing service to staging

  That sentence was written by the agent, not by keypaste. Treat it as a claim.

[d] deny  [o] once  [a] always  45s ›
```

**Every call is logged.** Each goes to a local, hash-chained audit log before the answer, without the value. If the log cannot be written, the call is refused.

```
2 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           agent        item           decision  method
  2026-07-27 09:57:42  claude-code  -              granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/.env  granted   prompt
```

[The demo](demo.md) runs this flow in about sixty seconds.

## Using your KeePass file

To keep a file where it is:

```
keypaste import old.kdbx --in-place
```

From then on `--vault old.kdbx`, `KEYPASTE_VAULT` or `keypaste use old.kdbx` opens it.

To copy it into the vault you already use:

```
keypaste import old.kdbx --vault mine.kdbx --dry-run
keypaste import old.kdbx --vault mine.kdbx --into old
```

- The source file is only read, never written.
- Entries keep their fields, attachments, tags, icons, times and history, under `--into`.
- If one row cannot land, nothing is written.

The desktop's Import from another app does the same, and takes other password managers' exports.

## Why approve locally

Credentials leak through AI prompts and [MCP configuration files](https://blog.gitguardian.com/the-state-of-secrets-sprawl-2026/), and malware has run installed coding agents to search for more, as in the [2025 `nx` compromise](https://blog.gitguardian.com/the-nx-s1ngularity-attack-inside-the-credential-leak/). keypaste applies KeePassXC's browser model, another program asking over a local channel and you confirming, to agents.

<a id="your-password-manager-probably-can-actually"></a>

Other password managers offer agent access with their own approval and delivery models. keypaste is an account-free, local approval bridge over a KDBX file.

<a id="what-this-does-not-do"></a>

## Limits

- A released value goes to the agent's AI tool, which may send it to a remote model or keep it. keypaste cannot erase those copies or revoke the credential.
- The log records the release, not later use.
- keypaste cannot verify the agent's reason.

[SECURITY](../SECURITY.md) and [THREATS](../THREATS.md) state the guarantees and limits. Today's download is the pre-1.0 CLI and bridge ([RELEASE](RELEASE.md)).
