# Replace your `.env` in 5 minutes

You have a `.env` file. By the end of this page it is in an encrypted vault you can open in
KeePassXC, your app still boots, and there is no plaintext copy on disk.

Nothing here is keypaste-specific magic: the vault is an ordinary KDBX4 file, and every variable is
an ordinary entry. If you stop using keypaste tomorrow, everything is still there and still
readable by other tools.

---

## Before you start

```sh
keypaste init ~/keypaste.kdbx        # asks for a master password, twice
export KEYPASTE_VAULT=~/keypaste.kdbx
```

Put that `export` in your shell profile. Otherwise every command needs `--vault ~/keypaste.kdbx`.

**Back the `.kdbx` file up now, before you put anything in it.** It is encrypted, so a backup is
useless to anyone who steals it and is the only thing standing between you and the FAQ at the
bottom of this page.

## Minute 1 — import what you already have

From the directory holding your `.env`:

```sh
keypaste env pull dev
```

`dev` is the project name — anything you like, one per app or per environment. It reads `./.env` by
default; pass a path for anything else (`keypaste env pull prod config/.env.production`).

```
note: a trailing ' #' comment was removed from: PORT. Quote the value if the '#' was part of it.
env/dev: 4 new, 0 updated, 0 unchanged
  new       DATABASE_URL, MOTD, PORT, STRIPE_KEY
Import 4 variables into env/dev? [y/N]
```

You get the plan before anything is written, by name — values are never printed. Two things worth
knowing:

- **If any line is malformed, nothing is imported.** You get every problem at once and an unchanged
  vault. There is no half-import.
- **`${VAR}` and `$VAR` are stored exactly as written, never expanded.** Expanding them would bake
  this machine's environment into a vault you may sync to another one.

## Minute 2 — check it, then delete the file

```sh
keypaste env ls dev                        # names only
keypaste get env/dev/DATABASE_URL --show   # one value, when you want to see it
```

Once you are satisfied, say yes to the deletion prompt (or pass `--delete-source` up front).
keypaste tells you what deleting does and does not do:

```
Deleting removes the file from the directory. It does not overwrite the blocks it
used: on an SSD, on a copy-on-write filesystem, or on any volume with snapshots or
backups, the old contents can outlive the file. If these values were exposed, rotate them.
Delete '/repo/app/.env'? [y/N]
```

If the file is inside a git repository, keypaste says so too. **Git history is usually the larger
exposure.** Deleting the file does nothing about it:

```sh
git log --oneline -- .env     # if this prints anything, treat those values as leaked
```

Full detail in [`../SECURITY.md`](../SECURITY.md).

## Minute 3 — run your app

```sh
keypaste run dev -- npm start
```

That is the whole point. The variables go straight into the child process's environment; no file is
written at any stage, and CI proves it on every push by running with every temporary directory
pointed at an empty folder and checking it stays empty.

- **The `--` is required.** Without it, `keypaste run dev npm start` cannot be told apart from a
  project called `npm`. Everything after `--` belongs to your command, including flags keypaste
  also understands.
- Your command gets keypaste's real stdin, stdout and stderr, so colours, prompts and progress bars
  behave exactly as if keypaste were not there.
- **The vault is closed before your command starts.** A dev server you leave running for a week is
  not holding a decrypted database open.
- Ctrl+C reaches your command, and keypaste waits for it rather than dying first. `docker stop` and
  `timeout` behave the same way.
- Once your command starts, its exit code is keypaste's. A command that does not exist reports 127
  and one that is not executable reports 126, as in a shell. keypaste's own failures always print a
  line starting `keypaste run:` first.

## Minute 4 — the rest of the repo

Keep `.env` in `.gitignore` — it will be gone from your working copy, but a teammate who has not
switched still needs the rule, and `keypaste env export` can put one back.

Keep `.env.example` exactly as it is. It documents *which* variables exist, which is a different job
from holding their values, and it is the only thing new contributors have to read.

Add the real thing to your README:

```sh
keypaste env pull dev        # once
keypaste run dev -- npm run dev
```

One vault or several is up to you. A single `keypaste.kdbx` with `env/app-dev`, `env/app-prod` and
`env/other-app` is simplest. Split when the blast radius differs — production credentials in their
own file with their own master password is a reasonable line to draw.

## CI

**Use a vault built for CI, never your personal one.** It is a different blast radius, a different
rotation schedule, and a different set of people who can read the logs. A CI vault holds only what
that pipeline needs.

```sh
keypaste init ci.kdbx
KEYPASTE_VAULT=ci.kdbx keypaste env set ci DATABASE_URL   # prompts, hidden
```

Then get the file to the runner. It is encrypted and its master password is not in it, so it can
live in the repository or in artifact storage — but **anything you commit to a public repository is
an offline cracking target forever**, so if the repo is public, use a long random master password
generated by a password manager, or keep the vault out of the repo entirely. KDBX4's Argon2 makes
guessing expensive, not impossible, and you cannot un-publish a commit.

Then, in the job:

```yaml
env:
  KEYPASTE_VAULT: ci.kdbx
steps:
  - run: printf '%s\n' "$KEYPASTE_MASTER" | keypaste run ci -- npm test
    env:
      KEYPASTE_MASTER: ${{ secrets.KEYPASTE_MASTER }}
```

That works because keypaste reads **exactly one line** of stdin for the master password and hands
the rest to your command untouched — so a program that reads stdin still gets its input.

Two more rules for non-interactive runs:

- Every confirming verb (`rm`, `env rm`, `env pull`, `env export`) refuses to guess when stdin is
  not a terminal. Pass `--yes` when you mean it.
- **Prefer `keypaste run` to `keypaste env export` in CI.** A runner that writes a `.env` has
  written plaintext to a disk you do not control, into a workspace something else may archive.

## The escape hatch

Sometimes you need a real file: a tool that only reads `.env`, a container build, or you are moving
off keypaste.

```sh
keypaste env export dev --dotenv --stdout    # to a pipe
keypaste env export dev .env --dotenv        # to a file, after confirming
```

```
! plaintext secrets are about to be written to disk
  /repo/app/.env will hold 4 values from env/dev in the clear. Anything
  that can read the file can read them, including your editor's swap file and
  your backups. `keypaste run` injects these without a file; this is the way out.
Write 4 values to '/repo/app/.env'? [y/N]
```

It refuses to overwrite an existing file unless you pass `--force`, and on Linux and macOS the file
is created readable only by you. Windows has no equivalent and keypaste says so rather than
implying a permission it did not set.

**What reads the file it writes.** keypaste quotes with single quotes wherever it can, because that
form is literal in every reader: `motdotla/dotenv`, `python-dotenv`, `joho/godotenv`, Docker Compose
v2, and `sh`. A value containing an apostrophe or a carriage return cannot be written that way, so
it is double-quoted and escaped — and keypaste names those keys on stderr, because not every reader
processes those escapes the way keypaste does. `docker run --env-file` is the one to avoid outright:
it does no quote or escape processing at all, so quoted values arrive with their quotes attached.

---

## FAQ

### What if I lose my master password?

**It is gone. Everything in that vault is gone.** There is no recovery, no reset, no support address
that can help, and no backdoor — because keypaste never had a copy to lose. The master key never
leaves your machine; that is the first line of the project's constitution, and it is the property
you are choosing when you use this instead of a hosted vault.

That is not a reason to be casual about it:

- **Write the master password down and keep the paper somewhere physical.** A safe, a wallet, a
  sealed envelope with someone you trust. The threat model here is a remote attacker, not your desk
  drawer.
- **Back up the `.kdbx`.** It is one encrypted file. Copy it anywhere. A backup is useless to a
  thief and everything to you.
- **Consider a key file.** KDBX supports a key file alongside the password; KeePassXC can add one.
  Then losing the password *and* the key file is what it takes, and you can store them apart.

Rotating what was in a vault you can no longer open means rotating every credential at its source.
That is a bad afternoon, and it is the only exit.

### How do I sync it between machines?

**Your file, your sync tool.** Syncthing, Dropbox, iCloud Drive, OneDrive, a private git repo, a USB
stick. The `.kdbx` is a single encrypted blob and none of those services can read it, which is
exactly why keypaste does not offer hosted sync — a service that holds your secrets is the thing
this project exists not to be.

One caveat: **KDBX has no merge.** If two machines edit the vault while offline, your sync tool will
produce a conflicted copy, and the loser's changes are in that copy rather than merged in. Edit in
one place at a time, and let the sync settle before you switch machines. For a team, treat the vault
as a document rather than a database.

### Can my teammate use the same vault?

Yes — share the file and the master password out of band, and everyone gets the same variables. It
works, and it is how small teams start. Its limits are real: one shared password, no per-person
revocation, and the merge caveat above. Anything more than that wants a vault per person with
overlapping content, or a tool built for teams.

### Does it work offline?

Entirely. There is no network code on the vault path at all.

### What does KeePassXC see?

Ordinary entries. `env/dev` is a group, each variable is an entry with the name as its title and the
value as its password. You can read, edit, add and delete them in KeePassXC with no knowledge of
keypaste, and CI verifies that in both directions on Linux, macOS and Windows on every push.

### Can I keep using `direnv`?

Yes. Put `keypaste run` inside whatever `direnv` starts, or have your `.envrc` shell out to
`keypaste env export dev --dotenv --stdout` if you truly need the values in your interactive shell —
with the understanding that they are then in your shell's environment and in anything it launches,
which is the exposure `keypaste run` exists to keep scoped to one command.

### Why did my value change when I imported it?

It almost certainly did not — but two rules differ from `motdotla/dotenv` on purpose, and both are
cases where dotenv silently loses data:

- A `#` starts a comment only when a space precedes it, so `PASSWORD=hunter2#42` keeps its `#`.
  dotenv truncates it to `hunter2`.
- A key set twice in one file is an error. dotenv keeps the first, godotenv keeps the last; since
  they disagree, keypaste refuses to pick.

Inside double quotes, `\n`, `\r`, `\t`, `\\` and `\"` expand, as they do in C or Python. If you mean
a literal Windows path, write `'C:\temp'` in single quotes.

---

Reference for every command and exit code: [`../README.md`](../README.md). What keypaste does and
does not protect against: [`../SECURITY.md`](../SECURITY.md).
