# Replace your `.env` in 5 minutes

You have a `.env` file. By the end of this page its variables are in an encrypted vault you can open in KeePassXC, and your app starts without requiring that plaintext file. Removing the original file does not erase copies in backups, snapshots or git history.

Nothing here is keypaste-specific magic: the vault is an ordinary KDBX4 file, and every variable is an ordinary entry. If you stop using keypaste tomorrow, everything is still there and still readable by other tools.

---

## Before you start

```sh
keypaste init ~/keypaste.kdbx        # asks for a master password, twice
export KEYPASTE_VAULT=~/keypaste.kdbx
```

Put that `export` in your shell profile. Otherwise every command needs `--vault ~/keypaste.kdbx`.

**Set up backups for the `.kdbx` now, and keep them current after changes.** A backup protects against losing the file; it does not recover a forgotten master password. Anyone who obtains the encrypted file can attempt offline password guessing, so use a strong master password and protect your backups.

## Minute 1 — import what you already have

From the directory holding your `.env`:

```sh
keypaste env pull dev --keep
```

`dev` is the project name — anything you like, one per app or per environment. It reads `./.env` by default; pass a path for anything else (`keypaste env pull prod config/.env.production`).

```
note: a trailing ' #' comment was removed from: PORT. Quote the value if the '#' was part of it.
env/dev: 4 new, 0 updated, 0 unchanged
  new       DATABASE_URL, MOTD, PORT, STRIPE_KEY
Import 4 variables into env/dev? [y/N]
```

You get the plan before anything is written, by name — values are never printed. Two things worth knowing:

- **If any line is malformed, nothing is imported.** The error report lists up to ten problems and counts any remaining ones; the vault is unchanged. There is no half-import.
- **`${VAR}` and `$VAR` are stored exactly as written, never expanded.** Expanding them would bake this machine's environment into a vault you may sync to another one.

## Minute 2 — check it, then delete the file

```sh
keypaste env ls dev                        # names only
keypaste get env/dev/DATABASE_URL --show   # one value, when you want to see it
```

The import above used `--keep` so you can inspect the result before deletion. Once satisfied, run `keypaste env pull dev` again; unchanged values are left alone, and you can answer its deletion prompt. keypaste tells you what deleting does and does not do:

```
Deleting removes the file from the directory. It does not overwrite the blocks it
used: on an SSD, on a copy-on-write filesystem, or on any volume with snapshots or
backups, the old contents can outlive the file. If these values were exposed, rotate them.
Delete '/repo/app/.env'? [y/N]
```

keypaste deletes the file it read and nothing else. If you edit the `.env` while it is asking — any
of its three questions — it keeps the file, says the new content was not imported, and exits
nonzero if you had asked for the deletion. Run `keypaste env pull dev` again and the edit goes in
like any other change.

If the file is inside a git repository, keypaste says so too. **Git history is usually the larger exposure.** Deleting the file does nothing about it:

```sh
git log --oneline -- .env     # if this prints anything, treat those values as leaked
```

Full detail in [`../SECURITY.md`](../SECURITY.md).

## Minute 3 — run your app

```sh
keypaste run dev -- npm start
```

That is the whole point. keypaste places the variables in the child process's environment without writing a plaintext env file. The CI injection check points temporary directories at an empty folder and checks that the fixture leaves it empty. Your app can still write or forward the values it receives; review its logging and behavior separately.

- **The `--` is required.** Without it, `keypaste run dev npm start` cannot be told apart from a project called `npm`. Everything after `--` belongs to your command, including flags keypaste also understands.
- Your command gets keypaste's real stdin, stdout and stderr, so colours, prompts and progress bars behave exactly as if keypaste were not there.
- **The vault is closed before your command starts.** A dev server you leave running for a week is not holding a decrypted database open.
- Ctrl+C reaches your command, and keypaste waits for it rather than dying first. `docker stop` and `timeout` behave the same way.
- Once your command starts, its exit code is keypaste's. A command that does not exist reports 127 and one that is not executable reports 126, as in a shell. keypaste's own failures always print a line starting `keypaste run:` first.

## Minute 4 — the rest of the repo

Keep `.env` in `.gitignore` — it will be gone from your working copy, but a teammate who has not switched still needs the rule, and `keypaste env export` can put one back.

Keep `.env.example` exactly as it is. It documents *which* variables exist, which is a different job from holding their values, and it is the only thing new contributors have to read.

Add the real thing to your README:

```sh
keypaste env pull dev        # once
keypaste run dev -- npm run dev
```

One vault or several is up to you. A single `keypaste.kdbx` with `env/app-dev`, `env/app-prod` and `env/other-app` is simplest. Split when the blast radius differs — production credentials in their own file with their own master password is a reasonable line to draw.

## CI

**Use a vault built for CI, never your personal one.** It is a different blast radius, a different rotation schedule, and a different set of people who can read the logs. A CI vault holds only what that pipeline needs.

```sh
keypaste init ci.kdbx
KEYPASTE_VAULT=ci.kdbx keypaste env set ci DATABASE_URL   # prompts, hidden
```

Then get the file to the runner. It is encrypted and its master password is not in it, so it can live in the repository or in artifact storage — but **anything you commit to a public repository is an offline cracking target forever**, so if the repo is public, use a long random master password generated by a password manager, or keep the vault out of the repo entirely. KDBX4's Argon2 makes guessing expensive, not impossible, and you cannot un-publish a commit.

Then, in the job:

```yaml
env:
  KEYPASTE_VAULT: ci.kdbx
steps:
  - run: printf '%s\n' "$KEYPASTE_MASTER" | keypaste run ci -- npm test
    env:
      KEYPASTE_MASTER: ${{ secrets.KEYPASTE_MASTER }}
```

That works because keypaste reads **exactly one line** of stdin for the master password and hands the rest to your command untouched — so a program that reads stdin still gets its input.

Two more rules for non-interactive runs:

- Every confirming verb (`rm`, `env rm`, `env pull`, `env export`) refuses to guess when stdin is not a terminal. Pass `--yes` when you mean it.
- **Prefer `keypaste run` to `keypaste env export` in CI.** A runner that writes a `.env` has written plaintext to a disk you do not control, into a workspace something else may archive.

## The escape hatch

Sometimes you need a real file: a tool that only reads `.env`, a container build, or you are moving off keypaste.

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

It refuses to overwrite an existing file unless you pass `--force`, and on Linux and macOS the file is created readable only by you. Windows has no equivalent and keypaste says so rather than implying a permission it did not set.

**What reads the file it writes.** keypaste quotes with single quotes wherever it can, because that form is literal in every reader: `motdotla/dotenv`, `python-dotenv`, `joho/godotenv`, Docker Compose v2, and `sh`. A value containing an apostrophe or a carriage return cannot be written that way, so it is double-quoted and escaped — and keypaste names those keys on stderr, because not every reader processes those escapes the way keypaste does. `docker run --env-file` is the one to avoid outright: it does no quote or escape processing at all, so quoted values arrive with their quotes attached.

---

## FAQ

### What if I lose my master password?

**Without the master password or another usable copy of the credentials, current keypaste cannot recover the vault's contents.** There is no master-password reset or support backdoor. The planned hosted service also stores encrypted data without the keys needed to decrypt it. Any future trusted-device or user-held recovery mechanism requires the reviewed design in [STEPS](STEPS.md); it is not available today. [PRODUCT](PRODUCT.md) §2 separates account recovery from vault recovery.

That is not a reason to be casual about it:

- **Write the master password down and keep the paper somewhere physical.** A safe, a wallet, a sealed envelope with someone you trust. The threat model here is a remote attacker, not your desk drawer.
- **Back up the `.kdbx`.** Keep current copies in protected locations. Encryption protects their contents subject to the strength of your master password; it does not make a stolen copy harmless.
- **Key files are not supported by current keypaste.** KeePassXC can require a key file alongside the master password, but keypaste cannot open that configuration today. When a vault requires both factors, losing either required factor without a backup can make it inaccessible; a key file is not a substitute for remembering the password.

Rotating what was in a vault you can no longer open means rotating every credential at its source. That is a bad afternoon, and it is the only exit.

### How do I sync it between machines?

**keypaste has no built-in sync today.** You can copy or synchronize the encrypted `.kdbx` with an existing file-sync service or a USB drive; keep the master password separate. [PRODUCT](PRODUCT.md) includes optional hosted encrypted sync and the same relay for self-hosting, with client-held keys. That service is planned, not available; [STEPS](STEPS.md) owns its delivery status.

One caveat: **keypaste does not merge concurrent vault edits today.** If two machines edit offline, the sync tool may leave conflicting copies or overwrite one version. KDBX itself does not prohibit merging; the missing feature is in keypaste. Keep every conflicting copy, edit in one place at a time, and let synchronization finish before switching machines.

### Can my teammate use the same vault?

Someone with the file and master password can read everything in that vault. If you choose to share one, use a dedicated work vault and transfer its password through a separate trusted channel. This gives no per-person revocation or individual accountability and has the concurrent-edit limitation above. Organization-owned credentials and offboarding are planned in [STEPS](STEPS.md), not implemented. Removing file access cannot invalidate values someone already copied; that requires changing them at their providers.

### Does it work offline?

Entirely. There is no network code on the vault path at all.

### What does KeePassXC see?

Ordinary entries. `env/dev` is a group, each variable is an entry with the name as its title and the value as its password. You can read, edit, add and delete them in KeePassXC with no knowledge of keypaste, and CI verifies that in both directions on Linux, macOS and Windows on every push.

### Can I keep using `direnv`?

Yes. Put `keypaste run` inside whatever `direnv` starts, or have your `.envrc` shell out to `keypaste env export dev --dotenv --stdout` if you truly need the values in your interactive shell — with the understanding that they are then in your shell's environment and in anything it launches, which is the exposure `keypaste run` exists to keep scoped to one command.

### Why did my value change when I imported it?

Check the import plan and the parser rules. Two deliberate differences from `motdotla/dotenv` are:

- A `#` starts a comment only when a space precedes it, so `PASSWORD=hunter2#42` keeps its `#`. dotenv truncates it to `hunter2`.
- A key set twice in one file is an error. `motdotla/dotenv`'s parser overwrites the earlier value with the later one; keypaste rejects duplicates so you must resolve the ambiguity before import. See the [upstream parser](https://github.com/motdotla/dotenv/blob/master/lib/main.js).

Inside double quotes, `\n`, `\r`, `\t`, `\\` and `\"` expand, as they do in C or Python. If you mean a literal Windows path, write `'C:\temp'` in single quotes.

---

Reference for every command and exit code: [`../README.md`](../README.md). What keypaste does and does not protect against: [`../SECURITY.md`](../SECURITY.md).
