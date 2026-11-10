# F.101 — Keep a minted token through a merge

Completed 2026-11-10 on `backlog-23-25`, source only; ci 42822341787 and app 42822347029 at the final code `4fa4c1e9`, both green on Windows, macOS and Linux.

## Amendments

The founder selected this on 2026-11-09: an uncopied token on the Tokens screen counts as an unsaved draft, so a merge waits until that screen is done. There is no STEPS or BACKLOG row.

## Judgment calls

- **Copied means a copy reached the clipboard.** A failed copy leaves the token a draft; the clipboard clearing later does not bring it back.
- **The screen is read in place.** Once nothing holds it, the tokens are read again without rebuilding the screen, so a copied token stays until Done, and a form's held change shows when the form closes.
- **Leaving asks.** An uncopied token raises "Discard this uncopied token?" wherever a draft does, a right-click on a sidebar row included. Lock discards it unasked, as it discards every draft.

## Evidence

Run before the fix, 5 of the 7 new tests failed. `AMerge_KeepsAMintedTokenNotYetCopied` confirmed the bug: the screen was rebuilt and the token lost. The two that passed are guards on existing behaviour: `LeavingAMintedTokenOnceCopied_AsksNothing_AndForgetsIt` and `AMintedTokenHeldThroughAMerge_IsForgottenWhenTheShellCloses`. `DrawnRevealWaysOutTests` now discards at "another place" and copies before "menu".

With the fix, `ShellStatusTests`, `ScopedTokensViewModelTests`, `RevealMergeTests`, `DrawnRevealWaysOutTests` and `KeepUnsavedWorkTests` pass 204 of 204 locally on macOS.

## Limits and follow-ups

A person who reads a revealed token and types it elsewhere has not copied it, so leaving still asks. The app was not started by hand.
