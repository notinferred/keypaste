# C.1a — Tag entries into projects and environments

Completed 2026-09-28 on `main` above `6a89ed6`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** core reads an entry's own tags and adds or removes one as an edit with a revision. It refuses a tag holding `,`, `;`, a tab, a control character or edge whitespace, and never reads or writes group tags.

A project tag is `env:<project>` or `env:<project>:<environment>`, matched ordinally:
- the project follows the existing name rule, without `:`;
- the environment is `[a-z0-9][a-z0-9-]{0,31}`;
- `env:<project>` means `dev`.

A malformed `env:` tag is reported and grants no membership. It still protects its entry when its environment names a protected profile in any case. Projects are the tag projects together with the legacy `env/` groups, marked as legacy.

The live-approval check (`ApproverHandler.cs:99,212-215`, `SessionHost.cs:349`) reads the entry's tags as well as its path, so D-0348's protection follows a tag. The CLI gains `env tag <project> <entry> [-p <environment>]` and `env untag`, each naming the fields that join or leave the set. `env ls [<project>]` lists projects, environments with protected ones marked, and member entries, with `--json`.

The tags half of a new `verify-keepassxc-projects.sh` and a tag step in the workflows gate check it against real KeePassXC. The decision on the grammar supersedes D-0347 for new data. Traces to PRODUCT §§1, 2, 3.2 and 4.6.

**Verify (V-C.1a):** the vault is one KeePassXC made with the tags `env:billing`, `env:billing:prod`, `env:billing:Prod`, `finance` and one group tag. `keypaste env ls` names billing with `dev`, a protected `prod`, and exactly the member entries. It reports `env:billing:Prod` and ignores `finance` and the group tag.

After `keypaste env tag billing Stripe -p prod`, real KeePassXC's `show -a Tags` lists `env:billing:prod`, and the file stays KDBX 4.0.

The approval check runs through a real `keypaste-mcp` whose `--expose` covers the entries, and a real `keypaste agent`. A `request_credential` for that entry's password is offered Allow once only. So is one for an entry KeePassXC tagged `env:billing:Prod` through merge.

Protection shown only in a unit test does not pass, and neither do tags written by keypaste for both directions.

The founder selected C.1a together with V.7a and V.7b on 2026-09-28 and approved a plan that settled what the row left open:

- **The group tag and KDBX 4.0.** KeePassLib writes a file with any group tag as KDBX 4.1. A preflight against KeePassXC 2.7.10 before any code showed KeePassXC does the same: it keeps a group's `<Tags>` from imported XML and writes the file as 4.1 (header minor version 1), while the same document without the group tag is 4.0. The founder decided in advance that in this case the check splits. The vault without the group tag must stay KDBX 4.0 after `env tag`. The group-tagged vault must keep the version KeePassXC wrote, and its group tag, which keypaste ignores and leaves in place. The same preflight showed `show -a Tags` prints tags separated by commas, and that `keepassxc-cli merge` of a newer XML-made copy adds `env:billing:Prod` to an entry.
- `Env:` and `ENV:` prefixes are reported as malformed rather than ignored, and protect like any malformed tag.
- `env ls <project>` for a project with an `env/` group prints its variable names exactly as before, then its tagged entries.
- Every entry-level check applies the tag rule, including the one for a `run` naming entries through `kp:///` references, because D-0348 covers every release.
- `env tag` names the env-named fields using C.1b's rule; until C.1b, `run` does not use them.

## What changed for users

An entry can now join a project through its own KeePass tag, which KeePassXC shows and edits like any other tag. `env:billing` puts it in billing's `dev` environment and `env:billing:prod` in `prod`. `keypaste env tag billing services/Stripe -p prod` adds that tag and says which of the entry's env-named fields, such as `STRIPE_SECRET_KEY`, join `billing/prod`; `keypaste env untag` takes it out again, removing every tag that puts the entry in that environment in one edit. Each change is one revision in the entry's history. A project name containing `:`, an environment keypaste would not read such as `Prod`, and an entry that is not there are refused without writing anything, and tagging an entry already in the environment writes nothing.

`keypaste env ls` now lists every project the vault holds, from tags and from the `env/<project>` groups of earlier releases. Under each project are its environments, the protected ones marked, and the entries tagged into each; a project with an `env/` group is marked `legacy`, and its profiles are listed too. `--json` gives the same as `project`, `legacy` and `environments`, each with its `name`, `protected` and `members`. A tag that starts `env:` and breaks the grammar, such as `env:billing:Prod`, is named in a warning with the entry carrying it and adds the entry to no project. `keypaste env ls <project>` for a project known only from tags lists its environments and entries, and `-p` and `--profiles` work on them; for a project with an `env/` group it prints its variable names as before, followed by any tagged entries. Only an entry's own tags count, never a group's, and entries in the recycle bin or keypaste's own groups belong to no project.

An entry whose own tag names a protected environment, such as `env:billing:prod` or the malformed `env:billing:Prod`, is now treated like one in a `prod` group. Every agent request for it asks, at the desktop and at `keypaste agent`, offering Allow once only: no timed grant is kept and no rule releases it. The same holds for a `keypaste run --session` whose references name that entry. Removing the tag, from keypaste or from KeePassXC, removes the protection, as moving an entry out of a `prod` group does.

A tagged entry's fields are not yet its project's variables: `run`, `env export`, `env diff` and the app's Env profiles still resolve only the `env/` layout until C.1b, and the desktop does not show tags until V.7b. This is source only.

## Evidence

Local, Windows 10 Pro 19045, KeePassXC 2.7.10, on `main` above `6a89ed6` with the step's changes uncommitted.

**Core.** [ProjectTagTests](../../tests/Keypaste.Core.Tests/ProjectTagTests.cs) over the grammar: six well-formed tags with their project, environment and protection; twelve malformed ones, including `env:billing:Prod`, `env:billing:prod:eu`, `Env:` and `ENV:` prefixes, an edge space and a 34-character environment, each reported, granting nothing and protecting exactly when a later segment names a protected environment; five tags that are not project tags; the tag made for a project and environment reading back as them; the tag rules; and the path-or-tag protection rule. [VaultTagTests](../../tests/Keypaste.Core.Tests/VaultTagTests.cs): adding is one revision and one `Edited` naming the entry and survives a save; a tag already there, or an entry that is not, changes nothing; `Prod` and `prod` are two tags; removing several is one revision; five refused tags change nothing; an entry tag leaves the file KDBX 4.0; the recycle bin is not read; a tag edit keeps another client's fields, attachment and tags. [ProjectCatalogTests](../../tests/Keypaste.Core.Tests/ProjectCatalogTests.cs): exactly the members of `dev` and a protected `prod`, the malformed tag reported as protecting, `finance` ignored; legacy groups marked and merged by name with tags; the recycle bin and keypaste's own groups excluded. [TagProtectionTests](../../tests/Keypaste.Core.Tests/TagProtectionTests.cs) through a real vault: the source answers from path and tags, fails closed with no vault or an ambiguous name, and a handler given no rule asks every time for a tagged entry with no timed grant while granting the hour for an untagged one. `SessionAuthorityRunTests` gained a `kp:///` reference to an entry tagged `env:acme-api:prod` that is offered once only and keeps no grant.

**CLI and desktop.** [EnvTagVerbTests](../../tests/Keypaste.Cli.Tests/EnvTagVerbTests.cs) over `env tag`, `env untag` and `env ls` in text and JSON, for a tag-only and a legacy project; `EnvVerbTests` gained usage and help cases and its listings now show the legacy mark; `SecretHygieneTests` sweeps `env tag` and `env untag`; `KeyfileOptionTests` gained `env tag`. `DesktopApprovalTests` gained an entry tagged `env:ci:prod`, for which the desktop's own prompt hides Allow for 1 hour and Allow once releases with no lifetime.

**KeePassXC.** [verify-keepassxc-projects.sh](../../scripts/verify-keepassxc-projects.sh) passed against the release builds. KeePassXC imports one document twice, once with the group `services` tagged `env:billing:staging` and once without; it wrote the first as KDBX 4.1 and the second as 4.0. On the first, `env ls` prints exactly `billing`, `dev` with `services/Stripe` and a protected `prod` with `services/Database`, warns about `services/Odd`'s `env:billing:Prod`, and mentions neither `finance` nor `staging`; `--json` matches the same structure. `env tag billing services/Stripe -p prod` on the 4.0 vault names `STRIPE_SECRET_KEY` joining and not `Region`, KeePassXC's `show -a Tags` then lists `env:billing`, `env:billing:prod` and `finance`, and the file is still 4.0; `env untag` removes the `dev` tag. On the 4.1 vault a tag added and removed leaves the version and the group tag as KeePassXC wrote them. `env tag bill:ing …`, `-p Prod` and a missing entry are refused with identical bytes. KeePassXC merges in a copy of `services/Other` tagged `env:billing:Prod`. A real `keypaste agent` then holds the 4.1 vault and a real `keypaste-mcp --expose 'services/**'` asks four times: the dev entry answered with the hour is released; the `env:billing:prod` entry answered with the hour is refused and answered once is released; the merged `env:billing:Prod` entry answered with the hour is refused. The agent says three times that the entry is asked about every time, and the audit log shows granted, denied, granted and denied, all from the prompt.

[verify-keepassxc-workflows.sh](../../scripts/verify-keepassxc-workflows.sh) gained a tag step after the field step: on each of the three vaults the CLI tags the entry carrying the unmodelled data `env:kp94:prod`, KeePassXC reads it, the CLI untags it, KeePassXC no longer does, `-p Prod` is refused byte-identical, and every marker, `kp94-tag` among them, remains. [verify-keepassxc-writeback.sh](../../scripts/verify-keepassxc-writeback.sh) now expects the legacy mark and profile line in `env ls`.

**The command.** `./scripts/verify.ps1` selected workflows, scripts, backend, integration and desktop and passed in about eleven minutes: the backend suites ran 3,023 tests, 3,013 passed and 10 skipped; the desktop suite 720, 718 passed and 2 skipped; Consistency 42 of 42. `bash scripts/verify.sh compat`, with `KPXC_CLI` naming the installed 2.7.10, passed every gate: compat, write-back, history, recycle bin, run, backup, organize, import, fields, projects, keyfile, XML attach and workflows.

Hosted CI has not run. When `main` is pushed, the `ci.yml` compatibility job runs the projects gate on three operating systems and `app.yml` the workflows gate on Ubuntu and Windows.

## Decisions

D-0370 and D-0371 in [DECISIONS](../../DECISIONS.md). The choices the plan settled are under Scope as selected.

## Limits and follow-ups

- Tagged fields are not a project's variables until C.1b; `env tag` names them in advance.
- The agent's prompt still says "protected profile" for a protected tag; N.6 owns the wording.
- `env ls` lists a legacy project's profiles but not its variable entries as members.
- Anyone who can edit the vault can remove a protecting tag, as they can move an entry out of a `prod` group.
- Source only; the projects gate has run only locally on Windows.
