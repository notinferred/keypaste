# launch.md — the launch, and what has to be true first

> **Nothing in this file is sent until every box in the next section is ticked.** The copy is written early on purpose: written cold it argues, written on the morning it sells. The questions each post asks are real questions, and the answers are wanted whether or not the post does well.

Five channels, the ones `docs/PRODUCT.md` §5.3 sanctions: Hacker News, r/selfhosted, r/KeePass, the MCP community, X. One post each. No reposting, no second account, no asking anyone to go vote.

lobste.rs was dropped rather than deferred. Signup is by invitation from an existing user, which is the only item this file ever carried with a lead time measured in weeks, and no invitation is in hand. A venue that cannot be reached on the schedule the rest of the launch runs on is not a pending task.

---

## Before anything goes out

The unticked ones are things that are false today. The launch is the moment strangers arrive, and each of these is something a stranger hits before they hit the product.

**The product**

- [x] **3.4 has landed and there is something to install.** `v0.1.0` is published at `https://dl.keypaste.com/v0.1.0/` — four native binaries plus the corresponding source, each with a checksum (D-0041); O-0006 is answered yes (D-0040). Both pages carry per-OS install one-liners, and `.github/workflows/install.yml` runs them verbatim on Ubuntu 2404, macOS 15 and Windows 2025, weekly and on demand, with a corrupted-download negative control on the two Unix legs. Its first real run failed and found two defects in the gate rather than in the documentation (D-0043). The posts below needed no rewrite — none of them links an install command, by design. One limit the green run does not cover: it finds the binary at a known path, so it does not prove a fresh login shell resolves `keypaste` on `PATH`.
- [ ] **The demo GIF exists.** The last thing left in 3.1. `scripts/demo/` is the pipeline, it is WSL-only, it needs a real Claude session and a human keystroke, and it budgets three to eight takes. Both pages already reserve the slot and nothing else moves when it lands.
- [x] **O-0008 and O-0009 are closed or deliberately deferred in writing.** Both were settled by **D-0056**, and they were settled differently because they are different defects. O-0008 is **closed on both front ends**: the CLI now writes through Win32 and sets the formats that keep a copied password out of Clipboard History and Cloud Clipboard, which `clip.exe` could not express. O-0009's `argv` exposure is **deliberately deferred**, in writing and with reasons — `env set p KEY=value` still puts a value where `/proc`, WMI and shell history can read it, because D-0014 judged that refusing the one-liner pushes people to clean shell history by hand or to something worse, and it warns on stderr when it happens. Its one loose end, an escape hatch so a CI job using the inline form on purpose can silence a per-run warning, is stated as still open rather than quietly dropped. **One caveat before this line is used as an answer:** the Windows fix passes its unit tests and has not yet been checked against the actual defect on a real machine — that is step 1.5a's Verify line in `docs/STEPS.md` and it needs a person to press Win+V.

**The links**

- [ ] **The repository is public.** It is private right now, which makes every link in every post below a 404. Tracked as **O-0014** and as step 3.0 (H-0003). CI cost is not part of the argument: it moved to a flat-rate provider when GitHub-hosted billing stopped the jobs starting (D-0042). The real question is that `docs/PRODUCT.md` law 3.8 calls auditable code the trust strategy and every post below sells exactly that, against D-0006's warning that publishing is irreversible because GitHub can serve any commit ever pushed. Answer it before posting, not before tagging.
- [x] **Every published URL points at something that exists**, on both pages, the thanks page, and in `Directory.Build.props` — `RepositoryUrl` and `PackageProjectUrl`, which `PublishRepositoryUrl=true` bakes into SourceLink metadata inside the shipped binaries. Those two have to be right before a tag, because an artifact cannot be edited afterwards.
- [x] **The canonical link is `https://github.com/notinferred/keypaste`** and every post below uses it. Both pages, the thanks page and `Directory.Build.props` point there, and there is no organisation to move to: the repository stays under the founder's account (D-0082).
- [ ] **`blacksmith-*` runners have an answer for pull requests from forks.** CI is the first thing a contributor meets, and a launch produces pull requests from people with no write access. Step **K.5**, which D-0079 moved into `[MVP]` for exactly this reason.
- [ ] **Branch protection on `main` requires the CI checks.** It cannot be set on a private repository on this plan, so today "a red build blocks the merge" is a habit rather than a rule, and merges happen locally where no check is consulted. Once the repository is public this becomes available, and it should be switched on before anyone outside can open a pull request. Step **K.4**.

**The promises already published**

- [x] **keypaste.com's signup does what the page says it does.** **Fixed and verified end to end on 2026-07-28.** Hyperdrive connects as `keypaste_signup_writer`, which holds `INSERT` on `public.signup` and nothing else; as that role every read is refused with 42501. A live submission stores a row and redirects to `/thanks/`, a duplicate is a no-op, the honeypot stores nothing, and a bad address, a foreign `Origin` and a non-form body each get 400. Two defects were found doing it: `wrangler hyperdrive update` silently wipes the `mtls` block on a credential swap, and `ON CONFLICT (email) DO NOTHING` needs SELECT on PostgreSQL 18, which made the Worker's own SQL incompatible with the role it was written for. Details in D-0037 and `site/README.md`. Still open: it is a plain SQL role rather than a managed one, so rotation is by hand.

 For the record of what this item used to fear: the Worker was already deployed while `DECISIONS.md` claimed nothing was, and `public.signup` did not exist — so every submission hit the handler's 503 saying the address was not stored. No address was ever silently dropped, and there was no list for the over-privileged credential to read. Both records were corrected rather than quietly edited.
- [x] **Nothing goes to the list until double opt-in ships.** `DECISIONS.md` D-0037 and the page footer both promise a confirmation first, and the confirmation mail is the relay's job — step **5.6**, `[Launch]`, which does not exist yet. So this box is not "5.6 has shipped"; it is the decision that **no list message is sent at launch at all**. Signups accumulate and are mailed once 5.6 lands. It is tickable today because it is a promise not to act, and a launch is exactly the pressure that breaks such a promise: D-0079 narrowed the box rather than dragging the whole relay into `[MVP]` to satisfy it.
- [ ] **`security@keypaste.com` receives mail**, tested by sending to it, and GitHub's private vulnerability reporting is switched on. `SECURITY.md` names both, and a security contact that bounces on launch day is worse than no security contact. Step **2.4b**, added by D-0079 because this box had a falsifiable criterion and no step behind it.

**The contributor path**

- [x] **O-0002 is decided and `CONTRIBUTING.md` exists.** DCO, not a CLA — **D-0055**, which closed O-0002 on 2026-08-06. AGPL-3.0 is chosen and staying, so the relicensing freedom a CLA buys has nothing to buy here, and its price is a deterrent to the drive-by fix law 5.3 says to want. `CONTRIBUTING.md` says so, and `.github/workflows/dco.yml` checks it on every pull request rather than trusting the page: it judges only the commits a pull request adds, because sign-off began at D-0055 and a gate red against the whole history would be switched off within a week. **One limit, stated rather than discovered:** the workflow's bash is proved against real commit ranges, but no pull request has run it, so the plumbing — the checkout depth, the event context — is unexercised until the first one.
- [x] **`CHANGELOG.md` exists.** Added in 3.4, and the release workflow refuses to publish a tag that has no section in it — docs/PRODUCT.md §4.7 as a gate rather than an intention.

**The venues**

- [ ] **Each venue's current rules have been read in the week before posting.** Not summarised here, because a rule copied into this file is a rule that goes stale quietly. Self-promotion policies on both subreddits, the Show HN guidelines, and whatever the MCP community asks of people posting their own tools.

---

## What every post draws on

Written once here. The posts below reference these rather than restating them, so there is one place to correct.

**The pitch these posts were written to** — `docs/PRODUCT.md` v1.0 §1; v1.1 (D-0061, D-0075) now says "password manager and secrets manager", and the posts stay on the developer wedge until 3.11 rewrites them for the app: stop pasting secrets into chats. keypaste is a local-first, KDBX-compatible vault that stores your passwords and env variables, injects them into your projects, and lets AI agents like Claude request exactly one credential — with your approval, scoped access, and a full audit trail — without ever seeing your vault.

**The claim, and its exact limits.** The differentiator is the combination, never novelty: an ordinary KDBX file you own, no account and no server anywhere, a person answers each request, and the log never leaves your disk. Each of the others gives up at least one of those. **No post contains "the first", "the only", or "nobody does this"** — `D-0036` lost that argument already, against a field that includes Keeper, Bitwarden's Agent Access SDK, 1Password Environments and `kprun`, and it lost it in private rather than in a comment thread.

**The three links.** The repository, [`docs/demo.md`](docs/demo.md) for the sixty seconds end to end, and [`docs/keepass-and-agents.md`](docs/keepass-and-agents.md) for the argument. Every post links the demo. No post links an install command, so nothing here needs rewriting when 3.4 changes the install block.

**The dialog.** This block is held to what the shipped binary prints by `scripts/verify-demo.sh`, which is why it appears exactly once in this file. Where a post wants it, the post says `[dialog block]` and you paste this:

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

**The log.** Same, and the same rule:

```
2 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           client       entry                decision  method
  2026-07-27 09:57:42  claude-code  -                    granted   exposure
  2026-07-27 09:57:42  claude-code  env/demo/STRIPE_KEY  granted   prompt
```

**Numbers that are safe to quote**, all measured rather than estimated, from `D-0035`: `keypaste --help` at 71 ms, a full `request_credential` round trip against a running approver at 248 ms, and the Argon2 pause when you unlock at 255 ms. Medians of ten runs. Nothing else in the repository has a ratified latency figure, so nothing else gets quoted as one.

**Figures that are cut and stay cut.** `D-0038` re-checked every statistic in the essay against its primary source and four did not survive. Cyberhaven's 2026 report, LayerX's 77%, Netskope's regional splits and a widely repeated CVSS 9.4 were all removed for want of a source that says what they were said to say. Nothing quantifies how often a developer hands a `.env` to an agent, so that claim stays qualitative in every post. If a number is not in `docs/keepass-and-agents.md`, it does not go in a comment either.

---

## The order of the day

Smallest and most expert first, so that whatever is wrong is found by someone with a reason to be generous about it, and found before it is found on a front page.

1. **r/KeePass and the MCP community, the same day.** The two audiences most likely to catch a real mistake: one knows the format, the other knows the protocol. Nothing else goes out for at least forty-eight hours.
2. **Stop.** If either thread surfaces a factual error, a compat problem, or a claim that does not hold, fix it in the repository before continuing. This step is the reason for the ordering, and skipping it wastes it.
3. **r/selfhosted.** Wider, friendlier, and the first real traffic.
4. **Show HN**, early in the US working day. Then stay at the keyboard.
5. **X, timed to the Show HN post**, GIF first.

---

## Show HN

**Title** (77 characters; HN truncates at 80):

```
Show HN: Keypaste – a KDBX vault that hands an agent one credential at a time
```

**Text:**

keypaste is a local-first vault that speaks KDBX — the KeePass format — and ships an MCP server, so a coding agent can ask for one credential instead of asking you to paste one into the chat.

The shape is two processes, and the split is the whole design. `keypaste-mcp` is what the agent talks to: it holds no vault and decides nothing. `keypaste agent` is a process you start in your own terminal, and it is the only thing that ever sees your master password — so nothing an agent does can make a password prompt appear, because keypaste never gives you a reason to expect one. The request surfaces in your terminal:

[dialog block]

Say nothing for 45 seconds and that is a no. What comes back is one field of one entry, for a lifetime you were shown, scoped to that one connection. What an agent may even *name* is default-deny: the `env/` subtree and nothing else, widened only by a glob in a config file you wrote. Every call — granted, denied or malformed — appends a hash-chained line to `~/.keypaste/audit.jsonl`, the value is never in it, and the line is written before the agent is answered. The log is a precondition rather than observability: if it cannot be appended to, the bridge refuses to start at all instead of running and failing to record.

What it does not do, before anyone has to ask. The credential lands in the model's context twice, because `request_credential` returns it as text and as structured data, and your client stores both in its session file. Then the agent puts it on a command line. keypaste's guarantee ends at the moment of release; the TTL and the log are what it offers instead of a promise it could not keep. The reason the agent gives you is a claim — keypaste strips the control characters, caps the length and labels whose words they are, and it cannot tell you whether the sentence is true. Write a policy rule to stop being asked about a routine case and no human sees those requests at all: the point of the rule, and the cost of it. The audit log is tamper-evident, not tamper-proof. And a process already running as your user is outside all of this, everywhere.

It is also not alone here, and pretending otherwise would be a poor way to open a thread. Keeper's MCP server prompts before it unmasks. Bitwarden published an Agent Access SDK in March with the same request-and-approve shape, though it is alpha. 1Password's Environments MCP server asks for approval and then never hands the credential over at all — a genuinely different answer, and on that one axis a stronger one. `kprun` already injects KeePass entries into a child process and writes a local JSONL log, without the approval step. What is keypaste's is the combination: an ordinary KDBX file you own, no account and no server anywhere, a person answering each request, and a log that never leaves your disk.

Pre-1.0 and it says so. No released GUI yet, and the approval prompt is that terminal rather than a native dialog. AGPL-3.0. Everything it writes opens in KeePassXC, and that is proved in both directions against a real `keepassxc-cli` on Linux, macOS and Windows on every push.

Demo, sixty seconds, end to end: [docs/demo.md]. Repo: [repo].

**The question I actually want answered:** is release-with-a-TTL the wrong trade? 1Password's answer — approve the request, then inject the value into the child process and never return it to the model at all — is structurally stronger than mine, and it is available to them because the vault is theirs. Is there a version of that a local vault can do over MCP as the protocol stands, or is "the secret ends up in the transcript" simply the price of a tool that returns values?

---

## r/selfhosted

**Title:**

```
keypaste: a KDBX vault your coding agent can ask for one credential at a time
```

**Body:**

The thing that started this: agents do not usually go rummaging for your credentials. They stop and politely ask *you* to paste the key into the chat window. And you do, and now it is in a transcript on somebody's server.

keypaste is the local-first answer to that. Your vault is a KDBX file on your disk — the KeePass format, openable in KeePassXC. There is no account, no service holding your secrets, and no network required for anything. Sync it with whatever you already run.

Two things it does beyond being a vault:

`keypaste run dev -- npm start` reads an env set out of the vault and injects it into the child process, so the `.env` file stops existing. Nothing is written to disk at any point in that.

And it ships an MCP server, so Claude or another agent can request one credential. The request goes to a separate process you started in your own terminal, which is the only thing holding the vault:

[dialog block]

45 seconds to answer, silence is a no, one field for a lifetime you were shown. Every call lands in a hash-chained `~/.keypaste/audit.jsonl` — granted, denied, malformed, all of it — and `keypaste log` reads it back:

[log block]

For this crowd specifically: **there is nothing to self-host.** No server component, no database, no container. That is the point rather than a missing feature — but it does mean the audit log is a file on the same machine as the thing being audited, which is the honest limitation. It is tamper-evident, not tamper-proof: anyone who can write the file can recompute the chain.

AGPL-3.0. Pre-1.0 — no released GUI yet, and the approval prompt is a terminal rather than a native dialog. Replacing a `.env` takes about five minutes: [docs/replace-dotenv.md]. The sixty-second version of the agent flow: [docs/demo.md]. Repo: [repo].

**The genuine question:** is a hash-chained local JSONL good enough for you, or would you want the log shipped somewhere append-only that the machine cannot rewrite? I have deliberately not built that, because the moment the log leaves your disk keypaste stops being a thing with no network surface — but I would rather hear that I have the trade backwards from people who run their own infrastructure than guess.

---

## r/KeePass

Respectful, compat-first, and it leads with the gate rather than the product. the Ideas table in `DECISIONS.md` already carries the standing instruction for this community: contribute compat fixes upstream, never fork-and-fight. This post is an ask for critique, not an announcement.

**Title:**

```
Built a KDBX-compatible tool that gives AI agents one entry at a time — format critique wanted
```

**Body:**

This is my own project, so flag it as self-promotion if that is the rule here. What I actually want from this subreddit is the compatibility critique, because you are the people who will spot it.

First, what it is not. It is not a fork of KeePass or KeePassXC, it is not a new format, and it does not ask you to migrate anything. It reads and writes ordinary KDBX4 through KeePassLib vendored from the KeePass 2.61 netstandard port, because after surveying what exists for .NET there is no maintained KDBX4 package worth putting on the secret path. Files it writes are meant to be indistinguishable from files KeePass wrote.

That claim is gated rather than asserted. CI runs a real `keepassxc-cli` against generated files on Linux, macOS and Windows on every push, in both directions — keypaste writes and KeePassXC reads, KeePassXC writes and keypaste reads. It was made permanent by decision when it was added rather than left as a convenience, and it runs on every push rather than nightly or on demand.

What it adds is an MCP server, so a coding agent can request one credential from the vault instead of the current practice, which is that the agent asks you to paste your key into a chat window. A separate process you start in your own terminal holds the vault and asks you:

[dialog block]

One field of one entry, for a lifetime you were shown, and a hash-chained local log of every request. Prior art is worth naming: `kprun` already injects KeePass entries into a child process and writes a local JSONL log; what is different here is the approval step and the scoping.

**The question, and it is the reason for the post.** For environment variables I use a convention rather than a custom field: a group `env/<project>`, one entry per variable, title is the variable name and password is the value. That was chosen so the whole thing stays editable in KeePassXC with no keypaste involved — you can add a variable in the GUI and `keypaste run` picks it up. But it does mean a project's env set is fifty entries in a group rather than one entry with fifty custom string fields, which is the other obvious design.

Does that abuse the format in a way that will bite people later? Custom string fields would be tidier in the tree and worse in the GUI. I picked GUI-editable and I am not confident it was right.

Repo: [repo]. The sixty-second demo: [docs/demo.md].

---

## MCP community and Discord

Shorter, protocol-level, and it opens with the design question rather than the product. This is a message in a channel, not an announcement post — keep it to something a person reads without scrolling.

**Message:**

Built an MCP server for a local KDBX vault and want to put one design question to people who know the protocol better than I do.

The shape: two processes. `keypaste-mcp` is the stdio server the client spawns, and it holds no vault and decides nothing — it forwards. A separate process the human started holds the vault and does the deciding. The split exists so that nothing the agent does can cause a master-password prompt to appear; if the bridge could ask for a password, a malicious client could ask for one too, and the human would have no way to tell them apart.

Two tools. `list_entry_names` returns names, never values, and only within a default-deny glob. `request_credential(entry, reason, ttl)` prompts a human. The order the request is evaluated in is the security property: resolve the entry, re-check it against the exposure globs, then the grant cache, then the cooldown, then the policy file, then ask a person — and read the field out of the vault last of all, after the yes. A request that is going to be refused never has its field read, so a secret is never in memory for a call that was about to be denied.

**The question.** MCP tool results come back into the model's context, so a credential this returns is in the transcript and in the client's session file, twice over — text content and structured content. 1Password's Environments server sidesteps this by never returning the secret: it injects into the child process instead. I cannot do that from a stdio server that does not own the process tree.

Is there a way to express "act on this value, do not show it to the model" in the protocol as it stands? A result the host substitutes but never renders, or a reference the host can resolve at exec time? Or is returning-the-value simply what a tool result is, and the answer is that credential tools should be hosts rather than servers?

Repo, if useful: [repo]. Sixty-second demo: [docs/demo.md].

---

## X

Seven posts. The GIF carries the first one; nobody reads past it otherwise. No hashtags, no thread emoji, and no announcing that it is a thread. Links only in the last two, because the timeline punishes early links.

**1** — GIF attached, the recorded demo.

> Your coding agent needs an API key. It asks you to paste one into the chat.
>
> Here is the other version: it asks the vault, you get one prompt, one credential goes out for five minutes, and there is a line in a log about it.

**2**

> The vault is a KDBX file on your disk. KeePass format. No account, no server, nothing to sign up for. If keypaste disappears tomorrow you open the same file in KeePassXC and carry on.

**3**

> Two processes, and the split is the point.
>
> The MCP server the agent talks to holds no vault and decides nothing.
>
> A separate process you started holds the vault. It is the only thing that ever sees your master password.

**4**

> So no agent, and nothing an agent does, can make a password prompt appear on your screen.
>
> If it could, a malicious client could draw one too, and you would have no way to tell.

**5**

> Silence for 45 seconds is a no. One field of one entry, for a lifetime you were shown, scoped to one connection. Every call — granted, denied, malformed — is a hash-chained line in a local log.

**6**

> The honest part: the credential ends up in the model's context, twice, because that is what an MCP tool result is. Then the agent puts it on a command line.
>
> The TTL and the log are what I offer instead of a promise I could not keep.

**7**

> Pre-1.0, AGPL, no released GUI yet.
>
> Sixty seconds end to end: [docs/demo.md] Code: [repo]
>
> The question I keep going back and forth on: which agent would you actually let hold a live credential for 300 seconds?

---

## The fourteen days after

`docs/STEPS.md` Stage 3 — respond to every issue and every comment for two weeks straight. That is the whole commitment; the rest of this section is what makes it survivable.

**Day 0.** Six hours at the keyboard after the Show HN post, because that is the window. Nothing else scheduled that day. Do not ship code during it — write the issue down and answer the person.

**Every day, days 1–14.** Every issue answered, every comment answered, including the dismissive ones and the ones that are right. Triage into: a real bug, a real gap already conceded in writing, a misunderstanding the docs caused, or a disagreement about the design. The third category is the valuable one, because it is the only one where the fix is free.

**Security reports leave the thread immediately.** "Please send that to security@keypaste.com so it is not public while I look at it" and nothing more in public until it is resolved. `docs/PRODUCT.md` §3.10 — disclose fast and fully if something real ships.

**Weekly, twice.** A changelog entry in `CHANGELOG.md`, which is also what the release workflow reads for the release notes, so the entry and the announcement cannot drift. **Not to the email list.** The page promises one message when there is something to install, and says in as many words that it is not a newsletter. Turning the list into a changelog feed would break a promise made in writing to people who signed up before there was a product.

**Good first issues get labelled** once `CONTRIBUTING.md` exists, and not before — a labelled issue is an invitation, and an invitation with no contribution terms behind it wastes the first contributor's time.

### The answers, written before they are needed

Every objection below is one the repository already concedes somewhere. Answering from the file rather than from memory is what keeps the answers consistent at hour six.

| What they say | Where the honest answer already lives |
| --- | --- |
| "The secret ends up in the model's context anyway, so what did this buy?" | `docs/demo.md` honest limits, `SECURITY.md`. It buys scope, a lifetime, and a record. It does not buy secrecy after release, and nothing here claims it does. |
| "1Password's approach is better." | `README.md` says so, unprompted: a genuinely different answer, and on that axis a stronger one. Agree, then say what it costs — their vault, their account, their service. |
| "Isn't this just kprun?" | `README.md` names `kprun` before anyone else can. The difference is the approval step and the scoping. Say that, and link them. |
| "Tamper-evident is not tamper-proof." | Correct, and `SECURITY.md` says it in those words. Anyone who can write the file can recompute the chain. `--expect <hash>` is the partial answer. |
| "A policy rule means no human sees it." | `README.md`: the one path that hands an agent a credential with nobody watching. The point of the rule and the cost of it. If you want a human, do not write the rule. |
| "Prompt injection — the reason string is attacker-controlled." | `THREATS.md` T-1 and T-2. Sanitization removes mechanism, not meaning. The dialog labels whose words they are and no filter can decide whether a sentence is true. |
| "Windows clipboard history keeps the secret." | Not any more, on either front end — O-0008 is closed by D-0056. Say what closed and what did not: first-party Clipboard History and Cloud Clipboard skip the value, because the formats are a request well-behaved consumers honour; third-party clipboard managers decide for themselves and mostly do not, and RDP redirection hands it to another machine. `SECURITY.md` and `THREATS.md` T-19 both state it that way. Do not shorten this to "we fixed it". |
| "Why AGPL?" | `docs/PRODUCT.md` §3.8. Auditable code is the trust strategy for an unknown founder, and copyleft is what stops a closed cloud clone of a tool whose whole pitch is that it is local. |
| "Why vendor KeePassLib instead of using a package?" | `D-0007` is the survey. Nothing maintained exists for .NET with KDBX4 support; the clean-looking options had days of commit history. Maturity decided it, not licensing. |
| "This does nothing against malware already on my machine." | True, and it is the blanket concession in `SECURITY.md` and `THREATS.md`. A process running as your user is outside all of it. Do not defend this one — agree. |
| "How do I know your CI actually checks anything?" | `D-0038`: the gate was watched failing before it was trusted, because a gate never observed failing is not known to be a gate. Point at `scripts/verify-demo.sh`. |

**What not to do, for fourteen days.** No second account, no asking anyone to upvote, no deleting a critical comment, no arguing with someone who is right, and no shipping a feature because one comment asked for it. New ideas go to the Ideas table in `DECISIONS.md` — that is what it is for, and a launch is exactly when that rule is hardest to keep.

---

## What this file is not

It is not a public page. It links nothing and nothing links it; the audience is one person on one morning.

The dialog and the log above are held to the shipped binaries by `scripts/verify-demo.sh`, the same gate that holds `docs/demo.md`, `README.md`, `site/public/index.html` and `docs/keepass-and-agents.md`. That is why each appears exactly once here. If a post needs a shorter version, it does not get one — it links the demo instead.
