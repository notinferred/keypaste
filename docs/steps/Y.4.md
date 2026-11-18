# Y.4 — Linux CI tests against one pinned KeePassXC

Completed 2026-11-18, source only.

`scripts/install-keepassxc.sh` now installs upstream's KeePassXC 2.8.0 rebuild AppImage on Linux, x86_64 and aarch64, each by its SHA-256, extracts it rather than mounting it, since hosted runners may lack FUSE, and installs the host libraries it expects; it fails, naming the libraries still missing, unless the extracted `keepassxc-cli` reports that version. Every Linux install in `ci.yml`, `release.yml`, `install-desktop.yml` and `upgrade-desktop.yml` calls it in place of Ubuntu's package and fails when it fails, so one `VERSION` governs Windows, macOS and Linux and changing it takes a commit. The gates find the AppImage's app beside its CLI.

## Evidence
Linux CI's backend, compat, aot and release jobs run the KeePassXC tests and gates against the pinned build; `WorkflowRulesTests` holds every Action pinned.

## Limits and follow-ups
CI no longer runs a KeePassXC 2.7 release, the oldest reader [law 14](../../SECURITY.md#security-laws) names; BACKLOG holds it.
