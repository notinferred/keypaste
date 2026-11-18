# A.6 — NuGet and npm packages are proposed for update automatically

Completed 2026-11-18, source only.

Dependabot, which already moved the Actions pins, now also proposes NuGet updates over `Directory.Packages.props` and npm updates in `site/`, `site/web/`, `extension/`, `scripts/real-tools` and `scripts/mcpb-cli`, each ecosystem grouped into one monthly pull request. `cooldown: default-days: 7` holds back any version younger than seven days; GitHub documents that cooldown applies to version updates only, so a security update is proposed at once.

Dependabot was chosen over Renovate on the evidence of both projects' source. Each regenerates NuGet lock files the same way, a per-project `dotnet restore --force-evaluate` (Dependabot's `LockFileUpdater`, Renovate's nuget `artifacts.ts`), and each honours central package management, so the old reason for leaving NuGet off is gone. Renovate's `minimumReleaseAge` and its security bypass match Dependabot's `cooldown`, but Renovate needs an app installed and adds a second bot. Neither rewrites THIRD_PARTY_NOTICES.md, so `dependency-notices.yml` runs on every push Dependabot makes to a `dependabot/nuget/**` branch. It regenerates the lock files with CONTRIBUTING's force-evaluate restore over both solutions and the consistency project, runs `third-party-notices.sh generate`, and commits any change as keypaste with a sign-off. Because a push made with the workflow's token starts no workflow, it then dispatches `ci` and `app` on the branch. Restore can run code from the new packages, so it runs in a read-only job that uploads a patch; a separate job with write access applies it only if it touches nothing but lock files and the notices, and runs no package code.

There is no vault-safety suite yet, so an update merges only after the checks a pull request needs green: `ci ok` and `app ok`, as CONTRIBUTING describes, and dco's `signed-off`.

## Evidence
`WorkflowRulesTests` (the new workflow's Actions are pinned to commits). Both YAML files parse. No local actionlint ran (no Docker here); ci's `workflows` job runs it on push.

## Limits and follow-ups
- Nothing runs until this reaches `main`, and nothing here has yet been watched producing a pull request. The first grouped NuGet pull request is what proves that the lock files and notices regenerate and that `ci ok` and `app ok` report on the regenerated commit.
- A merge waits on green checks only where branch protection requires `ci ok` and `app ok`. Making them required, and turning on Dependabot alerts and security updates in the repository's settings, are the founder's acts.
- Once the workflow commits to a Dependabot branch, Dependabot stops rebasing it; `@dependabot recreate` starts it again, and the workflow runs again on the new push.
- A dispatched `ci` run is a full run with macOS, which costs more than a pull request's run; it happens only when the notices or lock files change.
- When A.2 moves `site/` out, its two npm entries leave `dependabot.yml`.
