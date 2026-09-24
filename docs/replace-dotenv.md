# Replace your `.env` in 5 minutes

Import a `.env` file into an encrypted KeePassXC-compatible vault, then start your app with variables injected into its environment. Deleting the original file does not erase backups, snapshots or git history.

The vault uses KDBX4 and stores each variable as an ordinary entry, so other compatible tools can continue reading it.

## Before you start

```sh
keypaste init ~/keypaste.kdbx
export KEYPASTE_VAULT=~/keypaste.kdbx
```

Put that `export` in your shell profile. Otherwise every command needs `--vault ~/keypaste.kdbx`.

Set up backups for the `.kdbx` now, somewhere other than the machine it lives on. Keypaste keeps its own copies of the file beside it, in `<vault>.backups`, which covers a bad save and not a lost disk. A backup protects against losing the file; it does not recover a forgotten master password, and a copy taken before you changed that password still opens with the old one. Anyone who obtains any of these encrypted files can attempt offline password guessing, so use a strong master password and protect the copies as carefully as the vault.

<a id="minute-1--import-what-you-already-have"></a>

## Import

From the directory holding your `.env`:

```sh
keypaste env pull dev --keep
```

`dev` names the project or environment. Import reads `./.env` by default; supply another path when needed, as in `keypaste env pull prod config/.env.production`.

```
note: a trailing ' #' comment was removed from: PORT. Quote the value if the '#' was part of it.
env/dev: 4 new, 0 updated, 0 unchanged
  new       DATABASE_URL, MOTD, PORT, STRIPE_KEY
Import 4 variables into env/dev? [y/N]
```

The import plan lists names without values before writing anything.

If any line is malformed, the vault remains unchanged. The error report lists up to ten problems and counts the rest. `${VAR}` and `$VAR` are stored literally so importing does not bind the vault to one machine's environment.

<a id="minute-2--check-it-then-delete-the-file"></a>

## Verify and delete

```sh
keypaste env ls dev
keypaste get env/dev/DATABASE_URL --show
```

The import above used `--keep` so you can inspect the result before deletion. Once satisfied, run `keypaste env pull dev` again; unchanged values are left alone, and you can answer its deletion prompt. keypaste tells you what deleting does and does not do:

```
Deleting removes the file from the directory. It does not overwrite the blocks it
used: on an SSD, on a copy-on-write filesystem, or on any volume with snapshots or
backups, the old contents can outlive the file. If these values were exposed, rotate them.
Delete '/repo/app/.env'? [y/N]
```

keypaste deletes only the file it read. If the file changes during any of its three questions, it keeps the file, reports that the edit was not imported and exits nonzero when deletion was requested. Run `keypaste env pull dev` again to import the edit.

keypaste also reports if the file is inside a git repository. Deletion does not remove git history:

```sh
git log --oneline -- .env     # if this prints anything, treat those values as leaked
```

See [SECURITY.md](../SECURITY.md) for the deletion and exposure limits.

<a id="minute-3--run-your-app"></a>

## Run your app

```sh
keypaste run dev -- npm start
```

This command currently asks for the vault password on each invocation; an unlocked desktop does not satisfy it. In source, the desktop's Env Sets screen can instead import the `.env` and, once you save the project's directory and command, open a terminal running it with the set from the unlocked app after you confirm it ([desktop](desktop.md#what-the-screens-show)). Using the desktop's unlock from `keypaste run` is planned in [STEPS](STEPS.md) as E.1c.

keypaste places variables in the child environment without writing a plaintext env file. CI points temporary directories at an empty folder and checks that the injection fixture leaves it empty. The child can still write or forward values; review its logging and behavior.

Once delivered, environment values are copies held by the child and possibly its descendants. Locking a vault cannot remove those copies. The focused product will require an unlocked session for new launches and stop new releases when locked; it will not promise to revoke values already supplied.

The `--` separates the project from its command. Without it, `keypaste run dev npm start` could name `npm` as the project. All arguments after `--` belong to the command. The child receives keypaste's stdin, stdout and stderr, preserving terminal prompts and output. keypaste closes the vault before starting the child, so a long-running server does not keep the vault unlocked. Ctrl+C, `docker stop` and `timeout` reach the child; keypaste waits for it to exit. After startup, keypaste returns the child's exit code. Missing commands return 127 and non-executable commands return 126. keypaste's own failures print a line beginning `keypaste run:`.

<a id="minute-4--the-rest-of-the-repo"></a>

## Repository changes

Keep `.env` in `.gitignore` for teammates and future exports. Keep `.env.example` to document required variable names for contributors.

Document the workflow in your README:

```sh
keypaste env pull dev
keypaste run dev -- npm run dev
```

One vault can hold `env/app-dev`, `env/app-prod` and `env/other-app`. Use separate vaults and master passwords when environments need different access boundaries.

## CI

Use a dedicated CI vault containing only the credentials the pipeline needs.

```sh
keypaste init ci.kdbx
KEYPASTE_VAULT=ci.kdbx keypaste env set ci DATABASE_URL
```

Store the encrypted file in the repository or artifact storage, with its master password separate. A public repository exposes the vault to offline guessing indefinitely. Use a long random master password or keep the vault outside the repository; Argon2 raises guessing cost but cannot prevent it.

Then, in the job:

```yaml
env:
  KEYPASTE_VAULT: ci.kdbx
steps:
  - run: printf '%s\n' "$KEYPASTE_MASTER" | keypaste run ci -- npm test
    env:
      KEYPASTE_MASTER: ${{ secrets.KEYPASTE_MASTER }}
```

keypaste consumes exactly one line of stdin for the master password and leaves the rest for the child command.

With non-terminal stdin, confirming verbs (`rm`, `env rm`, `env pull`, `env export`) require `--yes`. Prefer `keypaste run` in CI to avoid writing plaintext into a runner workspace that may be archived.

## The escape hatch

Export when a tool requires a `.env` file or when moving credentials to another system:

```sh
keypaste env export dev --dotenv --stdout
keypaste env export dev .env --dotenv
```

```
! plaintext secrets are about to be written to disk
  /repo/app/.env will hold 4 values from env/dev in the clear. Anything
  that can read the file can read them, including your editor's swap file and
  your backups. `keypaste run` injects these without a file; this is the way out.
Write 4 values to '/repo/app/.env'? [y/N]
```

Export requires `--force` to overwrite an existing file. Linux and macOS exports are owner-readable; Windows has no equivalent permission control and reports that limit.

Export uses single quotes where possible, which are literal in `motdotla/dotenv`, `python-dotenv`, `joho/godotenv`, Docker Compose v2 and `sh`. Apostrophes and carriage returns require double quotes and escapes; keypaste names those keys on stderr because readers differ in escape handling. Avoid `docker run --env-file`, which preserves quotes and escapes literally.

## FAQ

### What if I lose my master password?

Without the master password or another usable copy of the credentials, keypaste cannot recover the vault's contents. There is no master-password reset or support backdoor. Entry history can restore an earlier value in an accessible vault; it cannot recover the master password, an entry that was permanently deleted or a lost vault file.

Keep a written copy of the master password in a secure physical location. Keep current `.kdbx` backups in protected locations. Their confidentiality depends on the master password's strength. The published `v0.3.0` cannot open vaults that require a key file; in Unreleased source keypaste opens them and `keypaste access` attaches an existing key file. Losing either required factor without a backup can make that vault inaccessible.

If you cannot recover access, rotate each credential at its provider.

### How do I sync it between machines?

keypaste has no built-in sync. You can copy or synchronize the encrypted `.kdbx` with an existing file-sync service or a USB drive; keep the master password separate. Hosted sync and relays are optional ideas in [BACKLOG](BACKLOG.md), with no committed delivery.

One caveat: keypaste does not merge concurrent vault edits today. If two machines edit offline, the sync tool may leave conflicting copies or overwrite one version. KDBX itself does not prohibit merging; the missing feature is in keypaste. Keep every conflicting copy, edit in one place at a time, and let synchronization finish before switching machines.

### Can my teammate use the same vault?

Someone with the file and master password can read everything in that vault. If you choose to share one, use a dedicated work vault and transfer its password through a separate trusted channel. This gives no per-person revocation or individual accountability and has the concurrent-edit limitation above. Organization management and offboarding are unimplemented options in [BACKLOG](BACKLOG.md). Removing file access cannot invalidate values someone already copied; that requires changing them at their providers.

### Does it work offline?

The vault path contains no network code and works offline.

### What does KeePassXC see?

Ordinary entries. `env/dev` is a group, each variable is an entry with the name as its title and the value as its password. You can read, edit, add and delete them in KeePassXC with no knowledge of keypaste, and the compatibility gate checks both directions on Linux, macOS and Windows for qualifying CI runs.

### Can I keep using `direnv`?

Use `keypaste run` within the command `direnv` starts. Explicitly exporting values into an interactive shell exposes them to every process that shell launches. Those values persist independently of the desktop's lock state; this is not automatic activation or revocation.

### Why did my value change when I imported it?

Check the import plan and parser rules. keypaste differs from `motdotla/dotenv` in comment and duplicate-key handling:

A `#` starts a comment only after a space, so `PASSWORD=hunter2#42` retains `#42`; dotenv truncates it to `hunter2`. Duplicate keys are rejected. The [upstream dotenv parser](https://github.com/motdotla/dotenv/blob/master/lib/main.js) keeps the later value; keypaste requires the duplicate to be resolved before importing.

Inside double quotes, `\n`, `\r`, `\t`, `\\` and `\"` expand, as they do in C or Python. If you mean a literal Windows path, write `'C:\temp'` in single quotes.

See [README.md](../README.md) for commands and exit codes, and [SECURITY.md](../SECURITY.md) for security limits.
