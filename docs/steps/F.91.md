# F.91 — Tell Agents' state by shape as well as colour

Completed 2026-11-09 on `backlog-2026-11`, source only; CI has not run on it, so no run IDs are recorded. Implemented only, in RELEASE's terms.

## Amendments

The founder selected this from BACKLOG's investigation candidates on 2026-11-09: keep the dot for running and use a distinct shape or ring for stopped or refusing, so the state reads without colour. There is no STEPS row.

## What it does

Agents' mark has three states, set by `NavItem`: running is the filled green dot it was, stopped (`DotStopped`, neither live nor refusing, which covers locked, not serving and held by another keypaste) is a 6px muted ring with a 1.5px stroke, and refusing (F.86's audit failure, `DotDanger`) is a red diamond, the same 6px square turned 45 degrees. The shapes are `Border.dot.stopped` and `Border.dot.refusing` in `Surfaces.axaml`, and no colour was added.

Reversible local choices, settled here: the wide sidebar takes the same marks as the rail through the same bindings, though it was not colour-only for running and refusing, since it prints `live` or `refusing` beside the dot, because the marks are one system; DESIGN's rail paragraph now names the three shapes.

## Evidence

`Agents_draws_a_different_shape_for_each_state_so_it_reads_without_colour` draws each state and compares silhouettes, read from the frame's pixels with colour gone, on the rail in dark, light and both high-contrast palettes and in the wide sidebar in dark and light. Against the code before the fix all six cases failed, with running and stopped differing by 0 pixels of shape.

Locally on macOS, `verify.sh test` over `IconRailTests` passes 33 of 33. `ScreenRenderer`, with its reach walk and one-amber checks at each zoom, `AssistiveTechnologyTests`, `ShellViewModelTests` and `SessionOwnershipTests`, which holds F.86's refusing state, pass 82 of 82 in a run at the same product code whose only failures were the new test's six cases under a first, too strict measure. `verify.sh compile` passes.

`ScreenRenderer` adds `50d-rail-agents-running`, `-refusing` and `-stopped`, drawn at 720 by 520. Looked at in dark and light at 100%, enlarged and at real size, and the stopped ring in the wide sidebar in dark: the three read apart without colour and clear the robot icon. The same three in both high-contrast palettes read apart as well.

## Limits and follow-ups

No person has looked at this on a real display, and no colour-blind check was made beyond the shapes differing. At 100% the ring's hole is about 3 pixels wide and its centre keeps a little antialiased ink, so at that scale it reads as a small ring with a lighter centre; the test measures it at half of each mark's strongest ink so that high contrast counts the same. The rail's accessible name stays `Agents` while stopped, with `not serving agents` in its tooltip and help text, as before; the other `Border.dot` status marks, such as Agents' live sessions and the log's verdict, keep their shape.
