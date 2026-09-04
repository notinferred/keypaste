# ARTIFACTS.md — what informs the product and is not in git

> Referenced **by location, never linked**, and never by value. Nothing here is a secret; several rows say where a secret lives, which is a different thing and is the point of the row.
>
> `[process]` — nothing in this repository can check any of it. The only mechanizable column is "last confirmed", and it is a person's word.

---

## Private notes

| what | where | last confirmed |
|---|---|---|
| Business model, positioning, acquisition path, pivot and failure conditions | `~/Nextcloud/keypaste/business.md` | 2026-09-04 (exists; not read for this audit) |
| Tier ladder with figures, KPI targets, comparable pricing with sources and fetch dates | `keypastebusinessnotes.md` in the working tree — **gitignored** by `.gitignore` rule `*businessnotes*.md`, `git check-ignore` confirms it, and `git log --all -- keypastebusinessnotes.md` is empty | 2026-09-04 |

D-0006 removed this material from the roadmap and the parking lot, and D-0072 keeps it out of every commit. The committed pages carry the tier *shapes* and the decisions (D-0063, D-0071); a number appears only in the working file, and `docs/STEPS.md` says "figures in the working file" where one would otherwise be named.

---

## Infrastructure

| what | where | note |
|---|---|---|
| keypaste.com landing page | Cloudflare Worker `keypaste-site`; source is `site/` in this repo, deployed with `npx wrangler deploy` from that directory | the only server-side code the project runs |
| Release downloads | Cloudflare R2, served at `dl.keypaste.com` | `release.yml` refuses to overwrite a version that already exists |
| Signup database | PlanetScale Postgres, table `public.signup`, reached through Cloudflare Hyperdrive | schema is `site/schema.sql` |
| The database role's password | **an account-level Cloudflare Hyperdrive config, and nowhere else** — not in the repo, not in the Worker, not in an env var | rotating it means `alter role … password` plus `wrangler hyperdrive update`; see `site/README.md` |
| Hyperdrive binding id | committed in `site/wrangler.jsonc` — it is a handle, not a credential | |
| Vulnerability reports | `security@keypaste.com`, routed by Cloudflare Email Routing | the only reporting channel that works today |
| DNS for `keypaste.com` and `dl.keypaste.com` | Cloudflare | |
| CI runners | GitHub Actions on Blacksmith runners exclusively; no GitHub-hosted label remains in any workflow | |
| Sync relay host and its S3-compatible bucket | **not yet held** — step 5.2 (H-0019) | the relay is one binary (D-0064); the hosted instance is that binary on a small VM behind Cloudflare |
| Stripe account for Individual and Team billing | **not yet held** — **H-0018** | licence keys are issued by the relay, checked by the relay, and never gate a client |
| Azure Trusted Signing for Windows binaries | **not yet held** — **H-0017** | D-0070; price and eligibility re-verified at enrolment |
| Apple Developer Program for notarization | **not yet held** — **H-0015** | D-0057 |

The site has **no CI job**. `ci.yml` is the .NET gate and does not look at `site/`. The pre-deploy checklist in `site/README.md` is the whole of the protection, and it is **H-0011**, run by hand before every deploy.

---

## Names not yet held

| what | where | status |
|---|---|---|
| GitHub org `keypaste` | github.com | **not registered** — step 0.4 (H-0001) |
| npm and crates names | npmjs.com, crates.io | **not registered** — **H-0001** |
| Trademark on "keypaste" | — | **not filed, deliberately** — D-0058 accepted the risk. No full clearance search was run, and D-0053's one known live collision is the whole basis; a second one is the trigger to revisit |

The repository itself is `notinferred/keypaste` and is **private**. Whether it goes public is **H-0003**, and it is the precondition every launch link depends on.

---

## Recordings and fixtures

| what | where | note |
|---|---|---|
| The demo cast | the pipeline is `scripts/demo/` in this repo; an accepted take is committed at `docs/demo/keypaste-demo.cast` — **none exists yet**, step 3.1 (H-0005) | the pipeline is WSL-only. A cast is committed as text so anyone can grep it for the master password, the sentinel and the dialog `record-demo.sh` asserts — `scripts/demo/README.md` says why. One take was recorded and **rejected**: the credential was never released in it, which `record-demo.sh`'s positive control refuses, so it proved nothing and was not kept |
| The demo GIF | **does not exist yet** — step 3.1 (H-0005) | both pages reserve the slot; it renders from the cast above, so it is blocked on the same take |
| KeePassXC for the Windows compat job | fetched from the official zip, pinned by SHA-256 in `ci.yml` | changing the pin is a security decision |
| Published `v0.1.0` assets | `https://dl.keypaste.com/v0.1.0/` — four native binaries, the corresponding source, and checksums | immutable; the pipeline will not republish a version |

---

## Upstream

| what | where | note |
|---|---|---|
| KeePass source | vendored from the KeePass 2.61 netstandard port, commit and licence recorded in `third_party/KeePassLib/UPSTREAM.md` | GPL-2.0-or-later; two documented `#if` changes, nothing else |
