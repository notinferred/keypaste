# F.108 — Limit keypaste.com's signups to 10 a minute per address

Completed 2026-11-10 on `backlog-subscribe`, site only.

## Amendments
No STEPS row: it closes BACKLOG's `/subscribe` investigation candidate by the founder's decision of 2026-11-09, 10 a minute per address, matching share creation, verified on a preview before `main`.

## Evidence
`site/test/subscribe-api.test.mjs`: two of its three tests failed before the change and all pass after; `npm test` in `site/` passes 48 of 48.

Local `wrangler dev` with no database binding: 10 honeypot POSTs answered 303, the next three 429 with the page. `wrangler dev --remote` on Cloudflare's network with the real limiter, no database binding and no routes: 11 answered 303 and the next 14 answered 429. Only honeypot submissions were sent, so no row was written. Version `bb5ac08e-d5d9-4fb7-86b9-fe64659ce56a` was uploaded with `wrangler versions upload`, showing the binding; production stayed on `fdfda0d1-6991-4b2b-a4d1-e5ccb0d1f730` at 100%.

## Limits and follow-ups
`keypaste-site` has no preview URL, so that version has answered no request; the remote session is the preview evidence, which the founder accepted on 2026-11-10 as the preview. Cloudflare's limiter is per location and approximate, as the 11th submission above shows. People behind one address share the 10.
