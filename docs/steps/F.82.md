# F.82 — Make Settings a screen you can find things on

Completed 2026-11-09 on `settings-sections` at `21d1e2db`, source only; ci fast tier 42642737116, dev desktop 42643837231 and dev desktop-gates 42646380242, all green on Linux.

## Amendments

The founder selected this on 2026-11-09 after reviewing the screen. There is no STEPS row.

## Judgment calls

- **Three sections, General, This vault and Browser**, on a segmented control, each saying what it holds, which replaces one heading that was untrue of Vault access. Browser shows only where a browser can reach the vault. Advanced stays one row under every section, since it opens its own screen.
- **The `keypaste` a terminal finds is under General**, since it belongs to this computer and no vault.
- **Vault access opens behind Change…**. Cancelling the form zeroes what was typed, and a finished change closes it and keeps its message.
- **What agents get moved to Agents**, under the list of agents, as `AgentRulesView`.
- **Recommendations and its count stay in Settings**, since where they should live is the founder's to decide.

## Evidence

The classes that draw Settings and Agents pass locally, and `ScreenRenderer` drew the three sections, the open form and Agents' rules, checked by eye. `VaultAccessTests` gained the form's cancel and finish and the sections.

## Limits and follow-ups

Backups still restores only from the unlock screen. Settings reopens on General each time it is visited.
