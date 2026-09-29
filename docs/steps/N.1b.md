# N.1b — Give every advanced feature one home

Completed 2026-09-29 on `main` above `8192987`, source only. [PRODUCT](../PRODUCT.md) and [STEPS](../STEPS.md) own scope and the plan; this record is what the step did and is not revised afterwards.

## Scope as selected

**Build:** the Advanced card on Settings, which already opens the activity log and share links (N.1a1), opens two more screens. One is Scoped tokens, holding New token, its one-time copy and Revoke, moved from Agents. The other is Diagnostics, holding the facts now on Settings' own page. The log's hash check, Verify chain and Copy hash, is offered on the activity log only; Agents › History shows the records, still marking an unverified one, without it. Each client card's choice of Session grants up to 1h, Ask every time or Inject only, the `*` card's included, moves from a dropdown on the card to the card's menu. Nothing is removed. The keypaste-design skill applies. Traces to PRODUCT §5.8.

**Verify (V-N.1b):** a driver through the app's launch composition (D-0342) reaches the following. From Settings' Advanced card: minting, copying and revoking a scoped token, the log's check and the diagnostics facts. From a client card's menu on Agents: changing its choice, which `clients.toml` then holds. By their automation trees, Agents' first level and History carry no token control, no hash check and no choice dropdown, and Settings' own page carries Advanced's Open rows but none of the controls or facts behind them. Every frame of the drive passes N.1a2's amber check. A control left reachable only from its old place, or removed, leaves the row open.

The row was corrected against the code before it was built (`8192987`). Settings › Advanced is a card of Open rows, not a screen. History and the activity log are one view. Agents had no per-app menu, and the `*` card needed one too. The per-app menu bullet moved to here from N.3.

## What changed for users

- **Settings › Advanced has four rows:** Activity log, Share links, Scoped tokens and Diagnostics, each opening a screen with Back to Settings.
- **Scoped tokens is its own screen.** It lists the vault's tokens by name, prefix, scope, mode and expiry. New token is the screen's primary. The form's Create token, then the minted token's Copy, take that place while each is shown, and Revoke still asks first. Agents no longer lists tokens.
- **Diagnostics is its own screen.** It holds the vault, keypaste's home, `KEYPASTE_HOME` and the version, and Settings' own page no longer does.
- **A client's policy is chosen from its card's ⋯ menu.** The card shows the policy it holds as text, and its menu offers Session grants up to 1h, Ask every time and Inject only, with the held one checked. The menu closes on a choice, Escape or a press elsewhere, and opens with focus on its first choice. A client with no label has its ⋯ disabled, with the hint to start its bridge with `--client-label`. The `*` card, Every other client, has the same menu.
- **History leaves the hash check to the activity log.** History still marks a row the chain does not vouch for, and its notes now say "The activity log in Settings › Advanced says why", where they said Verify chain. The same sentence replaced a stale pointer to "the Activity screen" in Agents' own history note.

## Evidence

Local, Windows 10 Pro 19045, on `main` above `8192987` with the step's changes uncommitted.

**The journey.** [OneHomeJourneyTests](../../tests/Keypaste.App.Tests/Session/OneHomeJourneyTests.cs) launches the app as `App.Launch` composes it, unlocks through the unlock screen, and presses controls where the window draws them.

- **Settings:** the automation tree names none of New token, Token name, Token scope, Create token, Copy token, Verify chain, Copy latest hash, `KEYPASTE_HOME` or keypaste home, and names Open the scoped tokens and Open diagnostics.
- **Scoped tokens:**
  - New token opens the form. The name and scope are typed into it.
  - Create token mints the token, and `TokenStore` then lists it.
  - Copy starts the clipboard countdown under the token's name.
  - Done, then Revoke twice, removes it from the vault.
- **The activity log:** Verify chain shows the verdict, and Copy latest hash puts it on the clipboard.
- **Diagnostics:** the tree names the vault's path, the resolved home and the version.
- **Agents:**
  - With a bridge attached under the label `one-home`, the tree names none of the moved controls and no dropdown is visible.
  - The card's ⋯ opens its menu, and Ask every time writes `ask` for `one-home` to `clients.toml`, read back through `ClientPolicies.TryLoad`. The menu is then closed.
- **History:** the tree names neither Verify chain nor Copy latest hash.
- **Amber:** every frame, eleven of them, passes `AmberElements.AssertOne`:
  - Settings, the activity log, Diagnostics and History have none;
  - Scoped tokens has New token, then Create token, then Copy, then New token again;
  - Agents has Connect client, with and without a card's menu open.

**Around it.**
- `LogViewModelTests.History_marks_an_unverified_record_but_leaves_the_check_to_the_activity_log` holds that on an edited log:
  - History offers neither Verify nor Copy hash, as a view or as a command;
  - it still marks the altered row;
  - it says the activity log says why and where;
  - the activity log still offers both.
- `ShellViewModelTests.Settings_advanced_opens_the_scoped_tokens_and_diagnostics` holds each screen's Back to Settings.
- `AgentClientsTests` now changes a policy through the menu's choices, and holds the three choices in order with the held one checked.
- `AccessibleDriveTests` checks that no control on Scoped tokens or Diagnostics is announced by its type.
- The filtered run of these classes, `FourPlacesJourneyTests`, `AgentActivityViewModelTests`, `AgentActivityViewTests` and `ScopedTokensViewModelTests` passed 62 of 62.

**The renders.** `ScreenRenderer` draws Scoped tokens (`22-settings-tokens`, its form, the minted token and a revoke), Diagnostics in both palettes (`95b-settings-diagnostics`) and a card's open menu (`27-agents-client-menu`), with no frame over one amber element. The first render of the menu showed the card's own policy text drawn through it. The menu's canvas now sits above the card's later content, and the second render was clean. README's `agents.png` is the new `20-agents`.

**The command.** `bash scripts/verify.sh` selected workflows, scripts and desktop and passed in 367 seconds. The desktop suite ran 841 tests: 838 passed and 3 were skipped, the renderer's draws, which need an output folder. Consistency passed 43 of 43. Backend and integration were skipped, since no changed path maps to them.

## Decisions

[D-0380](../../DECISIONS.md) records the new homes. These bind only this step's code:

- **The card's menu is drawn in the window's own tree,** on a canvas lifted above the card's content, with the open card's container lifted above the cards after it. A flyout would be a separate top level, which neither the amber check nor the driver sees.
- **The menu styles are shared.** The ⋯ toggle, the menu and its items moved from the Items screen's local styles into the theme, so both screens draw one menu.
- **The token screen's actions are plainly primary.** On Agents they had yielded to a waiting request. Scoped tokens shows no request, and its three primaries are never shown together.
- **Diagnostics holds its own view model.** It reads the session's vault path, the home and the version, which Settings' view model no longer carries.

## Limits and follow-ups

- **Headless rendering only.** The menu, its focus and its closing are checked in frames drawn headless and with synthetic input.
- **Settings' Advanced card is still a card.** Its rows open screens; there is no Advanced screen of its own.
- **The prototypes in `docs/design/design/` still draw tokens on Agents and a select on each card.** The handoff's README now describes the moved screens.
