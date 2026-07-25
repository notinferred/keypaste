# Security Policy

keypaste handles secrets. Trust is the only asset the project has, so security reports are treated
as the highest-priority work in the repository.

## Reporting a vulnerability

**Email `security@keypaste.com`.** Please do not open a public issue, discussion, or pull request
for a security problem.

Include whatever you have: affected version or commit, a description of the issue, reproduction
steps or a proof of concept, and the impact you believe it has. Reports in any language are fine.

If you would rather report anonymously, that is fine too — no identifying information is required,
and no report will be ignored for lack of it.

## What to expect

| | |
| --- | --- |
| Acknowledgement | within 72 hours |
| Initial assessment | within 7 days |
| Fix or mitigation plan | communicated as soon as it exists, with an honest timeline |
| Coordinated disclosure | up to 90 days, shortened by agreement or if the issue is being exploited |

You will be credited by name or handle in the advisory and changelog if you want to be, and left
out if you do not.

There is no bug bounty. keypaste is pre-1.0 and unfunded; this is stated plainly rather than
implied by silence.

## Disclosure commitment

Per CORE.md §3.10: if keypaste is breached, or a serious bug ships, it will be disclosed fast and
fully — what happened, what was exposed, what changed. No quiet patches for security-relevant bugs.

## Scope

In scope: anything in this repository — the core library, the CLI, the MCP bridge, the build and
release pipeline, and the dependency chain on the secret path.

Out of scope for now: the keypaste.com marketing site, and any deployment of keypaste that a third
party operates. Vulnerabilities in upstream dependencies should be reported upstream first; tell us
too, so the pinned version can be moved.

## Supported versions

Pre-1.0, only the latest commit on `main` is supported. Once releases are tagged, this table will
list the supported release line.

## Design commitments worth knowing before you test

These are constitutional (CORE.md §3) and a violation of any of them is a valid report:

- The vault master key never leaves the local process.
- Agents never receive the vault — only one credential, one scope, one TTL, after explicit human
  approval or a policy the human wrote. Default is deny.
- Every agent access is logged locally, immutably, in human-readable form.
- No secret ever touches disk unencrypted by keypaste's doing.
- No telemetry on secret content or entry names, ever.
- All cryptography comes from mature audited libraries implementing the KDBX4 spec. No custom
  cryptography is written here.
- Every error path in the agent bridge results in denial, not exposure.

## Maintainer note

While the repository is private, `security@keypaste.com` is the only reporting channel. GitHub's
private vulnerability reporting will be enabled as a second channel when the repository is made
public.
