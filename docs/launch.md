<a id="what-this-file-is-not"></a>

<a id="launchmd--the-launch-and-what-has-to-be-true-first"></a>

# Launch material

> This is draft copy for announcing keypaste 1.0.0, the first desktop release, which is not yet published; it describes the source build headed there. Today's download is the CLI and MCP bridge `v0.3.0`, which has `keypaste agent`, a separate `keypaste-mcp` binary and an `Approve? [y/N]` prompt, so a post about `v0.3.0` must use [its own guide](https://github.com/notinferred/keypaste/blob/v0.3.0/docs/demo.md) instead. [FEATURES](FEATURES.md) lists what each version has.

It is not an instruction to publish or contact anyone, and there is no campaign, schedule or announcement gate: the founder chooses whether and where to use it, and only once 1.0.0 is published and installation-verified ([RELEASE](RELEASE.md)).

## 1.0.0 draft

keypaste is a local password manager that keeps your logins, keys and project values in a KeePass-format file on your computer, with no account. AI coding agents use what they need one field at a time, each use approved by you and logged.

It comes as a desktop app for Windows, macOS and Linux, the `keypaste` command-line tool, and an MCP bridge your AI tools start as `keypaste mcp`. An agent asks for one field and says why; you answer in the app or in `keypaste approve` with Deny, Allow once, or for a labelled agent Always allow or Always block. Agent settings say, for each kind of value, whether to ask, allow or block. `keypaste run dev -- npm start` hands a project's variables to a command without a `.env` file. Every request goes into a local, hash-chained log.

KeePassXC opens the same file, and the source is AGPL-3.0. Locking the vault cannot take back a value a program already received.

[Install keypaste](https://github.com/notinferred/keypaste#install), [watch the demo](https://github.com/notinferred/keypaste/blob/main/docs/demo.md), or [read the source](https://github.com/notinferred/keypaste).

## Transcript material

The recorded example uses a fake credential. `scripts/verify-demo.sh` compares this single approval block and the log header with the built binaries; keep their contents exact.

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

```
2 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           agent        item           decision  method
  2026-07-27 09:57:42  claude-code  -              granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/.env  granted   prompt
```

A released value goes to the MCP client, which may keep it; [SECURITY](../SECURITY.md) and [THREATS](../THREATS.md) own the limits a post should link rather than restate.

## Before using this material

Confirm the download, version and supported workflows against [RELEASE](RELEASE.md). Keep installation commands in README rather than copying commands that will become stale. Check the chosen venue's rules and answer feedback within the time actually available; no particular channel or response campaign is required.

Public bug reports and requests belong in the [issue tracker](https://github.com/notinferred/keypaste/issues). Vulnerabilities go to <security@keypaste.com>, under the response policy in [SECURITY](../SECURITY.md).

The signup endpoint stores unconfirmed addresses and sends no confirmation mail. Do not send to that list unless the promised consent flow exists and the recipients have confirmed. Future mail handling is optional work in [BACKLOG](BACKLOG.md).

New suggestions do not become product commitments automatically. Record them in [BACKLOG](BACKLOG.md) for a separate scope decision.
