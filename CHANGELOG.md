# Changelog

> docs/PRODUCT.md law 4.7: small releases, real changelogs, semantic versioning. Written for someone deciding whether to upgrade, not for someone reading commits. The release workflow refuses to publish a tag that has no section here.

Versions are the ones published at `https://dl.keypaste.com/v<version>/`. Every release carries a `SHA256SUMS` file and a per-asset `.sha256`, plus the corresponding source for that tag. The published CLI/MCP binaries are unsigned and un-notarized (O-0010); there is no public desktop release. The [release contract](docs/RELEASE.md) records the platform matrix and the requirements for an installed, publicly available release.

## Unreleased

**A save that had to wait its turn no longer throws away the save it was waiting for.** When
keypaste cannot write the vault immediately — a virus scanner has it open, or another keypaste
process is saving the same file — it waits a moment and tries again, for about two seconds. If what
it was waiting for was another program finishing its own save, keypaste would then write its own
copy over the top. Whatever that other save added was gone: not in the entry's history, not in
KeePassXC's History tab, not anywhere, because as far as this copy of the vault was concerned it had
never existed. keypaste now re-reads the file between attempts and refuses to write at all if
something else got there first — the same refusal you already get when a vault changed on disk
while you had it open. Nothing is written, and you are told to reload. This has been possible since
`0.1.0`; it needed two programs to save the same vault within about two seconds of each other,
which is exactly what a password manager and an agent sharing one vault do.

**On Windows, a save that collided with the vault's own name failed instantly instead of waiting.**
Windows can briefly refuse to let a file be renamed onto a name another program has reserved.
keypaste treated that as a permanent failure and gave up on the first try, reporting that the vault
could not be saved when trying again a moment later would have worked. It now waits, the same way it
already waited for a scanner.

**And it no longer leaves a `vault.kdbx.tmp` file next to your vault.** The failed save above left
one behind every time, and nothing ever cleaned it up. keypaste now removes it — and only it, and
only when it is certain no other program still has the file open.

## 0.2.0

**If you are running `v0.1.0`, upgrade.** Several of the repairs below are data loss, and none of them is in the `v0.1.0` archives: `env export` could delete the vault it was reading from and write plaintext over it, `env rm` and `env set` could act on a different entry than the one named, `get` could hand back the wrong entry's password, and `env pull` could delete an edit it never imported. `v0.1.0` stays where it is and stays broken — published versions are immutable here — so the fix is this version.

**Built from the commit `v0.2.0-rc.1` proved.** The candidate published unadvertised at its own URL first; its bytes were then fetched anonymously from the public origin and checked against a recorded hash for every asset, and every advertised target was installed on a clean machine and made to create a vault and inject a value into a child process. `0.2.0` is that same source.

**An agent that says hello and asks in the same breath is no longer told it never said hello.** An MCP client may send its handshake and its first request together without waiting in between, and several do. On macOS the request could overtake the handshake it followed, and keypaste answered it "called before the initialize handshake completed" — a refusal written for a client that had introduced itself to nobody, handed to one that had. keypaste now waits a moment for an identity already on its way. What it still will not do is answer for a client it cannot name: one that sent nothing waits out the same moment and is still refused.

**The download pages now say which machines the binaries are for.** Every advertised target
carries an OS floor — glibc 2.35 on Linux, macOS 13 or later, Windows 10 1809 or later — and, where
nothing has actually been run on that floor, says so in the same breath. Only the Linux x64 claim is
backed by a run: the release workflow starts that binary on a clean Debian 12 and requires Alpine to
refuse it. The macOS and Windows numbers are .NET 10's supported-OS floors rather than versions this
project has measured, and the pages say that rather than implying otherwise. Nothing about the
binaries changed; what changed is that a reader on an older machine can now find out before
downloading instead of after.

**A password you approved and never received is now said out loud, and it no longer costs you the
rest of your approvals.** Some entries carry a long note — a certificate, a private key somebody
pasted in, a page of instructions. If an agent asked for one of those and you said yes, keypaste
could not send the answer: the reply was bigger than one message. The connection dropped, everything
else you had approved on it was thrown away, the agent tried again and you were asked the same
question a second time — and after all that the log recorded it as though nobody had approved
anything and the agent had never reached a person at all. Now the answer arrives. It says the
release was approved and the value was too large to send, it says so in the log in those words, and
it tells the agent not to ask again because nothing about it will change. Nothing partial is ever
sent: keypaste will not hand over a shortened copy of a secret, because a shortened secret is a
different secret. Asking twice costs one question rather than two, the connection stays up, and the
next thing you approve on it works normally. If you need a value that large, copy it yourself — that
is the honest answer, and it is now the one you are given.

**A vault with a lot of entries in it can be listed now, and it costs you nothing to do it.** If an
agent asked for the names of your entries and there were too many of them to send in one go — around
a thousand ordinary ones, or far fewer if they are long or written in a script other than English —
keypaste could not send the answer at all. What happened instead was worse than an error message.
The connection between the agent and the vault was dropped; the agent tried once more and it was
dropped again; and any approval you had already given on that connection went with it, so the next
time the agent asked for a password you had just released, you were asked about it all over again.
What the agent was told, after all that, was that something had gone wrong. The reply is now sized
to what will actually fit. When some names are left out it says so before the list and again after
it, it does not pretend to be an inventory of your vault, and it tells the agent there is no way to
ask for the rest — so it stops looking, and asks you instead. Nothing else changes: the same part of
your vault is exposed, the names are still the only thing that comes back, and your approvals stay
where you left them.

**An agent can no longer stack up prompts for you to clear.** keypaste has always said it shows
one request at a time and refuses a second rather than queueing it, and the part of it that decides
did exactly that. The bridge in front of that part did not: it held the second request until you
had answered the first, and then sent it on — so the promise held for two agents at once and failed
for one agent asking twice, which is the case it was written for. Ten requests meant ten prompts,
one after another, each waiting for the last. They are refused where they arrive now. The agent is
told `BUSY` rather than `DENIED`, because you did not refuse anything and an agent that reads a
refusal learns the wrong thing about what you want; it is told the wait may be as long as a person
takes, so it waits instead of retrying in a loop; and it is not told which of its own calls is in
the way. Asking for the names of your entries goes down the same connection, so it is refused the
same way while you are deciding. Every refusal is still one line in the audit log.

**Changing your computer's clock is no longer a way to extend an agent's access.** When you
approve a request, keypaste remembers that answer for the lifetime it showed you, so the agent's
next request for the same thing does not ask you again. That lifetime was measured against your
computer's calendar clock — the one an NTP correction, a timezone tool or a person with the
settings open can move. Moving it back an hour made an approval that had already run out work
again, told the agent it had another hour on it, and left the password sitting in memory with
nothing left to clear it, because the timer that should have wiped it had already fired and found
the deadline "not yet". An approval is now measured against both your clock and one that cannot be
moved, and whichever says more time has passed wins — so a correction, and a laptop that slept
through the whole lifetime, both end it. A refusal is measured the same way in the opposite
direction: if you say no, moving the clock forward will not cut the pause short and let the same
question through.

**What your master password field tells the rest of your computer is now a test, not a
description.** Windows and Linux both run an accessibility service any program on the machine can
ask questions of, and an ordinary password box answers them with the password — the dots are
drawn, not enforced. keypaste has never used an ordinary password box, but nothing checked that,
and the security page said so. It is checked now: what leaves that field is the placeholder and a
row of dots, and two different passwords of the same length produce exactly the same answer.
Nothing about the field changed; what changed is that it can no longer quietly stop being true.
Two things the check turned up and did not fix: `Ctrl/Cmd+V` does nothing in that field, which
`SECURITY.md` used to imply it did, and the field has no name for a screen reader. Both are
written down.

**A password copied at the instant you lock no longer arrives after the lock.** Copying goes
through the windowing system, and that takes a moment. If the vault locked, or you quit, or you
pressed Clear now while it was still in progress, the copy finished afterwards: the password
landed on the clipboard *after* the vault had shut, and started its own twenty-second countdown
in a window that was no longer there. It is taken back now the moment the system hands it over,
quitting waits for that rather than exiting around it, and a `keypaste run` line caught the same
way is still left where it landed, because that was never a secret. One quieter case went with
it: when keypaste could not read the clipboard back after copying, the countdown ran and then
cleared nothing at all, leaving the password there indefinitely. It clears.

**The programs now say keypaste published them.** Open the properties of a downloaded
`keypaste.exe` and the company and copyright named a person — on a project whose whole public
identity is the project. The value came from a field nobody had set: `Company` was blank, the
build filled it in from `Authors`, and what shipped appeared in no file you could search for. It
is set on purpose now, and the copyright reads `Copyright (c) 2026 keypaste`. `KeePassLib.dll`,
the vendored KDBX library the app ships beside it, had a blank copyright where it should carry
Dominik Reichl's; it carries his now, which is what its licence asks for. The `v0.1.0` downloads
are unchanged and cannot be changed — this is what the next release will say.

**Minimizing the window can lock your vault, which is what the checkbox always claimed.** "Lock
when the window is minimized" has been in Settings since the entry screens shipped. It saved your
choice, showed it ticked on every later visit, and nothing anywhere read it: no part of the app
watched the window's state, so the one gesture you had chosen to mean "I am leaving" left the vault
open. It locks now, through the same lock `Ctrl/Cmd+L` and the idle timeout go through — the shell
leaves the window, a password you copied comes off the clipboard, and restoring shows the unlock
screen rather than the entries. Ticking the box takes effect on the next minimize rather than the
next launch, and unticking it stops at once. It is a minimize and only a minimize: another window
taking focus is not one, and on macOS hiding the app with Cmd+H is not one either. Observed on
Windows; macOS and Linux are still to be watched by hand.

**The app now launches with the settings you chose.** Pick a one-minute idle timeout, quit, and
the next launch held your vault open for five — Settings said "1 minute" while the session ran on
the default it had never been told to replace. The theme did the same: `theme = "dark"` was
written, and every launch painted itself in whatever the operating system said until you went back
to Settings and picked Dark again. The preferences are read once now, at startup, and the screen
shows the record that armed the session rather than a second read that only agreed by luck. The
palette is applied before the window is built, so there is no flash of the wrong one. A timeout you
typed into `app.toml` by hand that is not one of the six offered — `137`, say — is honoured and the
list names it, rather than being rounded off to a number the vault is not using; the file is not
rewritten. A file keypaste cannot read still costs a preference and never a lock, and is still left
exactly as it is.

**A path two entries answer to no longer hands out one of their secrets.** F.1a stopped
`keypaste env rm` deleting the wrong entry, and left the reading half alone. So `keypaste get
env/dev/nested/TOKEN` still served whichever of `nested/TOKEN` in `env/dev` and `TOKEN` in
`env/dev/nested` the file listed first — the wrong password, on your clipboard or your screen,
with nothing saying it had chosen. The desktop's Copy button did the same, and an inline edit
read the wrong entry and then wrote your change into it. All three now refuse and name the
collision, and the desktop pane addresses the entry you selected by its group and its title
rather than by the two joined. Removing is unchanged.

**`keypaste add` no longer refuses an entry that does not exist.** A title of `b/c` in group `a`
made the *path* `a/b/c` taken, and that was enough to reject a genuinely different `c` in `a/b`
with "already exists". It checks the identity now, so that entry can be created — and says
afterwards that the path names two things, because that is what you have just made. What it
still refuses is a third entry on a path that already names two.

**`env pull` deletes the file it imported, not whatever is at that path when it finishes.** Add a
variable to your `.env` while keypaste is asking for your master password — or while it is asking
whether to import, or whether to delete — and the old code deleted it anyway, having read the file
minutes earlier. The new variable was in neither the vault nor the directory, and nothing else on
your machine had a copy. keypaste now records what it read, by path and by content, and deletes only
that: a file that changed is kept, said so, and re-importable by running the command again. The
check happens before you are asked, so you are never offered the deletion of bytes keypaste did not
import, and again at the removal itself, where the file is moved out of the way and verified under a
name nothing else can reach — so an editor saving between your `y` and the delete cannot lose the
save. A source that something else removed no longer reports `Deleted`.

**`env export` will not write a `.env` over your vault.** `keypaste env export billing vault.kdbx
--dotenv --force --yes` deleted the vault and wrote one project's variables into its place, in
plaintext — every other entry, every other project and all of the entry history gone, with nothing
to recover from. Without `--force` it was worse than useless: the refusal it printed named `--force`
as the way past itself. The destination is now compared against the vault before anything is
written, through symbolic links and junctions, including one in a directory above the file, and
`--force` does not lift it. A destination that is *any* KeePass vault is refused as well — wider
than the defect, deliberately, because a hard link and a bind mount reach one file by a path no
check can resolve, and losing somebody else's vault costs the same.

**Deleting a variable can no longer take its neighbour with it.** A KeePassXC user can title an
entry `nested/TOKEN` and put it in `env/dev`, and that entry has the same path as an ordinary
`TOKEN` in `env/dev/nested` — `env/dev/nested/TOKEN`, for both. keypaste had two rules for taking
that path back apart and they disagreed, so `keypaste env rm dev nested/TOKEN` listed one entry,
deleted the other, and said `Removed`. An entry is now addressed by its group and its title, never
by the two joined, everywhere a vault is written. The same defect was in `env set`: a title of
`billing/TOKEN` sitting directly in `env` could receive a value meant for `env/billing/TOKEN`.

**A name that two entries answer to is refused rather than guessed at.** KDBX allows two entries
with one title in one group and KeePassXC will make them. Removing or updating either would be a
guess, so keypaste declines, names the collision and writes nothing; `keypaste rm` and `keypaste
env rm` exit nonzero and leave the file untouched, and the desktop says so on the screen you were
on. `keypaste ls` and `env ls` still show both, because a listing that hid one would be keypaste
and KeePassXC disagreeing about the same file. A removal that finds nothing to remove no longer
reports success — it exits 3 and does not rewrite the vault.

**The approval dialog stops mangling the agent's reason.** A reason naming an entry — `env/demo/STRIPE_KEY`, the ordinary thing for an agent to say — had its slashes replaced and was then labelled as having been scrubbed. Reasons now keep the path separator; markup, fences and pipes are still stripped, so a hostile reason is as inert as it ever was.

**The audit log names the entry, not the handle.** Agents are told to prefer an opaque handle, so most real requests logged as `k1_…` — unreadable, in the one record whose job is saying which entry was asked for. It now records the entry the approver resolved.

**`keypaste setup` wires your AI clients for you.** One command finds the clients installed on this
machine and points each at a vault: Claude Code and Codex are configured through their own
`mcp add` commands, and Cursor and Claude Desktop — which have none — get their block printed for
you to paste. `--dry-run` shows the exact commands and changes nothing; `--remove` takes keypaste
out again and leaves every other server alone. Running it twice is the same as running it once,
which matters because re-running is how you fix a vault that moved. This replaces the page of
by-hand instructions that stood between installing keypaste and using it.

**A hostile review of the whole tree before it goes public** (`docs/STEPS.md` 10.1). Three findings, each with a regression test watched failing before its fix.

Names out of a vault are now drawn through the sanitizer everywhere a person reads them, not only where a model does: `keypaste ls`, `keypaste env ls`, the `env pull` rejection message, and the desktop app's entry list, group tree, detail pane and env tables. A KDBX title cannot hold a control character, so what this closes is a name that *reads* as something it is not — a bidirectional override or an invisible code point. A listing says when what it drew is not what the vault holds. What addresses an entry, seeds an edit or reaches the clipboard is unchanged and still exact.

The approval prompt now says when the entry or the reason it shows was scrubbed. It stays silent for an ordinary name, so the dialog is unchanged for everyone whose entries are ordinary.

The agent bridge now records an access that ends in an exception. Previously only cancellation was caught, so an I/O or cryptographic failure out of the vault escaped before the audit line was written — nothing was released, but nothing was recorded either. Relatedly, a connection that fails to accept no longer ends the approver holding your unlocked vault.

## 0.2.0-rc.1

**A candidate, not a download**, and the same source as `0.2.0` above. It was published unadvertised at its own URL to run the release pipeline over the parts only a tag reaches — the desktop build's prerelease version, the matrices both workflows derive from the release definition, and the publish job. Nothing linked it, and its changes are the ones listed under `0.2.0`.

## 0.1.0

**The first release meant to be installed.** Everything below was already true of `0.1.0-rc.1`; what changed is that the pipeline has now been run end to end, an asset has been downloaded and checked by hand off the published origin, and the install commands on the README and on keypaste.com name this version. The rc exists in the open at its own URL and stays there; nothing links it.

**What keypaste does at 0.1.0.** A local KDBX4 vault you own, environment variables injected into a child process without touching disk, and an MCP bridge that lets an agent ask for exactly one field of one entry - answered by a person, for a lifetime shown before they answer, with every call appended to a hash-chained local log. No account, no server, no network.

**Known limits, stated rather than discovered.** The binaries are unsigned and un-notarized, so nothing cryptographically ties them to this project (O-0010); the checksum beside each asset proves the bytes arrived intact, not who made them. There is no released GUI - the desktop app builds from source and is not in this release - and approval is a terminal prompt. Linux needs glibc 2.35 or newer; Alpine and other musl distributions are not covered. Intel Macs and Windows on ARM build from source. `THREATS.md` T-21 is the full account of what you trust by downloading instead of building, and building from source remains strictly stronger.

## 0.1.0-rc.1

First tag, and the first time anything has been published. It exists to run the release pipeline end to end rather than to be installed; treat it as a dry run with real bytes.

**Native binaries for four platforms.** `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`, compiled with NativeAOT: one file each, no .NET runtime to install, about 10 MB per binary and a little under half the startup time of the framework-dependent build. `osx-x64` is not published (O-0013). Intel Macs build from source. Documentation correction, 2026-09-07: [GitHub provides Intel macOS runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners); their absence is not a current reason to exclude this target. It still requires its own build and verification in this project's release matrix.

**Every gate runs against the published binary, not a rebuild.** The release workflow deletes the ordinary build before testing, so the artifact that gets uploaded is the artifact that was proved: credential approval and refusal across two real processes, MCP over real pipes, the audit chain's tamper detection, environment injection, the demo transcripts, and - on all four platforms - a KDBX written by that exact binary opening in real KeePassXC.

**The Linux binaries need glibc 2.35 or newer** (Debian 12, Ubuntu 22.04 and later). The release workflow checks the x64 binary on a clean Debian 12 container; there is no equivalent arm64 container check. Alpine and other musl distributions are not covered.

Nothing about the vault format, the approval flow or the audit log changed in this release.
