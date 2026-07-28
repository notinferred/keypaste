# ARTIFACTS.md — what informs the product and is not in git

> Referenced **by location, never linked**, and never by value. Nothing here is a secret; several
> rows say where a secret lives, which is a different thing and is the point of the row.
>
> `[process]` — nothing in this repository can check any of it. The only mechanizable column is
> "last confirmed", and it is a person's word.

---

## Private notes

| what | where | last confirmed |
|---|---|---|
| Business model, pricing ladder, positioning, acquisition path, launch and revenue benchmarks, pivot and failure conditions | `~/Nextcloud/keypaste/business.md` | 2026-07-28 |
| Working pricing and licensing notes | `keypastebusinessnotes.md` in the working tree — **gitignored** by `.gitignore` rule `*businessnotes*.md`, and verified never to have been committed | 2026-07-28 |

D-0006 removed this material from the roadmap and the parking lot before the repository was
published. It must not come back. `docs/STEPS.md` says "tracked privately, outside this repo" where
a benchmark would otherwise be named, and that is deliberate.

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
| CI runners | GitHub Actions, with Blacksmith runners for the three-OS matrix | |

The site has **no CI job**. `ci.yml` is the .NET gate and does not look at `site/`. The pre-deploy
checklist in `site/README.md` is the whole of the protection, and it is Owner Queue row **H-0011**.

---

## Names not yet held

| what | where | status |
|---|---|---|
| GitHub org `keypaste` | github.com | **not registered** — Owner Queue **H-0001** |
| npm and crates names | npmjs.com, crates.io | **not registered** — **H-0001** |
| Trademark on "keypaste" | — | **not checked** — **H-0002** |

The repository itself is `notinferred/keypaste` and is **private**. Whether it goes public is
**H-0003**, and it is the precondition every launch link depends on.

---

## Recordings and fixtures

| what | where | note |
|---|---|---|
| The demo cast | `scripts/demo/` in this repo — committed on purpose so a take is reproducible | the pipeline is WSL-only |
| The demo GIF | **does not exist yet** — Owner Queue **H-0005** | both pages reserve the slot |
| KeePassXC for the Windows compat job | fetched from the official zip, pinned by SHA-256 in `ci.yml` | changing the pin is a security decision |
| Published `v0.1.0` assets | `https://dl.keypaste.com/v0.1.0/` — four native binaries, the corresponding source, and checksums | immutable; the pipeline will not republish a version |

---

## Upstream

| what | where | note |
|---|---|---|
| KeePass source | vendored from the KeePass 2.61 netstandard port, commit and licence recorded in `third_party/KeePassLib/UPSTREAM.md` | GPL-2.0-or-later; two documented `#if` changes, nothing else |
