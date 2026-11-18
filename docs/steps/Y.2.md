# Y.2 — Every test run opens a vault from each published release

Completed 2026-11-18.

Each published CLI release, v0.1.0, v0.2.0 and v0.3.0, wrote a sample vault with its own published macOS arm64 binary, committed under [`tests/Keypaste.Core.Tests/ReleaseVaults/`](../../tests/Keypaste.Core.Tests/ReleaseVaults/README.md) with the commands that made it. `ReleaseVaultFixtureTests` opens each with today's Core, checks every item's fields exactly, and saves a copy and reopens it, holding [law 14](../../SECURITY.md#security-laws)'s first half. A further check fails when a committed release directory is not in the test's list. Each future release adds its own vault; RELEASE has no step list for a one-line step, so the README carries that instruction.

## Evidence
`ReleaseVaultFixtureTests` passes, and `verify.sh compile`.

## Limits and follow-ups
- These releases could write no custom field, tag, attachment, recycled item or key-file-protected vault, so no fixture holds one; the first release that writes them adds a vault that does.
- A future release adds its vault only if someone runs the README's commands with its published binary; nothing enforces it.
