# N.5 — Make the item pane read like a password manager

Completed 2026-09-29 on `main` above `2e3b51f`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Verify (V-N.5):** in the app, an entry with an `https` address opens it through the platform launcher and copies it; one with `javascript:` or `file:` opens nothing and says why. The reference and identifier are absent from the pane's automation tree until "…" is opened. With no session serving agents and no release in the audit log, the agent card is absent; after a release of the entry's password, it is present. `EntriesViewLayoutTests` holds at 960 px.

A view model asserting over a fixture URL without the launcher does not pass.

The founder selected N.5 with N.2 and N.4 on 2026-09-29 and settled what the row left open:

- **The served session's exposure** had no value to read: each bridge sent its `--expose` globs only with each request. A bridge now also announces them when it attaches, for display only, as it announces its client (T-3); each request's own exposure is still the one enforced.
- **A bare address** such as `github.com/login` opens as `https://`, as KeePassXC opens it.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `2e3b51f`.

- **Which addresses open.** [WebAddressTests](../../tests/Keypaste.Core.Tests/WebAddressTests.cs), 21 of 21: `https`, `http`, mixed case and a bare host (with a port or a query) open; `javascript:`, a `javascript://` URL, `file:` in two forms, `data:`, `cmd://`, `mailto:`, `ssh://`, empty, blank, a host-less `https://` and an address with a space never do; nor does one holding a right-to-left override or a zero-width space.
- **The announced exposure.** `ApproverProtocolTests` round-trips an attach with globs and one without, refuses an exposure that is not a list of strings (against a positive control) and one with more than 64 globs. `SessionAuthorityClientsTests` keeps a bridge's exposure with its attachment and none as empty. `RunToolTests.EveryAttach_CarriesTheClientIdentity` shows the real bridge announcing `--expose env/**`. `EntryActivityTests.AgentsCanSee_WhatAnExposureOrRuleCovers_OrWhatLeftTheVault` and `EntryActivityRowsTests.TheAgentAccessCard_ShowsOnlyWhenAgentsCanSeeTheItem` cover the card's rule, the latter with a standing rule in `policy.toml`.
- **The app.** [ItemPaneJourneyTests](../../tests/Keypaste.App.Tests/Session/ItemPaneJourneyTests.cs) drives the app as `App.Launch` composes it, unlocking through the screen and clicking through the window's hit-testing, with a recording launcher in place of the platform's:
  - `https://github.com/login` reaches the launcher, and its Copy puts it on the platform clipboard;
  - `javascript:alert(document.cookie)` and `file:///C:/Windows/System32/calc.exe` show no link, reach the launcher never and show the line saying why;
  - `kp:///Work/pane-login`, the `uuid …` identifier and Copy the reference are absent from the visible automation tree until the "…" toggle is pressed;
  - with no bridge attached and nothing in the audit log the card is absent, and after a real bridge request for the login's password, answered Allow once in the app's own prompt window, it appears;
  - a second item's card appears while a bridge announcing `Personal/*` is attached.

  Its first run in the whole suite failed: the login, then named `github`, showed its card with nothing released. The four-places journey, in the same process, creates a share link for its own vault's `Work/github`, and a share's audit line names no vault, which the activity picture counts for whichever vault is open (D-0361's stated limit for lines without one). The journey's items now have names of their own; the share line is a limit below.
- **The layout.** `EntriesViewLayoutTests` holds at 1000×680 and 960×520, now with the preview's item carrying an `https` address whose link and Copy stay apart and inside the preview. `ScreenRenderer` drew every screen in both palettes with at most one amber element.
- **The command.** `bash scripts/verify.sh` in the step's worktree selected workflows, scripts, backend, integration and desktop. The backend suites ran 3,088 tests, 3,078 passed and 10 skipped, and integration, which drives the real bridge against the real owner, passed. Desktop failed once on the journey above and, resumed with `--from desktop` after the repair, ran 821 tests, 818 passed and 3 skipped (the renderer's draws); Consistency 43 of 43. The `compat` profile was not run: nothing here changes what a vault holds, and the owner and bridge pair it would drive is the one integration drives.

## Decisions

- D-0378: which addresses the pane opens, and when the Agent access card shows.

## Limits and follow-ups

- Opening an address is checked through the launcher seam: the plan's click in a source-built app on Windows, to watch the default browser open, was not made in this session, and no test watches a real browser open on any platform.
- **A share link's audit line names no vault**, so a share made from one vault counts as a release of the item at the same path in any other, for the card as for the table's last-used dot. This predates the step and was reported to the founder rather than planned here.
- A bridge from before this step announces nothing, so an item its agents can see shows no card until one receives it.
- The card says nothing about why it shows when nothing has been released: its summary reads None active.
- Source only.
