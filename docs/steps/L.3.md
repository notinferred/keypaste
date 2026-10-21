# L.3 — Write the safe-agent guide

Completed 2026-10-21 on `main` at `c208ad6`, source only: ci 39633992142 and app 39633992293, every job green, and ci 39634014306 ran the `tools` jobs.

## Amendments

The founder approved the shape on 2026-10-21: a new guide, [safe-agents.md](../safe-agents.md), in CLAUDE.md's guides row and in ci's push `paths-ignore`, since no gate reads it (D-0468); N.9's names `keypaste approve` and `keypaste clients`; each tool's settings from its current documentation, dated; T-14, T-35, T-36 and the same-user boundary linked rather than restated; and a check in ci's `tools` profile wherever a tool runs a command under its sandbox without a model.

Choices the row left open, taken under the founder's decision rule of 2026-10-21 and recorded as founder-delegated:

- **Claude Code's sandbox is not driven.** Claude Code has no command that runs a shell command in its sandbox without a model. The standalone `@anthropic-ai/sandbox-runtime` (`srt`) applies its own settings file, not Claude Code's settings with their protected paths and merges, needs Node 22.12 or later, which the `tools` job does not pin, and on Linux bubblewrap, socat and ripgrep, so a check through it would test a different configuration. The guide's Claude Code settings rest on its documentation.
- **Cursor's Agent CLI is not driven.** Its parameters page lists `agent sandbox run` as hidden, and the row named Codex and Claude Code.
- **Codex's Windows sandbox is not driven.** MXC needs Windows 11 capabilities the `windows-2025` runner is not documented to have, `elevated` needs an administrator-approved setup the runner has not had, and `unelevated` is the weaker fallback.
- **The Linux runner lifts AppArmor's user-namespace restriction** with `sysctl`, the fallback Codex's sandbox page gives, rather than loading the `bwrap-userns-restrict` profile: it does not depend on the profile file being in the image, and the runner is discarded after the job. The guide sends people to the profile.
- **The check uses `sandbox_mode = "workspace-write"`,** the setting the guide gives, not a beta permission profile; the guide names profiles as unexercised.
- **The check writes the runner's default home,** `$HOME/.keypaste`, made empty for the check and removed after it when it was absent, because the gate's own `KEYPASTE_HOME` sits in the temporary folder that `workspace-write` lets commands write.
- **The guide's own probe is `codex -c 'sandbox_mode="workspace-write"' sandbox -- /bin/sh -c 'touch ~/.keypaste/sandbox-probe'`,** after the same `touch` in the project folder succeeds, not an append to `clients.toml`, so a sandbox that fails leaves no `clients.toml` that cannot be read, which would refuse every agent request (T-36).
- **Claude Code's settings** go in user scope with `failIfUnavailable` and `allowUnsandboxedCommands: false`, plus `denyWrite` and matching `Edit` deny rules, since the file tools are outside the sandbox. **Cursor's** run mode is Allowlist with sandboxing, not Auto-review, whose escapes go to a classifier Cursor calls no security boundary.

## Evidence

Nothing was built or run on this machine beyond `bash -n` over the two changed scripts; ShellCheck runs in the `workflows` profile.

- **Documentation read on 2026-10-21**, as the guide's Sources list it: Claude Code's sandboxing, settings reference, sandbox environments, permissions and permission modes; Codex's sandbox, agent approvals and security, permissions, Windows sandbox, configuration reference, developer commands and MCP pages; Cursor's run modes, `sandbox.json` reference, agent security, MCP, CLI parameters and CLI configuration pages.
- **Source, where the documentation is silent.** Codex `rust-v0.162.1`: `cli/src/main.rs` and `lib.rs` give `codex sandbox` the host's options directly (`--permission-profile`, `-C`, `--allow-unix-socket` on macOS) and take `-c` overrides before `sandbox`; `debug_sandbox.rs` takes `writable_roots` from the loaded configuration but runs read-only unless `-c` sets `sandbox_mode`, ignoring the file's; `seatbelt_base_policy.sbpl` allows `process-info*` only for `(target same-sandbox)`; `linux-sandbox/src/bwrap.rs` passes `--unshare-pid` and `--proc /proc`. `@anthropic-ai/sandbox-runtime` 0.0.79 from npm: `macos-sandbox-utils.js` has the same `process-info*` rule and `linux-sandbox-utils.js` passes `--unshare-pid`.
- **Gate.** [verify-real-tools.sh](../../scripts/verify-real-tools.sh), after setup and each tool's reader, on Linux and macOS: from the project folder, `codex -c 'sandbox_mode="workspace-write"' sandbox -- /bin/sh -c …` appends to a file in that folder, which must land; the same append to `$HOME/.keypaste/clients.toml` and to Codex's `config.toml` must exit non-zero with the system's refusal (not permitted, read-only file system or permission denied), so a timeout does not pass, and leave each file's bytes as they were. Those runs also restarted Gemini CLI's bridge afterwards; a review removed that step, since Codex's sandbox never governs that bridge and the step could not fail on anything the guide's settings do. [install-real-tools.sh](../../scripts/install-real-tools.sh) adds Ubuntu's `bubblewrap` and lifts the user-namespace restriction on Linux. Runs ci 39633992142 and app 39633992293.

## Decisions

- D-0476.

## Limits and follow-ups

- **Unchecked by keypaste.** Claude Code's and Cursor's settings, Codex on Windows, and whether any tool's sandboxed command can read other processes rest on documentation or source, not on a run. Whether the agent's commands can reach keypaste's pipe under Codex or Cursor is not documented, and nothing tested it. Claude Code in WSL2 with a `keypaste approve` in WSL2, a Cursor team that sandboxes keypaste's MCP entry, and Codex's permission profiles are untested.
- **The documentation and the pinned Codex differ.** Codex's agent approvals page shows `codex sandbox macos|linux|windows [--permissions-profile <name>]`; 0.162.1 takes no platform word and spells the flag `--permission-profile`, with the other as an alias. The gate uses 0.162.1's form, which Codex's Windows sandbox page also uses.
- **Temporary folders are writable** under Codex's `workspace-write` and Cursor's defaults, so a vault or `KEYPASTE_HOME` kept in one is not protected; the guide says to keep them out.
- **The first `tools` run is the first execution** of the Codex check. If Codex's sandbox does not start on a runner, the gate fails on its in-workspace write rather than passing, and that runner's Codex settings stay unevidenced until the failure is diagnosed.
- **Options, not planned:** drive Cursor's `agent sandbox run` once it is documented as supported, and Codex's `unelevated` Windows sandbox on a branch.
