# Replace your `.env` in 5 minutes

> This guide describes the source build headed for keypaste 1.0.0, which is not yet published. In the published `v0.3.0` each variable is its own item under `env/<project>`, `get` takes `--show` rather than `--field … --reveal`, and there are no environments (`-p`), `env diff`, `kp://` references, `keypaste use` or `--session` runs. [FEATURES](FEATURES.md) lists what each version has.

Import a `.env` file into an encrypted KeePassXC-compatible vault, then start your app with variables injected into its environment. Deleting the original file does not erase backups, snapshots or git history.

Each variable becomes a protected field of an item tagged into the project, in the KDBX 4 file, so KeePassXC and other KeePass apps can read and edit it.

## Before you start

```sh
keypaste init ~/keypaste.kdbx
export KEYPASTE_VAULT=~/keypaste.kdbx
```

Put that `export` in your shell profile, or run `keypaste use ~/keypaste.kdbx` once, which the app also does when it first opens a vault. Otherwise every command needs `--vault ~/keypaste.kdbx`.

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
dev/dev: 4 new, 0 updated, 0 unchanged
  new       DATABASE_URL, MOTD, PORT, STRIPE_KEY on env/dev/.env
env/dev/.env will be created and tagged into dev/dev.
Import 4 variables into dev/dev? [y/N]
```

The import plan lists names without values before writing anything.

If any line is malformed, the vault remains unchanged. The error report lists up to ten problems and counts the rest. `${VAR}` and `$VAR` are stored literally so importing does not bind the vault to one machine's environment.

<a id="minute-2--check-it-then-delete-the-file"></a>

## Verify and delete

```sh
keypaste env ls dev
keypaste get env/dev/.env --field DATABASE_URL --reveal
```

The import above used `--keep` so you can inspect the result before deletion. Once satisfied, run `keypaste env pull dev` again; unchanged values are left alone, and you can answer its deletion prompt. keypaste tells you what deleting does and does not do:

```
Deleting removes the file from the directory. It does not overwrite the blocks it
used: on an SSD, on a copy-on-write filesystem, or on any volume with snapshots or
backups, the old contents can outlive the file. If these values may have leaked, rotate them.
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

This command asks for the vault password on each invocation. `keypaste run --session dev -- npm start` uses the unlocked desktop app or `keypaste approve` instead: it asks for no password, either one shows the project, the variable names, the command and this directory, and the command starts only when you allow it there ([approvals](approvals.md#runs-that-ask-for-a-projects-variables)). A refusal, a lock or nothing holding the vault ends the run with a reason and starts nothing; it never falls back to asking for the password. The desktop's Projects screen can also import the `.env` and, once you save the project's directory and command, open a terminal running it with the set after you confirm it ([desktop](desktop.md#projects)).

keypaste places variables in the child's environment without writing a plaintext env file. The child can still write or forward values, so review its logging and behavior. Once delivered, the values are copies held by the child and its descendants: a `--session` run needs the vault unlocked to start, but locking the vault afterwards cannot take those copies back ([SECURITY](../SECURITY.md)).

The `--` separates the project from its command. Without it, `keypaste run dev npm start` could name `npm` as the project. All arguments after `--` belong to the command. The child receives keypaste's stdin, stdout and stderr, preserving terminal prompts and output. keypaste closes the vault before starting the child, so a long-running server does not keep the vault unlocked. Ctrl+C, `docker stop` and `timeout` reach the child; keypaste waits for it to exit. After startup, keypaste returns the child's exit code. Missing commands return 127 and non-executable commands return 126. keypaste's own failures print a line beginning `keypaste run:`.

<a id="profiles-and-references"></a>

## Environments and references

A project holds one set per environment, named with lowercase letters, digits and `-`; everything above is the `dev` environment. An environment's keys are fields of the items tagged into it: `env set` and `env pull` add a new key to its item `env/<project>/.env`, or `.env.<profile>` for another environment, which they create tagged `env:<project>` or `env:<project>:<profile>`, unless `--entry` names another item tagged into it. An item kept the `v0.3.0` way, one per variable under `env/<project>`, is an ordinary item and no longer a variable. Every env verb and `run` take `-p <profile>`:

```sh
keypaste env set acme-api DATABASE_URL -p staging
keypaste env ls acme-api --profiles
keypaste env diff acme-api dev staging
keypaste run -p staging acme-api -- npm start
```

`env diff` names a key one environment lacks, a key an environment holds but cannot release (an expired item, a name that cannot be exported) and a value two environments share; it never prints a value. Environments named `prod`, `production`, `prod-*` or `production-*`, in any case, are protected: a `--session` run of one is asked about live every time, allow once only.

`keypaste env export acme-api -p staging > .env.keypaste` writes one reference per variable, such as `DATABASE_URL=kp://acme-api/staging/DATABASE_URL`, and no value, so the file can be committed. `kp:///<group>/<title>#username` names a field of any vault item. `keypaste run --env-file .env.keypaste -- npm start` resolves every reference or starts nothing, and `-p` moves each `kp://<project>/…` reference to that environment.

Once the desktop app has saved a directory for the project, `keypaste run -- npm start` there uses the project `projects.json` maps it to, and uses `./.env.keypaste` only when that file names nothing but that project's variables and literal values. A file naming vault items or another project needs `--env-file`. Only the desktop app maps a directory to a project, so without it name the project, as in `keypaste run acme-api -- npm start`, or the reference file, as in `keypaste run --env-file .env.keypaste -- npm start`; given neither, `run` starts nothing and says which to add. A found or named reference file is always announced with its project and environment, and every literal is listed before the vault is opened, because a cloned repository's file can put values such as `HTTPS_PROXY` beside your secrets. A literal that cannot be shown whole on one line, because it is very long or holds line breaks, tabs, trailing spaces or invisible characters, is refused.

<a id="minute-4--the-rest-of-the-repo"></a>

## Repository changes

Keep `.env` in `.gitignore` for teammates and future exports. Keep `.env.example` to document required variable names for contributors.

Document the workflow in your README:

```sh
keypaste env pull dev
keypaste run dev -- npm run dev
```

Commit a `.env.keypaste` from `keypaste env export` if you want the variable names and their environment in the repository; it holds no value. One vault can hold several projects, each with its environments. Use separate vaults and master passwords when environments need different access boundaries.

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

With non-terminal stdin, confirming verbs (`rm`, `env rm`, `env pull`, `env export --dotenv`) require `--yes`. Prefer `keypaste run` in CI to avoid writing plaintext into a runner workspace that may be archived.

## The escape hatch

Export values with `--dotenv` when a tool requires a `.env` file or when moving credentials to another system; `-p` picks the environment:

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

Export requires `--force` to overwrite an existing file. Linux and macOS exports are owner-readable. On Windows the file takes its folder's permissions, which keypaste does not narrow and says so, so export only into a folder others cannot read.

Export uses single quotes where possible, which are literal in `motdotla/dotenv`, `python-dotenv`, `joho/godotenv`, Docker Compose v2 and `sh`. Apostrophes and carriage returns require double quotes and escapes; keypaste names those keys on stderr because readers differ in escape handling. Avoid `docker run --env-file`, which preserves quotes and escapes literally.

## FAQ

### What if I lose my master password?

Without the master password or another usable copy of the credentials, keypaste cannot recover the vault's contents. There is no master-password reset or support backdoor. Item history can restore an earlier value in an accessible vault; it cannot recover the master password, an item that was permanently deleted or a lost vault file.

Keep a written copy of the master password in a secure physical location. Keep current `.kdbx` backups in protected locations. Their confidentiality depends on the master password's strength. keypaste opens a vault that needs a key file, and `keypaste access` attaches one; losing either required factor without a backup can make that vault inaccessible.

If you cannot recover access, rotate each credential at its provider.

### How do I sync it between machines?

keypaste has no built-in sync. You can copy or synchronize the encrypted `.kdbx` with an existing file-sync service or a USB drive; keep the master password separate. A cloud vault synced end to end and a relay for team projects are planned ([ROADMAP](../ROADMAP.md)) and not yet built.

The desktop app merges a save the sync tool delivers with the copy it holds open, keeping both machines' changes when they edited different fields of one item, and Recommendations offers a sync tool's conflicted copy for review and merge. The CLI and `keypaste approve` refuse to save over a file another program changed. Edit in one place at a time where you can, and let synchronization finish before switching machines.

### Can my teammate use the same vault?

Someone with the file and master password can read everything in that vault. If you choose to share one, use a dedicated work vault and transfer its password through a separate trusted channel. This gives no per-person revocation or individual accountability and has the concurrent-edit limitation above. Team projects, with access per person, are planned and not built. Removing file access cannot invalidate values someone already copied; that requires changing them at their providers.

### Does it work offline?

Yes. The vault is a file on your disk, and keypaste opens, reads and saves it with no network request. Only sharing goes online: `keypaste share` and the app's Share upload only ciphertext, to keypaste.com by default, and listing or revoking a share link asks that service about the link ([SECURITY](../SECURITY.md#plaintext-and-file-limits)).

### What does KeePassXC see?

Ordinary entries. Each variable is a protected field, named as the variable, of an entry whose tag puts it in the project: `env/dev/.env`, tagged `env:dev`, for the ones keypaste added, or any entry you tag yourself. A project kept the earlier way has an entry per variable under `env/dev`, its title the name and its password the value. You can read, edit, add and delete either in KeePassXC with no knowledge of keypaste; [FEATURES](FEATURES.md#keepassxc-compatibility) says what the compatibility tests check.

### Can I keep using `direnv`?

Use `keypaste run` within the command `direnv` starts. Explicitly exporting values into an interactive shell exposes them to every process that shell launches. Those values persist independently of the desktop's lock state; this is not automatic activation or revocation.

### Why did my value change when I imported it?

Check the import plan and parser rules. keypaste differs from `motdotla/dotenv` in comment and duplicate-key handling:

A `#` starts a comment only after a space, so `PASSWORD=hunter2#42` retains `#42`; dotenv truncates it to `hunter2`. Duplicate keys are rejected. The [upstream dotenv parser](https://github.com/motdotla/dotenv/blob/master/lib/main.js) keeps the later value; keypaste requires the duplicate to be resolved before importing.

Inside double quotes, `\n`, `\r`, `\t`, `\\` and `\"` expand, as they do in C or Python. If you mean a literal Windows path, write `'C:\temp'` in single quotes.

See [README.md](../README.md) for commands and exit codes, and [SECURITY.md](../SECURITY.md) for security limits.
