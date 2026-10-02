# C.5a2 — Check the env-field release where V-C.5a asks

Completed 2026-10-01 on `task/c5a2` above `6d0d1f4` (F.40), scripts, workflows and documents only; dev runs 36876221478 (Linux), 36881371090 (macOS and Linux) and 36889364149 (Windows) passed the gates at `d1e1aaa`, and the squashed commit adds only documents to that commit's scripts and workflows; final dev runs 36904562317 at `30fd6b4` on all three runners and 36992451621 at `c19f3dc` on Linux, after the site README and push-trigger changes; ci 36995550842 and app 36995553700 passed at `0319b47` on `integrate2`, with dev 36995556633 (all lanes and gates on all three runners). With these checks V-C.5a passes, so C.5a closes with this record and [C.5a1](C.5a1.md).

## Amendments

- The founder directed on 2026-09-30 that rows left open that day be finished with no new steps. The checks extend [verify-keepassxc-fields.sh](../../scripts/verify-keepassxc-fields.sh), whose second vault `keepassxc-cli` imports with `api/OpenAI`; no script was added and nothing under `src/` changed.
- The Worker on loopback is the site's own `src/worker.js` and `wrangler.jsonc` under `wrangler dev` from `site/`'s locked packages (wrangler 4.114.0), with `SHARE_DEV_MEMORY=1`. The gate writes that configuration without its two Hyperdrive bindings, because `share.js`'s `openStore` uses `SHARE_DB` whenever it is bound and reaches the memory store only without it (D-0399).
- `site/README.md`'s local `wrangler dev` procedure had not reached the memory store since the `SHARE_DB` binding was added on 2026-09-25, because `openStore` uses that binding whenever it is bound. At the founder's direction to leave nothing open, the README now gives the procedure this gate runs: the configuration without its Hyperdrive bindings, with `SHARE_DEV_MEMORY` set.
- The compat lane in `ci.yml`, `dev.yml` and `verify.sh compat` now builds the app driver before the fields gate and runs `npm ci --prefix site`; the three runner images carry Node 22.23.2. `verify.sh`'s map selects compat for changes to `site/src`, `wrangler.jsonc`, `package.json`, `package-lock.json` and `share-crypto.js`, and `ci.yml` no longer leaves those paths and `site/public/s/` out of its push trigger, so a site change the gate depends on is gated on `main` too.
- The third check needed no change: F.40 made the nine desktop gates run under macOS's bash 3.2, and `verify-desktop-approval.sh`'s custom-field phase passed on `macos-15` in 36881371090.

## Evidence

Each check below runs on the `api/OpenAI` entry KeePassXC imported, whose password, protected `OPENAI_API_KEY`, `Recovery codes`, `otp` and `KP2A_URL_1` hold distinct values; after each check the gate looks for every other value in the results, the audit logs, the agent's and the app's output and the terminal.

- **Agent and run tool.** The existing release through a real `keypaste agent` and `keypaste-mcp --expose 'api/**'` is unchanged. The same agent is then asked by `run` under `--allow-run` with `OPENAI_API_KEY=kp:///api/OpenAI#OPENAI_API_KEY`; its prompt's `injects` line names `api/OpenAI · OPENAI_API_KEY`, Deny refuses the run, and the sixth audit line is the run's prompted denial.
- **Policy rule.** With stdin at end after the master password, an agent whose `policy.toml` rule names `fields = ["OPENAI_API_KEY"]` releases exactly that value with no prompt, audited as `policy` with that field, and a `password` request draws one prompt and is refused. An agent given a rule naming `Recovery codes` reports it NOT in force; `OPENAI_API_KEY` then reaches a prompt and is refused, and `Recovery codes` gets the fixed denial with no prompt.
- **`run --env-file`.** `OPENAI_API_KEY=kp:///api/OpenAI#OPENAI_API_KEY` puts exactly KeePassXC's value in the child; `CODES=kp:///api/OpenAI#Recovery%20codes` exits non-zero and the child never runs.
- **App-held path.** `Keypaste.AppDriver hold` unlocks a copy made before any keypaste process opened the vault. A bridge's request draws a prompt reading `label=fields-probe entry=api/OpenAI field=OPENAI_API_KEY for=once, or for 1 hour`; Allow once returns exactly the value, audited as a prompted grant of that field with `granted_seconds` 0. On a second bridge allowed for 1 hour, the value returns, a `password` request draws its own prompt and is denied, and `Recovery codes`, `otp`, `KP2A_URL_1` and `URL` each get the fixed denial and an audit line with `field: invalid` and `method: invalid-request`, with no further prompt drawn.
- **Share.** The Worker answers an unknown share with the gone 404 before anything is shared, which only its memory store gives. `keypaste share api/OpenAI --field OPENAI_API_KEY --print` and `keypaste share 'kp:///api/OpenAI#OPENAI_API_KEY' --print` under `KEYPASTE_SHARE_URL=http://127.0.0.1:8787` each print a link on that origin. Node opens each as the viewer does, importing `site/public/s/share-crypto.js`: the status call, the check tag, then the one view, which leaves 0 views and a payload titled `OpenAI` holding one field, `OPENAI_API_KEY`, with KeePassXC's value. `--field "Recovery codes"` and `kp:///api/OpenAI#Recovery%20codes` exit non-zero, and the Worker's log then holds exactly two `POST /api/share 201` lines and none of the values.

**Runs.** Every build and test ran in `dev.yml`; this Mac built nothing.

| Run | Commit | Scope | Result |
|---|---|---|---|
| 36876221478 | `d1e1aaa` | auto, Linux | Green: backend 3233 of 3252 passed and the rest skipped, App.Tests 858 of 862, Consistency 44, the nine desktop gates, integration, and compat under KeePassXC 2.7.6, where the new steps took 57 s |
| 36881371090 | `d1e1aaa` | auto, three OSes | macOS green: backend 3234 of 3252, App.Tests 858 of 862, Consistency 44, the nine desktop gates under bash 3.2, integration, and compat under KeePassXC 2.7.12. Linux green. Windows stopped in the backend tests at `ApproverListenerTests.APeerThatSpeaksOutOfTurn_LosesItsRequestAndItsConnection` (`Assert.Single()` on an empty collection at line 328), a test this step does not touch, before its gates ran |
| 36889364149 | `d1e1aaa` | build, compat, Windows | Green: every compat gate `dev.yml` runs, the fields gate among them, under KeePassXC 2.7.12; dispatched with `gh workflow run` because the record was then uncommitted |

## Decisions

- D-0399: the fields gate shares through the site's own Worker under `wrangler dev` on loopback without its Hyperdrive bindings, and the compat lane installs `site/`'s locked packages and needs Node 22.
- Binding only this step's gate: the app's half runs on a copy of the KeePassXC vault taken before any keypaste process opens it, and the share runs last because it saves a share record into the vault.

## Limits and follow-ups

- **Run tool denied.** The gate shows the `run` tool's prompt naming the entry and field and the run refused; the child receiving the value is shown through `run --env-file`, not through the tool.
- **Uploads seen in the Worker's log.** "Uploads nothing" rests on wrangler's request log; a change to its format fails the gate rather than passing it.
- **Desktop gates on macOS run only in `dev.yml`** (F.40), so the custom-field phase's macOS evidence is 36881371090.
