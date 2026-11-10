# F.93 — `keypaste share` links the terms the first time

Completed 2026-11-09 on `backlog-2026-11`, source only; ci 42822341787 and app 42822347029 at the final code `4fa4c1e9`, both green on Windows, macOS and Linux.

## Amendments

The founder selected BACKLOG's row on `keypaste share` and the terms of use, deciding on one line linking the terms the first time a person shares, with no prompt. The founder left where it prints and what it remembers open. It prints on stderr after a successful create, once the link is handed over, so a refused share or one withdrawn for want of a clipboard neither says it nor uses it up. A share to another service through `--endpoint` or `KEYPASTE_SHARE_URL` prints nothing, since keypaste.com's terms do not cover it, and the first share to keypaste.com still prints it.

## Evidence

The fact is `terms_line_shown = 1` in a `[[share]]` section of the home's `app.toml`, written through `AppSettings.Save`, away from `[[settings]]`, whose eight-key limit Q.11 recorded. It is only ever written as 1, so the running desktop app's older copy cannot take it back. It holds no link, id or value.

`ShareVerbTests` gains seven cases driving the real verb, all failing before the change (39 run, 7 failed, 32 passed) and passing after: the first share and not the second, once per home, `share ls --json` parseable and leaving the line, an unreadable `app.toml` left byte for byte, one that is a directory and a home refusing the write each still succeeding and showing the line, a withdrawn share and another service. `AppSettingsTests.The_share_terms_line_stays_remembered_through_a_save_from_a_copy_read_before_it_was_shown` covers the stale copy. After the change `ShareVerbTests` passed 39 of 39 and `AppSettingsTests` 69 of 69; `verify.sh compile` passed. `verify-demo.sh` and the transcripts it checks do not read `keypaste share`, and `verify-keepassxc-fields.sh` shares through a loopback endpoint with its stderr in a file, so neither changes.

## Limits and follow-ups

`https://keypaste.com/terms` answers 404 on 2026-11-09. H-0027 has to settle that page before 1.0.0, the only release carrying this line, and the line's "cover share links" follows the draft terms H-0027 approves. Share creation takes no `--json`; its machine output is `--print`, whose stdout stays the link alone. A home whose `app.toml` cannot be read or written shows the line on every share. The first upload happens before the line is read, which H-0027 may weigh.
