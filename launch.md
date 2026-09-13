<a id="what-this-file-is-not"></a>

<a id="launchmd--the-launch-and-what-has-to-be-true-first"></a>

# Launch runbook

These are CLI-era drafts. The initial community introduction requires the working-product gate and founder daily use under [STEPS](docs/STEPS.md), step 3.2. Update component and version claims for the released product before posting; the historical “no released GUI” wording is not a launch requirement.

`docs/PRODUCT.md` §5.3 permits Hacker News, r/selfhosted, r/KeePass, the MCP community and X. Post once per channel. Do not repost, use another account or solicit votes.

lobste.rs was removed from the plan because signup requires an invitation and none is available.

## Before anything goes out

Repository visibility, the demo GIF and security mail delivery were verified on 2026-09-06:

- [x] The repository is public. Step 3.0, done 2026-09-06 at github.com/notinferred/keypaste.
- [x] The demo GIF exists at `docs/demo/keypaste-demo.gif`, 62 KB, in the slot both pages reserve. Step 3.1, done 2026-09-06.
- [x] `security@keypaste.com` receives mail, tested from an outside address 2026-09-06.

The remaining prerequisites and ordering belong to [STEPS](docs/STEPS.md); this historical checklist does not defer release safeguards until after launch. Recheck each venue's current rules before posting. No release announcement goes to the signup list until 5.6's consent flow ships and the recipient confirms.

<a id="what-every-post-draws-on"></a>

## Shared material

Each draft uses the shared claims, links and transcripts below.

PRODUCT v1.2 includes the broader password manager, managed hosting and organizations. Step 3.2 refreshes these drafts for the initial release; 3.11 follows the hosted-release gate. The historical CLI pitch covers a local KDBX vault, process environment injection, scoped agent requests and an audit log.

Describe the combination of a user-owned KDBX file, account-free local use, explicit approvals with bounded reuse or user-written rules, and a local audit log. Avoid “the first”, “the only” and “nobody does this”. D-0036 records alternatives including Keeper, Bitwarden Agent Access SDK, 1Password Environments and `kprun`.

Use the repository, [`docs/demo.md`](docs/demo.md) and [`docs/keepass-and-agents.md`](docs/keepass-and-agents.md). Every post links the demo; installation instructions remain in README so releases do not leave copied commands stale.

`scripts/verify-demo.sh` compares this single dialog block with the built binary. Replace each `[dialog block]` placeholder with it:

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

Use this verified log block for `[log block]`:

```
2 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           client       entry                decision  method
  2026-07-27 09:57:42  claude-code  -                    granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/STRIPE_KEY  granted   prompt
```

D-0035 records medians of ten runs: 71 ms for `keypaste --help`, 248 ms for a `request_credential` round trip against a running approver, and 255 ms for Argon2 during unlock. Quote these as the recorded measurements; no other latency figure is ratified.

D-0038 removed Cyberhaven's 2026 statistic, LayerX's 77%, Netskope's regional splits and a repeated CVSS 9.4 because primary sources did not support the claims. No retained figure measures how often developers give `.env` files to agents. Use only statistics retained in `docs/keepass-and-agents.md`, including in comments.

<a id="the-order-of-the-day"></a>

## Posting order

Start with the communities most likely to catch format or protocol errors:

1. Post to r/KeePass and the MCP community on the same day, then wait at least forty-eight hours.
2. Fix reported factual errors, compatibility failures and unsupported claims in the repository before continuing.
3. Post to r/selfhosted.
4. Post Show HN early in the US working day and remain available to respond.
5. Post the GIF on X when Show HN goes live.

## Show HN

Title (77 characters; HN truncates at 80):

```
Show HN: Keypaste – a KDBX vault that hands an agent one credential at a time
```

Text:

keypaste stores credentials in local KDBX files and provides an MCP server for individual credential requests.

`keypaste-mcp` forwards requests without holding the vault. You start `keypaste agent` in your own terminal to unlock the vault and approve releases. Agent requests cannot trigger master-password prompts. A request appears in that terminal:

[dialog block]

Silence for 45 seconds denies a request. Approval releases one field and permits reuse on that connection for the displayed lifetime. Exposure defaults to `env/` and can be widened only through client configuration. Every call is logged to `~/.keypaste/audit.jsonl` before its response; the returned value is excluded. If the audit cannot be written, access is refused.

The tool returns credentials as text and structured data, which clients can retain in context and session files or place on command lines. TTL limits approval reuse and cannot erase those copies. keypaste sanitizes the reason and labels its author but cannot verify the claim. Policy rules skip human review. A file writer can recompute the audit chain, and hostile programs running as your user remain outside these protections.

Keeper's MCP server prompts before unmasking. Bitwarden announced its then-alpha Agent Access SDK in March 2026. 1Password Environments approves requests and injects credentials without returning them to the model. `kprun` injects KeePass entries and writes a local JSONL log without approval. keypaste combines local KDBX ownership, account-free use, scoped approval and a local audit.

This draft describes the pre-1.0 CLI/MCP release, with unsigned binaries, terminal approvals and no public GUI release. The source is AGPL-3.0. CI checks KeePassXC interoperability in both directions on Linux, macOS and Windows.

Demo, sixty seconds, end to end: [docs/demo.md]. Repo: [repo].

Can a local KDBX tool support host-side injection over MCP without returning the credential to the model? 1Password Environments provides an injection workflow; a value-returning tool exposes its result to client retention.

## r/selfhosted

Title:

```
keypaste: a KDBX vault your coding agent can ask for one credential at a time
```

Body:

Pasting an API key into an agent chat exposes it to the client and its retained conversation.

keypaste uses a KDBX file on your disk that opens in KeePassXC and works without an account or network. Sync it with your existing file-sync tools.

It supports process environment injection and agent credential requests:

`keypaste run dev -- npm start` reads an env set from the vault and injects it into the child process without writing a plaintext env file.

The MCP server forwards credential requests to an approver you started in your terminal:

[dialog block]

Silence for 45 seconds denies a request. Approval releases one field and permits bounded reuse. Every call is recorded in the local hash-chained audit log, readable through `keypaste log`:

[log block]

Local use needs no server, database or container. The audit log stays on the same machine; anyone who can write it can recompute the chain.

This CLI-era draft describes AGPL-3.0 source, terminal approvals and no public GUI release. [docs/replace-dotenv.md] covers migration from `.env`; [docs/demo.md] shows credential approval. Repo: [repo].

Would a hash-chained local JSONL log meet your needs, or do you need an external append-only destination? External log delivery is not implemented and would add network access.

## r/KeePass

Lead with compatibility and request critique. `DECISIONS.md` records the standing instruction to contribute compatibility fixes upstream.

Title:

```
Built a KDBX-compatible tool that gives AI agents one entry at a time — format critique wanted
```

Body:

I maintain keypaste and am seeking feedback on its KDBX compatibility and entry convention.

keypaste reads and writes KDBX4 through KeePassLib, vendored from the KeePass 2.61 netstandard port. The dependency survey found no sufficiently maintained .NET KDBX4 package for this path. Existing vaults need no migration.

CI checks generated vaults against a real `keepassxc-cli` in both directions on Linux, macOS and Windows. The compatibility gate is a permanent requirement.

Its MCP server lets coding agents request a credential. A separate approver you start holds the vault and prompts in your terminal:

[dialog block]

An approval releases one field, with connection-scoped reuse for the displayed lifetime and a local audit record. `kprun` already supports KeePass process injection and local JSONL logging; keypaste adds approval and exposure controls.

Environment variables use `env/<project>`, with one entry per variable: title for its name, password for its value. This keeps variables editable in KeePassXC, and `keypaste run` reads those edits. A project with fifty variables therefore has fifty entries; the alternative is one entry with fifty custom fields.

What compatibility or usability problems does this convention introduce? Entry-per-variable favors KeePassXC editing; custom fields would shorten the tree.

Repo: [repo]. The sixty-second demo: [docs/demo.md].

## MCP community and Discord

Keep this message brief and focused on the protocol question.

Message:

I maintain an MCP server for a local KDBX vault and am reviewing its credential-delivery design.

The client launches `keypaste-mcp`, a forwarding bridge with no vault. The person launches the approver, which unlocks the vault and authorizes requests. This keeps agent requests from triggering master-password prompts.

`list_entry_names` returns names within configured exposure. `request_credential(entry, reason, ttl)` asks for a release. The approver resolves and rechecks the entry, considers live approval, cooldown and policy, then prompts when required. Authorization controls release; resolution can already materialize standard fields in memory. THREATS.md T-8 and T-18 explain that boundary.

Credential results are returned as text and structured data and may be retained by the client or model. 1Password Environments instead injects credentials into a child process. This stdio bridge does not own the child process tree.

Can MCP express a value that a host resolves for execution without rendering it to the model? If that requires host support, what should the server expose?

Repo, if useful: [repo]. Sixty-second demo: [docs/demo.md].

## X

Use seven posts with the recorded GIF first. Keep links in the last two posts. Do not add hashtags or thread markers.

1. Attach the recorded GIF.

> An agent requests an API key from the vault, you approve it, and the release is logged. The five-minute limit controls approval reuse; it cannot erase the agent's copy.

2.

> The vault is a local KDBX file. It works without an account or server and remains readable in KeePassXC.

3.

> The MCP bridge forwards requests without holding the vault. You start the separate approver and enter the master password in its terminal.

4.

> Agent requests cannot trigger a master-password prompt. This avoids teaching you to trust password windows another program could imitate.

5.

> Silence for 45 seconds denies a request. Approval releases one field and permits reuse on that connection for the displayed lifetime. Every call enters the local hash-chained log.

6.

> Returned credentials can reach model context, session files and command lines. TTL cannot erase those copies. The demo shows the boundary: [docs/demo.md].

7.

> This CLI-era release is pre-1.0 and AGPL, with terminal approvals and no public GUI release. Demo: [docs/demo.md]. Code: [repo]. Which credentials would you permit an agent to retain?

## Answering the launch

`docs/STEPS.md` step 3.3 closes when every issue and comment received since the posts is classified and answered.

Reserve six hours after Show HN for responses. Record issues during that window and defer code changes.

Answer every issue and comment. Classify each as a bug, known gap, documentation misunderstanding or design disagreement, and correct misleading documentation.

Move security reports to `security@keypaste.com` immediately and avoid discussing details publicly until resolved. `docs/PRODUCT.md` §3.10 requires prompt, complete disclosure of shipped vulnerabilities.

Update `CHANGELOG.md` as corrections land; the release workflow uses it for release notes. Do not send changelog messages to the signup list, which promises one installation announcement and is not a newsletter.

Label good first issues only with contribution terms available in `CONTRIBUTING.md`.

<a id="the-answers-written-before-they-are-needed"></a>

### Response references

Use the owner documents when answering recurring questions:

| Question | Reference and answer |
| --- | --- |
| Credential retention | `docs/demo.md` and `SECURITY.md`: scope and cached approval limits control release; clients can retain returned values. |
| 1Password comparison | `README.md`: injection avoids returning credentials to the model; compare the storage and account requirements of the documented integration. |
| `kprun` comparison | `README.md`: both support KeePass process injection; keypaste adds approval and exposure controls. |
| Audit tampering | `SECURITY.md`: a file writer can recompute the chain. An independently retained hash with `--expect` detects loss of that record. |
| Policy bypasses human review | `README.md` and `docs/policy.md`: matching rules release fields without prompting. Omit the rule when each release needs review. |
| Prompt injection | `THREATS.md` T-1 and T-2: sanitization limits control and display tricks but cannot verify a reason or remove all malicious meaning. |
| Clipboard retention | D-0056 requests exclusion from Windows Clipboard History and Cloud Clipboard. Third-party managers and RDP can still retain or forward values; see `SECURITY.md` and `THREATS.md` T-19. |
| AGPL | `docs/PRODUCT.md` §3.8: auditable source and copyleft are product commitments. |
| Vendored KeePassLib | D-0007 records the .NET KDBX4 package survey and the maturity requirement behind the choice. |
| Hostile local processes | `SECURITY.md` and `THREATS.md`: processes already running as the user remain outside these protections. |
| Verification quality | D-0038 records observing the gate fail before relying on it. See `scripts/verify-demo.sh`. |

Do not use additional accounts, solicit upvotes, delete criticism or promise features from a single comment. Record new ideas in the `DECISIONS.md` Ideas table.
