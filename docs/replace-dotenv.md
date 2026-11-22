# Replace your `.env` in 5 minutes

Move a project's `.env` into an encrypted vault, then start your app with its variables injected, with no file on disk. Each variable becomes a protected field of a KDBX 4 item, which KeePassXC and other KeePass apps can read and edit.

> This guide describes the source build headed for 1.0.0. The published `v0.3.0` differs ([FEATURES](FEATURES.md)).

## 1. Create a vault

```sh
keypaste init ~/keypaste.kdbx
export KEYPASTE_VAULT=~/keypaste.kdbx
```

Put the `export` in your shell profile, or run `keypaste use ~/keypaste.kdbx` once. Otherwise every command needs `--vault`.

Back up the `.kdbx` off this machine now. The copies keypaste keeps in `<vault>.backups` cover a bad save, not a lost disk. Anyone with a copy can guess passwords offline, so use a strong master password.

<a id="minute-1--import-what-you-already-have"></a>

## 2. Import your `.env`

From the directory holding it:

```sh
keypaste env pull dev --keep
```

`dev` names the project. keypaste reads `./.env` unless you give a path. Before writing, it lists the names, never the values:

```
note: a trailing ' #' comment was removed from: PORT. Quote the value if the '#' was part of it.
dev/dev: 4 new, 0 updated, 0 unchanged
  new       DATABASE_URL, MOTD, PORT, STRIPE_KEY on env/dev/.env
env/dev/.env will be created and tagged into dev/dev.
Import 4 variables into dev/dev? [y/N]
```

If any line is malformed, nothing is written. A value that changed on import is explained [in the FAQ](#why-did-my-value-change-when-i-imported-it).

<a id="minute-2--check-it-then-delete-the-file"></a>

## 3. Check it, then delete the file

```sh
keypaste env ls dev
keypaste get env/dev/.env --field DATABASE_URL --reveal
```

`--keep` left the file in place. Once you are satisfied, run `keypaste env pull dev` again and answer the deletion prompt:

```
Deleting removes the file from the directory. It does not overwrite the blocks it
used: on an SSD, on a copy-on-write filesystem, or on any volume with snapshots or
backups, the old contents can outlive the file. If these values may have leaked, rotate them.
Delete '/repo/app/.env'? [y/N]
```

If the file changes while keypaste asks, it keeps the file; run the pull again. Deleting a file does not remove it from git history:

```sh
git log --oneline -- .env     # if this prints anything, treat those values as leaked
```

<a id="minute-3--run-your-app"></a>
<a id="run-your-app"></a>

## 4. Run your app

```sh
keypaste run dev -- npm start
```

This asks for the master password each time. Everything after `--` is the command.

- **No password.** `keypaste run --session dev -- npm start` asks the unlocked desktop app or `keypaste approve` instead, which shows the project, variable names, command and directory ([approvals](approvals.md#runs-that-ask-for-a-projects-variables)). A refusal starts nothing.
- **From the desktop.** Environments can import the `.env` and open a terminal running the project's command ([desktop](desktop.md#environments)).

The child gets the variables in its environment and keypaste's stdin, stdout and stderr. keypaste closes the vault before starting it, passes signals to it, and returns its exit code. The child can still log or forward the values, and locking the vault cannot take them back ([SECURITY](../SECURITY.md)).

<a id="minute-4--the-rest-of-the-repo"></a>

## 5. Update the repository

- Keep `.env` in `.gitignore`.
- Keep `.env.example`, so contributors know which variables to set.
- Document the workflow in your README:

```sh
keypaste env pull dev
keypaste run dev -- npm run dev
```

To commit the variable names without values, use a `.env.keypaste` ([below](#stages-and-references)). Use separate vaults when stages need different access.

<a id="profiles-and-references"></a>

## Stages and references

A project holds one set of variables per stage, such as `dev`, `staging` or `prod`. Everything above used `dev`. Every env verb and `run` take `-p <stage>`:

```sh
keypaste env set acme-api DATABASE_URL -p staging
keypaste env ls acme-api --profiles
keypaste env diff acme-api dev staging
keypaste run -p staging acme-api -- npm start
```

- A stage's variables live on the item `env/<project>/.env` or `.env.<stage>`; `--entry` names another.
- `env diff` names a key one stage lacks or cannot release, and a value two stages share. It never prints a value.
- `prod`, `production` and their `-*` forms are [protected stages](policy.md#protected-stages): every `--session` run asks.

`keypaste env export acme-api -p staging > .env.keypaste` writes a `kp://` reference per variable, such as `DATABASE_URL=kp://acme-api/staging/DATABASE_URL`, which can be committed. `keypaste run --env-file .env.keypaste -- npm start` resolves every reference or starts nothing. Once the desktop app has saved a directory for a project, `keypaste run -- npm start` there finds the project itself.

A reference file's literal values are listed before the vault opens, because a cloned repository's file can set values such as `HTTPS_PROXY`.

## CI

Use a dedicated CI vault holding only what the pipeline needs:

```sh
keypaste init ci.kdbx
KEYPASTE_VAULT=ci.kdbx keypaste env set ci DATABASE_URL
```

Keep its master password in the CI's secrets. A vault in a public repository is open to offline guessing forever, so use a long random password or keep it out.

```yaml
env:
  KEYPASTE_VAULT: ci.kdbx
steps:
  - run: printf '%s\n' "$KEYPASTE_MASTER" | keypaste run ci -- npm test
    env:
      KEYPASTE_MASTER: ${{ secrets.KEYPASTE_MASTER }}
```

keypaste reads one line of stdin for the password. Without a terminal, `rm`, `env rm`, `env pull` and `env export --dotenv` need `--yes`.

<a id="the-escape-hatch"></a>

## Exporting a `.env` again

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

- Overwriting needs `--force`.
- On Linux and macOS only you can read the file. On Windows it takes its folder's permissions, so export only into a private folder.
- Avoid `docker run --env-file`, which keeps quotes as literal text.

## FAQ

### What if I lose my master password?

The vault cannot be recovered. Keep a written copy somewhere physically secure, and backups of the `.kdbx` and any key file. Otherwise, rotate each credential at its provider.

### How do I sync it between machines?

Sync the `.kdbx` with a file-sync service. The desktop app and `keypaste approve` merge a synced save; the CLI's other commands refuse to overwrite one. Edit in one place at a time.

### Can my teammate use the same vault?

Anyone with the file and master password can read all of it, with no per-person revocation. Use a dedicated work vault.

### Does it work offline?

Yes. Only sharing goes online ([SECURITY](../SECURITY.md#plaintext-and-file-limits)).

### What does KeePassXC see?

Ordinary entries: `env/dev/.env`, tagged `env:dev`, with a protected field per variable. You can edit them in KeePassXC without keypaste ([compatibility tests](FEATURES.md#keepassxc-compatibility)).

### Can I keep using `direnv`?

Use `keypaste run` inside the command `direnv` starts. Values exported into an interactive shell reach every process it starts.

### Why did my value change when I imported it?

keypaste parses differently from `motdotla/dotenv` in three ways:

- **Comments.** A `#` starts a comment only after a space, so `PASSWORD=hunter2#42` keeps `#42`.
- **Duplicate keys.** keypaste refuses them until you resolve them.
- **Escapes.** Inside double quotes, `\n`, `\r`, `\t`, `\\` and `\"` expand, as in C or Python. For a literal Windows path, write `'C:\temp'` in single quotes.

## Exit codes

| exit code | meaning |
| --- | --- |
| 0 | success |
| 1 | usage error |
| 2 | internal or environment error (including no usable clipboard, and an item name two items answer to) |
| 3 | vault or item not found |
| 4 | wrong master password |
| 5 | the audit log is not the file keypaste wrote |

`keypaste run` returns the child's exit code: 127 if the command is missing, 126 if it is not executable. Data goes to stdout and diagnostics to stderr.
