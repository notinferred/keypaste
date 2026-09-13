# Contributing to keypaste

## Changes and review

Read [PRODUCT](docs/PRODUCT.md) for scope and security laws, [STEPS](docs/STEPS.md) for the active build plan, and [CLAUDE.md](CLAUDE.md) for writing rules and document ownership. Product scope changes require dated founder re-ratification; proposals do not override the current requirements.

Keep changes focused and include their documentation. Shared feature logic belongs in `Keypaste.Core`; the CLI and desktop use it. Document features available in only one front end. Secret-path changes require tests, including encryption, injection, the agent bridge and secret display. New secret-path dependencies require written justification, pinned versions and lock files regenerated with `dotnet restore --force-evaluate`.

Use KDBX4 through the vendored library. Do not implement cryptography. Only `src/Keypaste.Core/Internal/KeePassInterop.cs` may reference KeePassLib types outside `third_party/KeePassLib`; application code uses the core boundary. Every KDBX file keypaste writes must open in real KeePassXC. CI permanently checks compatibility in both directions.

Write self-documenting code with clear names and structure. Default to no comments; use one line only for a non-obvious constraint or decision the code cannot express. Do not repeat code, tests or documents. Write concise, connected prose without hard wrapping, redundant recaps or excessive formatting.

`scripts/verify-demo.sh` checks README, launch, demo, KeePass/agent essay and site transcripts against the built binaries. These pages trigger backend CI on pushes to `main`; documentation pull requests run both workflows. Consult [RELEASE](docs/RELEASE.md) before changing published installation claims.

## Verification and commits

Run `./scripts/verify.ps1` in PowerShell or `bash scripts/verify.sh` in Bash after final edits. The shared command includes backend, desktop and consistency tests. [CLAUDE.md](CLAUDE.md#local-verification-and-delivery) owns profiles, prerequisites and checkpoint rules; `--list` prints commands without executing them.

Sign off every commit, including maintainer and agent commits:

```sh
git commit -s -m "your subject line"
```

The `Signed-off-by` trailer certifies the [Developer Certificate of Origin 1.1](https://developercertificate.org/): you wrote the change or have the right to submit it under the project's licence. `dco.yml` checks commits added by each pull request. There is no CLA. Use a subject of at most 72 characters and the required trailer, with no other body unless requested. Follow CLAUDE.md's project identity rule and merge locally.

## Security and licence

Report vulnerabilities privately to `security@keypaste.com`; [SECURITY.md](SECURITY.md) describes scope and response times. Anonymous reports are welcome. Do not use public issues, discussions or pull requests for security reports.

Contributions are licensed under [AGPL-3.0](LICENSE). Every release publishes its corresponding source.
