# F.110 — The app follows Apple's eight principles in a list-and-detail layout

Completed 2026-11-10 in `822be6eb`, source only; runs ci 42896894062, app 42896912581.

## Amendments

The founder's direction of 2026-11-10 set the scope when the task was selected:

- DESIGN-PRINCIPLES follows Apple's eight design principles, with PRODUCT re-ratified as v1.25.
- The current system was simplified rather than rebuilt: amber, the marks and the type stay.
- Every page was redrawn from the before-and-after sketches the founder approved, except the generator and New item, which the founder found too simple.
- Pressing a value copies it.
- The default idle lock is 30 minutes.
- The generator shows length, character choices, the word separator and its save destinations.
- New item adds fields from a button on the left of the form.
- The unlock screen keeps the key file in view.
- Local accounts will unlock together.
- The founder chose to drop every table with column heads and grips, since the approved Activity and Trash pages have none.

F.111's generator rework is included and closes with this record. A new icon set, also ordered that day, is separate work.

## Evidence

The work was built by area on branches, and each area had an independent review whose findings were fixed: the foundation, the generator and New item, the project and Recommendations, Agents and Activity, and Settings, Trash, unlock and first run. Each area's desktop profile ran on GitHub's runners through `dev.sh --profile`. The desktop gates passed on all three runners in dev run 42884775159.

The merged branch's first full round, app 42893011801, failed `Every_screen_keeps_every_control_within_reach` on every runner. Fluent's `Slider:horizontal` template outranked the app's `Slider` style, so the length slider kept its validation adorner, which cut its track by 1px. Raising the selector's priority fixed it, and the second round, ci 42896894062 and app 42896912581, was green on every runner with no reruns.

## Limits and follow-ups

- Nine head and grip tests left with the controls they tested.
- TOTP is not a New item field option, the title is not edited in place, Trash's sort menu has no test of its own, and resizable columns are gone. Each of these is in BACKLOG.
- Nobody has used the new layout on a real machine yet.
- README and keypaste.com still show the old screenshots. The README rewrite the founder asked for follows as its own work.
