# Contributing to keypaste

keypaste is a secrets tool. Trust is the only asset it has, so the rules below are shorter and stricter than most projects', and the ones that can be mechanized are.

## Sign off every commit

One `Signed-off-by` line per commit, and nothing else is asked of you — no CLA, no signing flow, no bot, no account (**D-0055**).

```sh
git commit -s -m "your subject line"
```

That line is the [Developer Certificate of Origin 1.1](https://developercertificate.org/): you are certifying that you wrote the change, or that you have the right to submit it under the project's licence. `.github/workflows/dco.yml` checks it on every pull request, and it checks only the commits your pull request adds.

A CLA would buy the freedom to relicense later. AGPL-3.0 is chosen and staying (**D-0041**), so that freedom has nothing to buy here, and the price is a real deterrent to the drive-by fix this project wants.

**This binds the maintainer too.** Every commit in this repository needs `-s`, including the ones an agent writes.

## Before you open a pull request

**Read [`docs/PRODUCT.md`](docs/PRODUCT.md).** It is the locked core, sections 1–6 do not change, and a change that conflicts with it is wrong no matter how good it is. Section 2 in particular is a list of things this project will not become, and it is deliberately permanent.

**A change on the secret path needs a test** — encryption, injection, the agent bridge, or anything a secret is drawn on (law 4.5). "It obviously works" is what the tests are for.

**A new dependency on the secret path needs written justification in the pull request** (law 3.9). Dependencies here are minimized and pinned, and every package change also needs `packages.lock.json` regenerated with a `--force-evaluate` restore or a locked-mode CI restore cannot hold (**D-0004**).

**Never write cryptography** (law 3.6). KDBX4 via the vendored library, and nothing invented. `third_party/KeePassLib` is the only place in the repository permitted to reference KeePassLib directly (**D-0007**); everything else goes through the interop boundary.

**Any KDBX file keypaste writes must open in real KeePassXC** (law 4.6). This is gated in CI in both directions and the gate is permanent.

## What a good change looks like

- **Commit messages are a subject line, 72 characters or fewer**, no body. `Signed-off-by` is the one trailer that belongs below it.
- **Small and focused.** One change, one reason.
- **Documentation ships with the feature**, not after (law 4.8).
- **CLI before GUI.** Every feature exists in the CLI first, and the GUI calls the same core library (law 4.2).

Five pages — `README.md`, `launch.md`, `docs/demo.md`, `docs/keepass-and-agents.md` and `site/public/index.html` — are held by `scripts/verify-demo.sh` to what the shipped binaries actually print. Editing one is a code change wearing a markdown extension, and it runs the full build on purpose.

## Merging

Maintainer note, recorded here because it is easy to get wrong: **merges happen locally, never with the GitHub merge button**, which stamps its own identity on the merge commit.

## Security problems do not go here

**Do not open an issue, a discussion, or a pull request for a security problem.** Email `security@keypaste.com` — [`SECURITY.md`](SECURITY.md) has the details, including what is in scope and what to expect. Reporting anonymously is fine.

## Licence

By contributing you agree your work is licensed under [AGPL-3.0](LICENSE), the licence the project ships under and keeps (**D-0041**). Every release publishes its corresponding source.
