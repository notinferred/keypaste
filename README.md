# keypaste

<!-- The demo GIF belongs here, as docs/demo/keypaste-demo.gif — recorded with scripts/demo/ and
     kept under 2 MB. Until it exists the dialog below is the hero, and nothing else on this page
     moves when it lands. -->

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

**Stop pasting secrets into chats.** keypaste is a local-first, KDBX-compatible vault that stores your passwords and env variables, injects them into your projects, and lets AI agents like Claude request exactly one credential — with your approval, scoped access, and a full audit trail — without ever seeing your vault.

[**Claude asks for a key, you approve, the deploy runs**](docs/demo.md) is the whole thing end to end, in about sixty seconds.

- **Local-first, offline.** Your vault is a file on your disk. No account, no cloud service holding your secrets, no network required. Sync it yourself with whatever you already use.
- **Standard KDBX, not a new format.** Everything keypaste writes opens in KeePassXC and KeePass — proved in both directions against a real `keepassxc-cli` on Linux, macOS and Windows on every push to `main` that touches code. If keypaste disappears tomorrow, your data doesn't.
- **Open source, AGPL.** Auditable by anyone, forever. A tool that handles secrets shouldn't ask to be trusted on faith.

**Pre-1.0, and it says so.** Everything on this page works today and is tested on all three operating systems. The binaries are unsigned, the desktop app is not among them — it exists, it browses and edits the same vaults, and it builds from source rather than shipping in a release ([`docs/desktop.md`](docs/desktop.md)) — and the approval prompt is a terminal prompt rather than a native dialog. [`docs/STEPS.md`](docs/STEPS.md) is the plan in tiers — the first open step is what lands next; [`docs/PRODUCT.md`](docs/PRODUCT.md) is the constitution and does not change.

## Install

**Five lines on macOS and Linux, six on Windows, and the checksum line is the reason the others are worth typing.** It checks the archive against a hash published beside it and stops if they disagree. Each binary is a single native file with no runtime to install — nothing needs .NET on your machine. The only thing written outside the directory you run this in is the last line, which puts the two binaries somewhere your shell can find them.

### macOS — Apple Silicon

<!-- install:macos -->
```sh
curl -fLO https://dl.keypaste.com/v0.1.0/keypaste-0.1.0-osx-arm64.tar.gz
curl -fLO https://dl.keypaste.com/v0.1.0/keypaste-0.1.0-osx-arm64.tar.gz.sha256
shasum -a 256 -c keypaste-0.1.0-osx-arm64.tar.gz.sha256
tar -xzf keypaste-0.1.0-osx-arm64.tar.gz
mkdir -p ~/.local/bin && mv keypaste keypaste-mcp ~/.local/bin/
```
<!-- /install:macos -->

Intel Macs are not covered — build from source below. macOS 26 is the last release that runs on them and no runner fleet still offers one, so shipping that slice would have meant publishing a binary no gate had ever executed.

### Linux — x64 and arm64

<!-- install:linux -->
```sh
curl -fLO https://dl.keypaste.com/v0.1.0/keypaste-0.1.0-linux-x64.tar.gz
curl -fLO https://dl.keypaste.com/v0.1.0/keypaste-0.1.0-linux-x64.tar.gz.sha256
sha256sum -c keypaste-0.1.0-linux-x64.tar.gz.sha256
tar -xzf keypaste-0.1.0-linux-x64.tar.gz
mkdir -p ~/.local/bin && mv keypaste keypaste-mcp ~/.local/bin/
```
<!-- /install:linux -->

For arm64, substitute `linux-arm64` in all three filenames. Both are built against glibc 2.35, which is checked on a clean Debian 12 container on every release; Alpine and other musl distributions are checked to *fail* there rather than assumed to, so the gap is measured. Build from source on musl.

### Windows — x64

<!-- install:windows -->
```powershell
$a = "keypaste-0.1.0-win-x64.zip"
Invoke-WebRequest -OutFile $a "https://dl.keypaste.com/v0.1.0/$a"
Invoke-WebRequest -OutFile "$a.sha256" "https://dl.keypaste.com/v0.1.0/$a.sha256"
$want = (Get-Content "$a.sha256" -Raw).Split()[0]
if ((Get-FileHash $a -Algorithm SHA256).Hash -ne $want) { throw "checksum mismatch" }
Expand-Archive $a -DestinationPath .
```
<!-- /install:windows -->

The Windows block stops after extracting rather than moving the binaries anywhere, because Windows has no per-user `bin` directory that is already on `PATH` the way `~/.local/bin` is on Unix. Put `keypaste.exe` wherever you keep such things and add that directory to `PATH` yourself. On macOS and Linux, `~/.local/bin` may also not be on your `PATH` — check with `command -v keypaste`.

Note the absolute path of `keypaste-mcp` either way, because that is what an MCP client needs; it is not something you run yourself.

### What the checksum does and does not prove

**It proves the bytes arrived intact. It does not prove who made them.** The checksum is served from the same origin as the archive, so anyone able to replace one can replace both — it defeats a corrupted download and a network attacker in transit, and it does not defeat a compromised bucket. This is the same limitation [`DECISIONS.md`](DECISIONS.md) records about KeePassXC's own `.DIGEST` file, and saying otherwise would be the more comfortable lie. The binaries are also **unsigned and un-notarized**, so nothing ties them to this project rather than to whoever served them. [`THREATS.md`](THREATS.md) T-21 is the honest version of what you are trusting when you download instead of build, and `SECURITY.md` has the verification steps in one place.

There is deliberately no `curl | sh`. It asks you to execute code you have not read, from an origin that is not this repository, in a form where the server can serve one thing to `curl` and another to a browser. A tool that handles secrets should not open by asking for that.

macOS quarantine: a browser download sets `com.apple.quarantine` and a `tar` extraction does not, so these commands should not hit it. On the release runner, setting the attribute deliberately and running the binary anyway **worked** — but that is one machine's observation, not a guarantee. If your Mac blocks it, `xattr -d com.apple.quarantine ~/.local/bin/keypaste` is the fix, and being told to strip a security attribute is a real cost of shipping unsigned binaries rather than a quirk.

### Or build it from source

**This is strictly stronger than downloading, and it stays here permanently for that reason.** The argument the rest of this page makes — four dependencies, nothing opening a socket, a decision order you can read — is an argument about source you can check. A prebuilt binary is a claim that it was compiled faithfully, and you did not watch it happen. Building needs the .NET SDK pinned in [`global.json`](global.json).

```sh
git clone https://github.com/keypaste/keypaste
cd keypaste
dotnet build keypaste.slnx -c Release
```

`keypaste` lands at `artifacts/bin/Keypaste.Cli/release/` and `keypaste-mcp` at `artifacts/bin/Keypaste.Mcp/release/` (`.exe` on Windows). This is also the only supported route on Intel Macs, on musl distributions such as Alpine, and on Windows on ARM.

## Sixty seconds to a project with no `.env` in it

```sh
keypaste init ~/vault.kdbx              # prompts for a master password, twice
export KEYPASTE_VAULT=~/vault.kdbx      # or pass --vault to every command

keypaste env pull dev                   # imports ./.env, then offers to delete it
keypaste run dev -- npm start           # injected into the child process, nothing written to disk
```

[**Replace your `.env` in 5 minutes**](docs/replace-dotenv.md) is the guide — importing, CI, syncing, the way back out, and honest answers about lost master passwords.

## Connecting it to Claude

Two processes, and the split is the whole design. `keypaste-mcp` is the MCP server your client starts, so software starts it; it holds no vault and decides nothing. `keypaste agent` is the one **you** start in your own terminal; it holds the vault and asks you the question at the top of this page.

```sh
keypaste agent --vault ~/vault.kdbx
```

Then point Claude Desktop at the bridge. Paths must be absolute, and on Windows the backslashes are escaped (`"C:\\Users\\you\\keypaste-mcp.exe"`):

```json
{
  "mcpServers": {
    "keypaste": {
      "command": "/absolute/path/to/keypaste-mcp",
      "args": [
        "--vault", "/absolute/path/to/vault.kdbx",
        "--client-label", "claude-desktop"
      ]
    }
  }
}
```

For Claude Code, the same thing in one line:

```sh
claude mcp add --transport stdio --scope project keypaste \
  -- /absolute/path/to/keypaste-mcp \
     --vault /absolute/path/to/vault.kdbx \
     --client-label claude-code
```

**There is no place in that config for a master password, and there never will be.** [**Connecting keypaste to Claude**](docs/mcp-setup.md) is the full guide, including what `--expose` governs and how to read the audit log back.

## How it compares

Only the wedge — where the secrets live, how they reach a process, and what happens when an agent asks for one. Everything below was checked against each vendor's own documentation in July 2026.

| | keypaste | KeePassXC | 1Password | Infisical |
| --- | --- | --- | --- | --- |
| Where secrets live | a KDBX file you own | a KDBX file you own | 1Password's service | Postgres, theirs or yours |
| Usable with no account | yes | yes | no — a membership is required | no — a server, Postgres and Redis |
| Injecting into a child process | `keypaste run dev -- npm start` | no | `op run -- npm start` | `infisical run -- npm start` |
| An agent can ask for a credential | yes, over MCP | no official integration | yes, over MCP (beta) | yes, over MCP |
| A person answers each request | yes, and no is the default | — | yes | not documented |
| What the agent receives | one field value, for a lifetime you were shown | — | no secret — 1Password injects it instead | not documented |
| Per-access log | local JSONL, hash-chained | no | yes, on Business | yes, on the paid tiers |
| Licence | AGPL-3.0 | GPL-2.0-or-later | source not published | MIT core, paid features |

**keypaste is not the only thing in this space, and pretending otherwise would be the fastest way to lose the argument.** Keeper's MCP server prompts a human before it returns unmasked secret data. Bitwarden published an Agent Access SDK in March 2026 with the same request-and-approve shape, though it is alpha and its logging is not there yet. 1Password's Environments MCP server asks for approval too, and then deliberately never hands the credential over at all — a genuinely different answer to the same problem, not a worse one. `kprun` already injects KeePass entries into a child process and writes a local JSONL log, without an approval step.

What is keypaste's is the combination: the vault is an ordinary KDBX file you own, there is no account and no server anywhere in the picture, a person answers each request unless they wrote a rule saying otherwise, and the log never leaves your disk. Each of the others gives up at least one of those.

## Using it

```sh
keypaste init ~/vault.kdbx           # prompts for a master password, twice
export KEYPASTE_VAULT=~/vault.kdbx   # or pass --vault to every command

keypaste add github --username me --url https://github.com
keypaste ls                          # tree of groups and entries, names only
keypaste get github                  # copies to the clipboard, clears after 20s
keypaste get github --show           # prints to stdout instead
keypaste rm github --yes
```

Passwords are never echoed at a prompt, and `get` never writes a secret to stdout unless you ask for `--show`. Data goes to stdout, everything else to stderr, so `keypaste get x --show` is safe to pipe.

| exit code | meaning |
| --- | --- |
| 0 | success |
| 1 | usage error |
| 2 | internal or environment error (including no usable clipboard) |
| 3 | vault or entry not found |
| 4 | wrong master password |
| 5 | the audit log is not the file keypaste wrote |

When stdin is not a terminal each prompt consumes exactly one line, in a fixed order: `init` takes the password twice, `add` and `env set` take the master password then the value, and everything else takes the master password. That is what makes the CLI scriptable.

## Environment variables

A project's environment variables live in the group `env/<project>`, one ordinary entry per variable — title is the name, password is the value. There is nothing keypaste-specific in the file, so KeePassXC can read, edit, add and delete them with no knowledge of keypaste at all. CI proves that in both directions on all three operating systems; see [`DECISIONS.md`](DECISIONS.md) D-0014 for why this shape was chosen over custom string fields.

```sh
keypaste env pull billing                      # imports ./.env, then offers to delete it
keypaste env pull billing config/.env.prod --yes --delete-source
keypaste env set billing DATABASE_URL          # prompts for the value, hidden
keypaste env set billing STRIPE_KEY=sk_test_x  # or inline, for scripts — see the caveat below
keypaste env ls                                # projects
keypaste env ls billing                        # variable names, never values
keypaste get env/billing/DATABASE_URL --show   # read one value
keypaste env rm billing STRIPE_KEY --yes
keypaste env export billing --dotenv --stdout  # the way back out — see below
```

`env pull` reads the whole file before it writes anything: if any line is malformed it reports every problem and imports nothing, so you never end up with half a `.env` in the vault and no `.env` on disk. It shows a plan first — how many variables are new, updated and unchanged, by name — and leaves unchanged ones alone.

It handles `export` prefixes, comments, all three quoting styles, and values that span lines. Two rules differ from `dotenv`, both deliberately: a `#` only starts a comment when a space precedes it (so `PASSWORD=hunter2#42` keeps its `#`), and a key repeated in one file is an error rather than a coin flip. Values are stored **exactly as written** — `${VAR}` and `$VAR` are not expanded, because expanding them would bake one machine's environment into a vault you sync to others. Inside double quotes `\n`, `\r`, `\t`, `\\` and `\"` expand, so write `'C:\temp'` in single quotes if you mean a Windows path.

Three things worth knowing before you rely on it, all covered in [`SECURITY.md`](SECURITY.md): the `KEY=value` form leaves the value in your shell history and in the process list, so keypaste warns when you use it; setting a variable that already exists keeps the old value in the entry's KeePassXC history rather than erasing it; and deleting a `.env` is tidying, not erasure — keypaste says so rather than offering a "shred" it could not honour.

## Running things with those variables

```sh
keypaste run dev -- npm start          # no .env on disk, nothing written to one
keypaste run prod -- ./deploy.sh
```

The `--` is required. Without it, `keypaste run dev npm start` cannot be told apart from a project called `npm`; everything after it belongs to the command, including flags keypaste also understands.

The command inherits your environment with the project's variables merged on top, and it gets keypaste's own stdin, stdout and stderr — so colours, prompts and progress bars work exactly as if keypaste were not there. **The vault is closed before the command starts**, so a server you leave running for hours is not holding a decrypted database open.

Once the command starts, its exit code is keypaste's; keypaste's own failures always print a line beginning `keypaste run:` first. A command that does not exist reports 127 and one that is not executable reports 126, as in a shell. Ctrl+C reaches the command, and keypaste waits for it rather than dying first — `docker stop` and `timeout` work the same way.

It refuses to run rather than inject something ambiguous: a variable whose name is not a legal environment variable, or two names differing only in case (two variables on Linux, one on Windows). Both are things KeePassXC will let you create and keypaste will not, and both name every offending key so one pass in KeePassXC fixes them.

## Getting them back out

A vault you cannot leave is a vault nobody should adopt, so there is an escape hatch. It is the one command in keypaste that writes plaintext, and it behaves like it.

```sh
keypaste env export billing .env --dotenv        # writes a file, after confirming
keypaste env export billing --dotenv --stdout    # prints it instead, for piping
```

The format has to be named — `--dotenv` is not assumed — and writing a file prints a red warning naming the destination and asks before it goes ahead. It will not overwrite an existing file without `--force`, it points out a `.git` ancestor, and on Linux and macOS the file is created readable only by you. Windows has no equivalent and keypaste says so rather than implying a permission it did not set. Prefer `keypaste run`, which needs no file at all.

Values are written in single quotes wherever possible, because that form means the same thing to `motdotla/dotenv`, `python-dotenv`, `godotenv`, Docker Compose v2 and `sh` alike. The handful that cannot be — a value containing an apostrophe or a carriage return — are escaped and named on stderr, because that is the form those readers disagree about.

## What an agent can and cannot do

Two tools appear in the client: `list_entry_names`, which returns group paths and entry names and never a value, and `request_credential`, which asks you to release one field of one entry.

Nothing is released without you saying yes to that specific request, unless you wrote a rule in advance that covers it. **Your master password is typed at `keypaste agent` and nowhere else.** Any program on your machine can pop up a window that looks like keypaste asking for it, so keypaste never gives you a reason to expect one: no agent, and nothing an agent does, can cause a password prompt to appear. That is why the approver is a separate process you start, rather than something the MCP server does.

Say no and the agent is told not to ask again. Say nothing for 45 seconds and that is a no. Ask for the same field again within the lifetime you approved and you are not asked twice. Every call — granted, denied, or malformed — appends a line to `~/.keypaste/audit.jsonl`, and the value is never in it.

What an agent may even *name* is default-deny: out of the box that is the `env/` subtree and nothing else, and widening it takes an explicit `--expose` glob in the client's config, which is a file you wrote.

### Saying yes in advance

If you are approving the same thing every day, you can write it down once in `~/.keypaste/policy.toml` and stop being asked about that one case:

```toml
[[allow]]
client          = "claude-code"     # the --client-label you gave the bridge
entries         = ["env/dev/**"]
fields          = ["password"]
max_ttl_seconds = 300
max_per_hour    = 20                # optional
```

`keypaste policy ls` shows what your rules mean in plain English — and shows what each pattern actually *parsed to*, because the obvious way to write one is usually not what it does. There is no policy file unless you write one, keypaste never writes it, and **anything at all wrong with it means the whole file is ignored and every request comes back to you**.

This is the one path in keypaste that hands an agent a credential with nobody watching. A rule cannot reach past `--expose`, cannot raise `--max-ttl`, cannot overturn a "no" you just gave, and cannot make an entry listable — but within its pattern, no human sees the request. [**Pre-approving with a policy file**](docs/policy.md) is the guide, including what that costs.

### Seeing what happened

Every call an agent makes is one line in `~/.keypaste/audit.jsonl`, allowed or refused. `keypaste log` reads it back:

```
3 records in /home/you/.keypaste/audit.jsonl

  time (UTC)           client       entry                decision  method
  2026-07-26 14:03:09  claude-code  -                    granted   exposure
  2026-07-26 14:03:11  claude-code  env/demo/STRIPE_KEY  granted   prompt
  2026-07-26 14:07:44  claude-code  env/demo/STRIPE_KEY  denied    out-of-scope
```

`--denied`, `--client <text>` and `--since 2h` narrow it, and a narrowed view always says so.

Each record carries the hash of the record before it, so `keypaste log verify` tells you whether the file is the one keypaste wrote — and tells you, every time it passes, the two things it cannot see: a rewrite that recomputed the chain, and records deleted from the end.

[**Approving an agent's request**](docs/approvals.md) is the guide to what you are actually deciding. [**THREATS.md**](THREATS.md) is the threat model — prompt injection through entry names and through the agent's stated reason, clients that cannot be authenticated, prompt fatigue, what a reused grant costs, and what tampering with the log does and does not achieve.

[**Your KeePass vault can't talk to AI**](docs/keepass-and-agents.md) is the argument behind all of this — why vaults were built with no network surface, what changed when agents arrived, and why the answer here is the one the KDBX ecosystem already reached for when browsers wanted credentials.

## Packages

| Roadmap name | Project | Ships as |
| --- | --- | --- |
| `keypaste-core` | `src/Keypaste.Core` | library — all vault logic lives here |
| `keypaste-cli` | `src/Keypaste.Cli` | `keypaste` |
| `keypaste-mcp` | `src/Keypaste.Mcp` | `keypaste-mcp` — MCP server, stdio, holds no vault and decides nothing |

## Vault format

KDBX4 with Argon2d key derivation (2 iterations, 64 MiB, parallelism 2) and AES-256. keypaste never invents a format and writes no cryptography of its own (docs/PRODUCT.md §2, §3.6): the format layer is [KeePassLib](third_party/KeePassLib/UPSTREAM.md), vendored from KeePass 2.61 and reached through a single file, `src/Keypaste.Core/Internal/KeePassInterop.cs`.

Any vault keypaste writes must open in KeePassXC, and anything KeePassXC writes back must be readable by keypaste. That is not a hope — `scripts/verify-keepassxc-compat.sh` and `scripts/verify-keepassxc-writeback.sh` prove both directions on every push to `main` that touches code, against a real `keepassxc-cli`, on all three operating systems, and the gate is permanent (docs/PRODUCT.md §4.6, [`DECISIONS.md`](DECISIONS.md) D-0008 and D-0014).

Directories and namespaces use .NET's PascalCase convention; the kebab-case names above are the roadmap's and survive where they are user-visible, in the shipped binary names.

CLI, MCP server, and the eventual GUI are all thin clients over `Keypaste.Core` — no logic is duplicated in a frontend (docs/PRODUCT.md §4.3).

## Build and test

Requires the .NET SDK pinned in [`global.json`](global.json).

```sh
dotnet restore keypaste.slnx --locked-mode
dotnet build   keypaste.slnx
dotnet test    keypaste.slnx
dotnet run --project src/Keypaste.Cli
```

Warnings are errors, code style is enforced at build time, and every dependency is pinned by `packages.lock.json`. Adding a package is therefore a two-step: declare it in `Directory.Packages.props` and the project, then `dotnet restore --force-evaluate` and commit the regenerated lock files. **The same applies to the `RuntimeIdentifiers` list**, which is a restore-time input too: changing it changes the restore graph, and a lock file that has not been regenerated fails `--locked-mode` with NU1004.

**Never pass `-r` to a restore.** It narrows the project's runtime identifier set to the single RID you named, which can never match a lock file recording four, so locked mode fails — and the failure reads like a stale lock file rather than like the flag being wrong. The projects declare all four RIDs and `PublishAot=true` is committed, so a plain `dotnet restore --locked-mode` is already correct for every target. Only `dotnet publish` takes `-r`.

### Building a native binary yourself

Cross-OS NativeAOT is not supported, so each platform's binary is built on that platform. What the compiler needs, per O-0005, is `clang` and `zlib1g-dev` on Linux, the Xcode command line tools on macOS, and the MSVC C++ build tools on Windows — and on Windows, `vswhere.exe` has to be findable, which is the part that fails confusingly when the toolchain is installed but the build claims it is not.

```sh
dotnet restore keypaste.slnx --locked-mode
dotnet publish src/Keypaste.Cli -c Release -r linux-x64 --no-restore -o out
dotnet publish src/Keypaste.Mcp -c Release -r linux-x64 --no-restore -o out
```

**Restore first, then publish with `--no-restore`, and the order is not stylistic.** `dotnet publish` restores implicitly, and an implicit restore inherits the `-r` — which narrows the RID set to one and rewrites `packages.lock.json` to match, leaving you with two modified lock files you did not ask for and a `--locked-mode` failure the next time CI sees them. Observed, not theorised. This is the same order `release.yml` uses, for the same reason.

The result is a single native file per project with no runtime to install; `PublishAot=true` is already committed, so it does not need passing. Substitute `osx-arm64` or `win-x64` for the RID you are on. `third_party/` disarms the trim analyzers because vendored source is not ours to annotate, so `scripts/verify-aot-trim.sh` re-arms the check the other way: it reads the publish logs and diffs their trim diagnostics against a committed baseline, failing on anything new and on anything against `src/` at all. Capture the output to run it after a change under `third_party/`:

```sh
dotnet publish src/Keypaste.Cli -c Release -r linux-x64 --no-restore -o out > cli.log 2>&1
scripts/verify-aot-trim.sh cli.log
```

Eleven diagnostics are expected, all from vendored code, each one cleared individually in [`DECISIONS.md`](DECISIONS.md) D-0040. ILC only re-analyses when its inputs changed, so a repeat publish emits none and the script says so rather than reading that as a pass.

`third_party/` is vendored source and is held to different rules — it is re-merged from upstream, not formatted or linted to our taste. `third_party/Directory.Build.props` quarantines it, and `dotnet format` is run with `--exclude third_party/`.

To run the KeePassXC compatibility gate locally you need `keepassxc-cli` on `PATH` (or `KPXC_CLI` pointing at it):

```sh
export KP_COMPAT_PASSWORD=ci-master-pw
scripts/make-compat-fixture.sh ./artifacts/compat/gen.kdbx
scripts/verify-keepassxc-compat.sh ./artifacts/compat/gen.kdbx
scripts/verify-keepassxc-writeback.sh ./artifacts/compat/writeback.kdbx
```

The fixture is built by the shipped `keypaste` binary, so the gate covers the CLI as well as the vault writer. The write-back script builds its own vault and drives both tools in turn: keypaste modifies an entry, then KeePassXC edits and adds env variables that keypaste has to read back.

## Security

Please report vulnerabilities privately — see [`SECURITY.md`](SECURITY.md).

## License

[AGPL-3.0](LICENSE). Auditable code is the trust strategy (docs/PRODUCT.md §3.8).
