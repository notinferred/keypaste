# Security Policy

keypaste handles secrets. Trust is the only asset the project has, so security reports are treated as the highest-priority work in the repository.

## Reporting a vulnerability

**Email `security@keypaste.com`.** Please do not open a public issue, discussion, or pull request for a security problem.

Include whatever you have: affected version or commit, a description of the issue, reproduction steps or a proof of concept, and the impact you believe it has. Reports in any language are fine.

If you would rather report anonymously, that is fine too — no identifying information is required, and no report will be ignored for lack of it.

## What to expect

| | |
| --- | --- |
| Acknowledgement | within 72 hours |
| Initial assessment | within 7 days |
| Fix or mitigation plan | communicated as soon as it exists, with an honest timeline |
| Coordinated disclosure | up to 90 days, shortened by agreement or if the issue is being exploited |

You will be credited by name or handle in the advisory and changelog if you want to be, and left out if you do not.

There is no bug bounty. keypaste is pre-1.0 and unfunded; this is stated plainly rather than implied by silence.

## Disclosure commitment

Per docs/PRODUCT.md §3.10: if keypaste is breached, or a serious bug ships, it will be disclosed fast and fully — what happened, what was exposed, what changed. No quiet patches for security-relevant bugs.

## Scope

In scope: anything in this repository — the core library, the CLI, the MCP bridge, the build and release pipeline, and the dependency chain on the secret path.

The agent bridge has its own threat model in [THREATS.md](THREATS.md): prompt injection through entry names, confused-deputy attacks by a client that cannot be authenticated, audit log tampering, and what the locked-vault posture of the current version does and does not buy. It is explicit about which of those are mitigated today and which are still open, and it does not repeat what is here.

Also in scope: the signup endpoint on keypaste.com — `site/src/worker.js` and everything it touches. It accepts other people's email addresses and stores them, so an injection, an authz mistake, or a way to read the list back is exactly the report we want. The static content of that site is out of scope, as is any deployment of keypaste that a third party operates. Vulnerabilities in upstream dependencies should be reported upstream first; tell us too, so the pinned version can be moved.

## Supported versions

| Version | Supported | Notes |
|---|---|---|
| `main` | Yes | Always the first thing a fix lands on |
| `0.1.x` | Yes | The current release line |
| Anything older | No | There is nothing older yet |

Pre-1.0, this is a short table on purpose. There is no long-term support line and there will not be one before 1.0; a fix goes onto `main` and into the next tag, and older tags are not patched.

## Verifying a release

Binaries are published to `https://dl.keypaste.com/v<version>/`, with a `SHA256SUMS` file, a per-asset `.sha256`, and the corresponding source for that tag (AGPL-3.0 section 6). Every one of them is produced by `.github/workflows/release.yml`, which runs this repository's full gate suite — including both directions of the KeePassXC compatibility check — against the exact binary it uploads, not against a rebuild.

Two things to know before you rely on that:

**The binaries are unsigned and un-notarized.** On macOS this means Gatekeeper quarantines the download and the documented install includes a line that removes the quarantine attribute; on Windows, SmartScreen warns. This is a known gap, not an oversight — it is tracked as O-0010 in [DECISIONS.md](DECISIONS.md), with the costs written down.

**The checksum proves integrity, not authenticity.** It is served from the same origin as the archive, so anyone able to replace one can replace the other. It protects against a corrupted or truncated download and against nothing else. Verifying provenance needs a signature, which does not exist yet. If you want the strongest available assurance today, build from source — the instructions are in the README and the dependency closure is pinned by content hash in `packages.lock.json`.

The release pipeline is in scope for reports, as the section above says.

## Design commitments worth knowing before you test

These are constitutional (docs/PRODUCT.md §3) and a violation of any of them is a valid report:

- The vault master key never leaves the local process.
- Agents never receive the vault — only one credential, one scope, one TTL, after explicit human approval or a policy the human wrote. Default is deny.
- Every agent access is logged locally, immutably, in human-readable form.
- No secret ever touches disk unencrypted by keypaste's doing.
- No telemetry on secret content or entry names, ever.
- All cryptography comes from mature audited libraries implementing the KDBX4 spec. No custom cryptography is written here.
- Every error path in the agent bridge results in denial, not exposure.

### The desktop app's master password field

The GUI is weaker here than the CLI is, in a way worth stating plainly rather than leaving to be discovered.

`ConsoleSecretPrompt` never forms a `string` at all: it reads the terminal a character at a time into a buffer that is overwritten when it is done. The desktop app cannot match that exactly, because its characters arrive from the toolkit as `string`s the runtime will not let anyone wipe.

What the app does do:

- **The password is not typed into a `TextBox`.** Avalonia's `TextBoxAutomationPeer` returns the control's text through the accessibility layer with no exception for password fields, and `Avalonia.FreeDesktop.AtSpi` — a session-bus service — is in the app's dependency closure. A `TextBox` master password would be readable by another process on the same machine. `TextBox` also keeps an undo stack whose entries each hold a `string`, which is a retained history of partial passwords.
- **The control holds no password.** `MaskedInput` reports one character at a time and stores nothing; what it draws is derived from a count. Its accessibility peer implements no value pattern, so there is nothing for the accessibility layer to return.
- **The buffer lives in a view model that is disposed on every path out** — a successful unlock, a wrong password, and locking — and disposal overwrites it. The wrong-password path is the one that happens most and it is covered by a test.
- **The password reaches Argon2 as a span**, never as a string, so no copy of the whole password is made on the way in.

What remains, measured rather than assumed:

- **Each keystroke arrives as a one-character `string`** that cannot be wiped and lives until the garbage collector reclaims it.
- **A paste arrives as the entire password in one `string`.** Pasting is supported anyway: refusing it would push people who use a password manager into typing their master password somewhere else, which is worse. keypaste does not clear your clipboard afterwards — that is not its to clear.
- Anything below the toolkit — the OS keyboard layer, the input method, a keylogger — is outside this boundary and always was.

### Values the desktop app shows, and values it copies

The app puts vault contents on screen. Which contents, and when, is a decision rather than an accident, and it is held by a test rather than by this paragraph.

- **An entry list shows titles and groups.** Not usernames, not URLs — that is what `keypaste ls` prints, and a list is read over shoulders and photographed for marketing.
- **Selecting an entry shows its username, URL and notes.** One entry, chosen by you.
- **A password is never shown, anywhere, in any state.** The Copy button reads it out of the open vault at the moment you press it and hands it to the clipboard; it does not become anything the window can draw. To read one, use `keypaste get --show`.
- **An environment value can be revealed by holding it**, one at a time, for as long as you hold it. Comparing a stored value against a `.env` file is a real task, and a product that could only copy would push people into pasting secrets into a text editor to read them. The control that draws it is not a `TextBlock` — a `TextBlock` publishes its text to the accessibility layer as the element's name, which is the same read path as a `TextBox`'s value with a different label on it. What it cannot defend against is your screen: a screenshot, a recording, a shared call, a remote-desktop session, or somebody behind you.

**Copying, and clearing.** A copied secret is taken back off the clipboard after twenty seconds, and only if the clipboard still holds it — something you copied since is left alone. It is also cleared the moment the vault locks, and before the app exits when you quit it. None of that survives the app being killed: `kill -9`, End Task, an out-of-memory kill, a power cut and a logout all leave the value where it is, because nothing is left running to take it away.

**On Windows, the app does something `keypaste get` cannot.** Clipboard History (Win+V) and Cloud Clipboard keep a copy that clearing does not remove. A program that owns a window can mark an item to be skipped by both, and the app does; `clip.exe`, which the CLI uses, has no way to say it. So a password copied from the app stays out of Windows' own clipboard history and one copied by `keypaste get` does not. Neither can do anything about a third-party clipboard manager, which decides for itself, or about RDP and Citrix redirection, which hand the value to another machine.

### Editing your vault from the app

A vault the app writes is a vault the CLI wrote: the same code, in the same library, producing the same format. The KeePassXC compatibility gate that covers one covers the other, and a test asserts the app has no other route to a vault file so that stays true.

**If something else changes the file while the app has it open, the app refuses to save.** It holds your vault in memory for as long as it is unlocked, and writing it back would revert anything a terminal or KeePassXC wrote in the meantime — with no history entry, because the change was never in the app's copy. You are told, and nothing is written. Lock and unlock to pick up the other change, then make yours again.

## What keypaste does NOT protect against

Stated plainly, because a security tool that overclaims is worse than one that is modest.

**In-memory secrecy is not claimed.** keypaste keeps master passwords in a clearable `char[]` rather than a `string`, and zeroes the derived bytes after use. That narrows the window and reduces the number of copies; it is not a boundary. The garbage collector may relocate a buffer and leave an unreachable copy behind, values can reach swap, hibernation files or a core dump, a debugger or any process running as the same user can read them, and some values necessarily become immutable strings anyway. `SecureString` is deliberately not used: it does not encrypt on Linux or macOS, so it would read as a guarantee it cannot provide.

**This applies to approved credentials too, and for longer.** When you approve an agent's request, `keypaste agent` holds that field's value in a clearable buffer until the grant expires — up to `--max-ttl`, five minutes by default — so a repeat request does not have to ask you again. It is zeroed the moment the grant expires rather than at the next time something looks, and when the agent stops, every live grant goes with it. But the value reached that buffer as an ordinary immutable string out of the vault, and that copy cannot be cleared. It also crosses a local pipe in plaintext and arrives in the MCP client's process, where keypaste has no say in what happens to it at all. A shorter `--max-ttl` is the control keypaste actually offers.

**And `keypaste agent` keeps the vault unlocked for as long as it runs.** There is no idle auto-lock in this version; stopping it is the lock. That is stated here rather than left to be discovered.

**With a policy rule in force, no human sees the request at all.** A rule you wrote in `~/.keypaste/policy.toml` releases the credential it covers without a prompt. The agent's stated reason is recorded and read by nobody, so none of the display protections that exist for the approval prompt apply — there is no display. What exists instead is one line on the approver's terminal per release, one line in the audit log naming which rule did it, and whatever limits you put in the rule. If you want a human in the loop, do not write the rule.

**A policy rule is a standing grant over a part of your vault as it is now, not as it was when you wrote it.** Whoever can write into that part chooses what the rule covers: a synced vault, a colleague on a shared file, a `.env` you imported from somewhere else. Moving an entry into a group a rule names is enough. `keypaste policy ls` shows what each rule *means*; it cannot yet show what each rule currently *covers*.

**A policy rule names a client label any process on your machine could claim.** `--client-label` is chosen by whoever spawns `keypaste-mcp`, not by whoever connects to it. That stops the *agent* choosing which rules apply to it, and it does not stop another local program starting a bridge with the same argv. Client-scoped policy narrows convenience, not authority — and under the previous version that program would still have needed you to press `y`.

**The policy file is authorization, not configuration — keep it out of synced folders.** `~/.keypaste` is deliberately not beside your vault; pointing `KEYPASTE_HOME` at Dropbox or iCloud means another machine writes this machine's grants. On Linux and macOS keypaste **ignores** a policy file writable by anyone but you, and says so rather than repairing it; **on Windows there is no equivalent** and it says that instead. The file is read once, at startup, so editing it while the agent runs changes nothing until you restart it.

**The clipboard is not fully recoverable.** `keypaste get` clears the clipboard after twenty seconds, and only if it still holds what keypaste put there. But no clearing survives `kill -9`, a crash, or a power cut; on X11 and Wayland the clipboard is owner-served, so the secret also lives in the `wl-copy`/`xclip` process that keeps serving it after keypaste exits; and on Windows, clipboard history (Win+V) and cloud clipboard sync retain a copy that clearing does not remove. On Windows, prefer `keypaste get --show` piped where you need it, or disable clipboard history. This is tracked as an open decision (O-0008 in `DECISIONS.md`).

**A value passed on the command line is not private.** `keypaste env set project KEY=value` takes the value from the arguments, where it is readable by any process on the machine — through `/proc/<pid>/cmdline` on Linux, through WMI or Sysmon on Windows — for as long as the command runs, and where your shell will also write it to its history file. This form exists because scripts need it, and keypaste prints a one-line warning to stderr when you use it. When it matters, use `keypaste env set project KEY` instead and let keypaste read the value from a prompt or a pipe, which is how every other secret enters the vault. Whether that warning should be silenceable is tracked as an open decision (O-0009 in `DECISIONS.md`).

**Overwriting a value does not erase the old one.** `keypaste env set` on a variable that already exists keeps the previous value as a KDBX history item, which is what KeePassXC's own editor does and where KeePassXC will show it. It stays in the file, encrypted, until KeePass's ten-item history limit evicts it. If you are rotating a credential *because it leaked*, that is probably not what you want: `keypaste env rm` removes the entry and its history together, and re-adding it afterwards starts clean. keypaste itself has no command that reads history, so it is visible in KeePassXC and nowhere in keypaste (D-0014).

**An injected variable is visible to anything that can read the child.** `keypaste run` puts your values in the child process's environment, which is the only place a program can read them from — and which is readable through `/proc/<pid>/environ` on Linux, through `ps eww` and the debugging APIs on macOS, and through process inspection tools on Windows. Every grandchild inherits them, and a crash reporter or a framework that dumps its environment on error will print them. This is the cost of the feature, not a defect in it: it is strictly better than a `.env` file, which has all of the same exposure *plus* a copy on disk, in your editor's swap file, and in your backups — but it is not a boundary. keypaste's promise here is narrower and testable: **nothing is written to a file at any point**, which `scripts/verify-run-injection.sh` proves on every push by running with every temporary directory redirected at an empty folder and asserting it stays empty.

**On Windows, closing the console window can orphan the child.** `keypaste run` suppresses its own termination on Ctrl+C, Ctrl+Break and SIGTERM so that it stays alive to pass the signal on and report the child's exit status. Closing the console window is different: Windows raises `CTRL_CLOSE_EVENT` and then terminates the process a few seconds later whether or not it was handled, which can leave the child running with no parent. keypaste also never escalates to a hard kill — a child that ignores SIGTERM will make keypaste wait, which is deliberate: keypaste does not get to decide when your database is allowed to die.

**Deleting a `.env` does not destroy it.** `keypaste env pull` offers to delete the file it just imported. Deleting removes the directory entry; it does not overwrite the blocks the file used, and keypaste does not try to. On an SSD the flash translation layer has already remapped them, on a copy-on-write filesystem (APFS, btrfs, ZFS, ReFS) an overwrite would land elsewhere anyway, and snapshots, Time Machine, VSS shadow copies and any backup tool keep their own copy. GNU `shred`'s own manual says the same about the filesystems it runs on, which is why keypaste does not offer a "shred" and does not use the word. Nor does deletion touch your editor's `.env~` or swap file, your backups, your CI logs, or **git history — usually the largest exposure of the three**: if the file was ever committed, the values are in the repository and in every clone, and `keypaste env pull` says so when it finds a `.git` ancestor. Treat a secret that was committed or shared as leaked and rotate it. Deletion is tidying, not erasure.

**Exporting puts your secrets back on disk, and that is the whole point of it.** `keypaste env export --dotenv` is the one command that writes plaintext. docs/PRODUCT.md §3.4 forbids a secret touching disk unencrypted *by keypaste's doing*; here you name the format, name the destination, and answer a confirmation, which is the same line `keypaste get --show` sits on. keypaste narrows what it can: the file is created only if nothing is already there (`--force` to replace), on Linux and macOS it is created readable only by its owner, a `.git` ancestor is pointed out, and the warning is printed in red before the question rather than after the fact. **On Windows there is no equivalent** — the file inherits its directory's permissions and keypaste says so instead of implying a restriction it did not apply. Everything under *"Deleting a `.env` does not destroy it"* above then applies to the file you just made, in advance: your editor's swap file, your backups, snapshots, and git. `keypaste run` exists so that you rarely need this; when you do use it, delete the file when you are done and treat the values as having been exposed if it ever left the machine.

**The audit log is tamper-evident, not tamper-proof.** Every agent access is recorded locally, and keypaste opens that file only in append mode: no code path in it seeks, truncates, rewrites or deletes. Each record also carries the hash of the record before it, so `keypaste log verify` can tell you whether the file is the file keypaste wrote. That catches careless tampering — a line edited, removed, inserted, or written by something else. It does not catch two things, and the command says so every time it passes rather than only when it fails: **the chain holds no secret**, so anyone who can write the file can recompute it, and **records deleted from the end leave no trace**, because nothing follows them. For the second there is `keypaste log verify --expect <hash>`, which checks that a hash you wrote down earlier is still in the file; keypaste keeps no copy of it, because an anchor stored beside the thing it anchors is worth nothing. On Linux and macOS the log is created readable only by its owner; **on Windows there is no equivalent** and it inherits its directory's permissions, the same gap `env export` has. keypaste never rotates or trims the log — deleting lines is the opposite of what it is for — so it grows without bound. See [THREATS.md](THREATS.md) T-5.

Since 2.3 this matters more than it did, not less: a credential released by a policy rule has no human witness, so the log is not a second record that it happened — it is the only one. That is why a release which cannot be written down does not happen at all.

**Local attackers are out of scope.** Anything running as your user can read your memory, watch your keystrokes, and read your clipboard. keypaste protects the vault file at rest and limits what an AI agent can reach; it cannot defend a compromised account against itself.

## Maintainer note

`security@keypaste.com` is the primary reporting channel, and for now the only one that works for an outside reporter: the repository is private (DECISIONS.md O-0014), so GitHub's private vulnerability reporting is not reachable by anyone who cannot see the repository. Enable it as a second channel when that changes.

keypaste vendors KeePassLib for its KDBX4 implementation (`third_party/KeePassLib/UPSTREAM.md`). Vulnerabilities in that code are in scope here, and are also worth reporting upstream to KeePass — we hand-merge upstream patches rather than receiving them through a package manager, so a report to us does not reach Dominik Reichl automatically, or the reverse.
