# V.11 — Flag an entry that lost its project tag

Completed 2026-10-05 at `756cb89` on `task/v11`. Runs: ci 37403315697 and app 37403317950, dispatched so every job ran, and dev 37397846023 on Linux and Windows and 37400993297 on macOS, with the integration and KeePassXC gates.

## Amendments

The row was expanded at selection against the code as it was. The founder chose the rule on 2026-10-05. The rule had to settle one question: every tag removal keypaste makes itself leaves the same history as a dropped one, and a KDBX version does not say which app wrote it. The options considered were these, and the founder chose the third:

- flag every lost tag;
- remember keypaste's own removals on each machine;
- decide from the vault alone, by what else the dropping version changed, as KeePassXC treats the file as the only record.

## Evidence

- **Core.** `LostProjectTagCheckTests`, over versions another program wrote through KeePassLib (`ForeignEdit`):
  - a tag dropped while the password changed is found, with its entry, environment, protection and time;
  - one removed alone, by keypaste or by that program, is not;
  - `env:billing` and `env:billing:dev` read alike;
  - moving to another environment with a change finds the one left;
  - a tag taken back is not found, and one lost again is found with a new key;
  - the recycle bin and `.keypaste/` are not read;
  - no public member of a finding holds a value.
- **App.** `LostTagRecommendationsTests`:
  - the dropped tag is listed and counted on Settings, with no notice;
  - Restore tag shows D-0415's two sentences, writes nothing until Add tag, and then puts the tag back in one revision;
  - a dismissal survives a lock and holds no title or value;
  - a tag removed in keypaste is not listed.
- **KeePassXC.** `verify-keepassxc-projects.sh`'s V.11 steps, through `keepassxc-cli merge`:
  - KeePassXC writes the version that drops `env:billing` from Stripe while changing its password, and the version that drops `env:billing:prod` from Database alone, each keeping the earlier version;
  - the app lists Stripe's tag only and prints no value;
  - a dismissal survives across processes, and Review again lists the tag again;
  - Restore tag names `billing/dev` and `STRIPE_SECRET_KEY` first;
  - KeePassXC then reads the tag back, with one more revision and the password as it left it.

## Decisions

- D-0421: only a version that changed more than tags is flagged, and a dismissal is keyed by the tag and when it was lost.

## Limits and follow-ups

- A tag dropped in a version that changed nothing else is taken as deliberate. A deliberate removal saved together with another change, in an app that keeps tags, is flagged, and Dismiss clears it.
- A dropped tag whose history KeePass has aged out (about ten versions by default) is not found.
- Which phone apps keep tags is R.1b's to observe.
- F.51 holds a crash of the app driver seen on macOS in dev 37397846023, after the app had refused a vault `keypaste agent` held; this step changed nothing on that path, and the macOS rerun passed.
