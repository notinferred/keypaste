<a id="what-this-file-is-not"></a>

<a id="launchmd--the-launch-and-what-has-to-be-true-first"></a>

# Launch material

This is reusable draft copy for the next CLI/MCP release, reviewed against the source on 2026-09-25; its transcript matches the current source, which `v0.3.0` predates (`v0.3.0` prompts `Approve? [y/N]` with a `for N seconds` line). An announcement of `v0.3.0` itself must use the `v0.3.0` [demo](docs/demo.md) recording instead, and when the version that ships this prompt is tagged, re-date this draft to it. It is not an instruction to publish or contact anyone. There is no mandatory campaign, posting schedule or announcement gate. The founder chooses whether and where to use it; any later use must describe the version actually available.

[PRODUCT](docs/PRODUCT.md) defines the focused local desktop product. [STEPS](docs/STEPS.md) owns delivery, and [RELEASE](docs/RELEASE.md) distinguishes source, packages and public downloads. A future desktop announcement must wait for that desktop release and its installation evidence.

## Next release draft

keypaste stores passwords and project environment variables in a local KDBX file and lets an AI client request one credential at a time over MCP.

The current public release is CLI/MCP `v0.3.0`, with downloads for Windows x64, macOS ARM64 and Linux x64/ARM64. It works without an account or service. `keypaste run dev -- npm start` reads an env set and passes it to a child process without writing a plaintext env file.

For agent access, your MCP client starts `keypaste-mcp`. You start and unlock a separate `keypaste agent` in a terminal, which shows credential requests. You answer Deny, Allow once, which releases one field and keeps nothing, or Allow for 1 hour, which lets that connection reuse the approval for the hour; silence for 45 seconds denies access. User-written policy can authorize a matching request without a prompt. The bridge records calls in a local hash-chained audit log and refuses release when the required record cannot be written.

The binaries are unsigned and un-notarized. There is no public desktop download. In source, the desktop and `keypaste agent` share one owner per vault, the unlocked app answers agents in its own prompt window, and the Env profiles screen launches a project's command or a terminal from that session; none of this is published yet.

The intended product is a familiar local password manager with one unlock session for vault use, native MCP approval and project launches. Existing processes retain credentials they already received; neither locking the vault nor expiring an approval erases those copies.

The source is AGPL-3.0. CI checks KDBX interoperability with real KeePassXC on the advertised operating-system families. This is compatibility evidence, not complete KeePassXC feature parity.

[Install the published CLI](https://github.com/notinferred/keypaste#install), [watch the terminal demo](https://github.com/notinferred/keypaste/blob/main/docs/demo.md), or [read the source](https://github.com/notinferred/keypaste).

## Transcript material

The recorded example uses a fake credential. `scripts/verify-demo.sh` compares this single approval block and the log header with the built binaries; keep their contents exact.

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

```
2 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           client       entry                decision  method
  2026-07-27 09:57:42  claude-code  -                    granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/STRIPE_KEY  granted   prompt
```

`request_credential` returns plaintext to the MCP client, which may retain it in context or session files. TTL controls approval reuse, not the downstream credential's validity. The log excludes the released field value, but agent-written names or reasons may contain sensitive text. A writer can recompute the log's hash chain. [SECURITY](SECURITY.md) and [THREATS](THREATS.md) own these limits.

## Before using this material

Confirm the download, version and supported workflows against [RELEASE](docs/RELEASE.md). Keep installation commands in README rather than copying commands that will become stale. Check the chosen venue's rules and answer feedback within the time actually available; no particular channel or response campaign is required.

Public bug reports and requests belong in the [issue tracker](https://github.com/notinferred/keypaste/issues). Vulnerabilities go to <security@keypaste.com>, under the response policy in [SECURITY](SECURITY.md).

The signup endpoint stores unconfirmed addresses and sends no confirmation mail. Do not send to that list unless the promised consent flow exists and the recipients have confirmed. Future mail handling is optional work in [BACKLOG](docs/BACKLOG.md).

New suggestions do not become product commitments automatically. Record them in [BACKLOG](docs/BACKLOG.md) for a separate scope decision.
