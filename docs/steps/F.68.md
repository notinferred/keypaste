# F.68 — Close R.1a's KeePassXC crash and screen-reader limits

Completed 2026-10-23 at `79b2aa82` on `f68-screen-readers-keepassxc`, source only: ci 40033374237 and app 40033384869 passed every job on all three runners on their first attempt, and dev run ACTS_RUN performed R.1a's acts on that app run's packages. The integrated commit differs from that tree only in this record, FEATURES and R.1a's limits.

It had no STEPS row. On 2026-10-23 the founder asked to complete three of R.1a's limits: KeePassXC 2.7.12 crashing on the runners, a Linux screen reader never getting a list's new rows, and the Connect plan's unused label.

## Amendments

- **KeePassXC.** The founder chose to probe before choosing a version, then to pin the version that did not crash.
- **Screen readers, first pass.** The founder first chose a TextBlock subclass that honours `AutomationProperties.Name` and a ListBox subclass, used for all 13 lists, that says when its rows change. Reviewing them as band-aids on a design that fights Avalonia, the founder then chose the design D-0527 records: the idle warning's announcement in a text of its own, no TextBlock named apart from what it shows, and the list fix left to Avalonia.
- **Added by the founder.** KeePassXC 2.8's Linux location for the first run (D-0528), and, when the projects gate failed on 2.8.0, refusing the values 2.7.11 and 2.8 fill in differently (D-0529).
- **Upstream.** The founder chose to read the two Avalonia issue drafts and the pull request first; nothing was posted.

## Diagnosis

- **KeePassXC crash.** The macOS crash reports of rehearsal 39967782112 show a segmentation fault in Qt 5's `QWidgetPrivate::showChildren`, two levels under KeePassXC's `main`, as it first showed its window about two seconds after starting. That is the stack KeePassXC #13521 and #9799 report for 2.7 on Qt 5 when KeePassXC is started as a background job, as the gates and acts start it; KeePassXC closed #13521 as not planned and investigates only its Qt 6 line, released as 2.8.0 on 2026-10-21. Probe dev run 40029123176 opened one vault 30 times per version, started as `keepassxc_recent` starts it:

  | | `macos-15` | `windows-2025` |
  |---|---|---|
  | 2.7.12 | 20 ended by themselves, each of the 19 crash reports in `showChildren` | 14 ended by themselves, status 139 |
  | 2.8.0 | none | none |

  Homebrew's cask, which macOS jobs installed unpinned, moved to 2.8.0 the same day.
- **Linux list rows.** Avalonia's `AtSpiNode` presents a selection container's realized rows as its own children (`GetChildPeers`, `CollectSelectionItemPeers`) and lists them again only when its own peer raises `ChildrenChanged`. A ListBox's peer raises it only when the ListBox's template children change; the rows are the items panel's children, and the panel has no node to hear its peer. The same code is in 12.1.4 and main at `c1e4fab24c38`.
- **Text names.** Avalonia's `TextBlockAutomationPeer.GetNameCore` returns the text and never reads `AutomationProperties.Name`, and raises a change of name with every change of text, for which Windows' bridge raises a live-region announcement (`AutomationNode.cs`). So the Connect plan, the install link and the session's records were read by their text, the sidebar heading as `PROJECTS`, and the idle warning, named by its ticking text, a new name each second, though CHANGELOG said it was announced once. `AgentsFirstLevelTests` passed throughout because it read the attached property rather than the peer.
- **Placeholders on 2.8.0.** Under 2.8.0 the projects gate's `{\{S:ESCAPED_WHOLE\}}` came back as written, where 2.7.11 gives `{{S:ESCAPED_WHOLE}}`. KeePassXC 2.8.0's #13688 makes `placeholderType` strip the braces of a text it does not know and classify the inside again: an inside that is no placeholder sends `resolveMultiplePlaceholdersRecursive` back over the same braces until its depth limit, which leaves them as written, and an inside that is one placeholder, as in `{{PASSWORD}}`, fills in without the braces.

## What changed

- **KeePassXC.** [install-keepassxc.sh](../../scripts/install-keepassxc.sh), which replaces `install-keepassxc-windows.sh`, installs 2.8.0 on Windows from its x64 archive and on macOS from its arm64 disk image into `/Applications`, each kept only at its SHA-256, and prints its `keepassxc-cli`; ci, app, install-desktop, release and upgrade-desktop call it in place of Homebrew and the Windows script. `verify-release-matrix.sh` reads it as a script that builds no package. When KeePassXC ends by itself during the acts, the step is now unreached with what the platform recorded, where the open-once step went on and the macOS Argon2d change tried three sessions.
- **The idle warning.** A live TextBlock one pixel square and transparent holds `CountdownAnnouncement`, set once per warning; the countdown that ticks is no longer live. The Connect plan, install link, session records and sidebar heading lost their unused names.
- **Agents' naming test** reads each control's name from its automation peer, counting a type's name as none.
- **The first run on Linux** reads `$XDG_STATE_HOME/keepassxc/keepassxc.ini`, or `~/.local/state`'s, before 2.7's cache file.
- **Placeholders.** `KeePassPlaceholders` finds the braces KeePassXC's pattern matches, in one pass over the value, and a placeholder or escape inside braces that hold another brace, name no placeholder and do not start `{S:` refuses the value wherever it is filled in or planted: "holds the KeePass placeholder {PASSWORD}, which KeePassXC 2.7 and 2.8 fill in differently inside braces that name no placeholder". The projects gate keeps the three escapes both versions give alike, holds the refusal of `{\{PASSWORD\}}` and `x{{PASSWORD}}y` through `keypaste run`, and checks that keepassxc-cli from 2.8.0 shows `{\{PASSWORD\}}` as written and from 2.7.11 to 2.7.x with its escape made braces.

## Evidence

At `79b2aa82`, ci 40033374237 and app 40033384869:

- `compat` on `windows-2025` and `macos-15` ran every KeePassXC gate against keepassxc-cli 2.8.0, and on `ubuntu-24.04` against Ubuntu's 2.7.6. On all three the projects gate refused `nested-escape` and `nested-placeholder` with nothing started; 2.8.0 gave the three remaining escapes as keypaste does, and both 2.8.0 and 2.7.6 showed `{\{PASSWORD\}}` as written. App's KeePassXC first-run gate passed with 2.8.0 on Windows and macOS, and its workflows gate with 2.8.0 on Windows.
- `ShellAnnouncementsTests.The_idle_warning_is_announced_once_while_its_seconds_tick` holds one change of the live text's name, to "Locking in 30 seconds.", while the drawn countdown reaches 27 seconds.
- `AgentActivityViewTests` holds that the Connect plan's peer is named by the plan, and `AgentsFirstLevelTests` that every control on Agents is named, as its peer gives it.
- `KeePassPlaceholdersTests` holds seven refused values, three that refuse nothing, and the escapes that still fill in; `KeePassXcDatabasesTests` holds both Linux places in order.

ACTS_EVIDENCE

Before the final design, ci 40030867082 and app 40030877968 at `7422fbbb` ran the first pass: they found the projects gate's 2.8.0 difference, the release matrix reading the installer as a DMG build and `AgentsFirstLevelTests` failing once the unused names were removed. None of these runs is evidence for the integrated design.

## Decisions

D-0526 pins KeePassXC 2.8.0 on Windows and macOS; D-0527 records the idle warning's live text and the names text is given; D-0528 the first run's Linux location for 2.8; D-0529 the refusal. D-0208, D-0377 and D-0519 were rewritten to point at them.

## Limits and follow-ups

- **Linux list rows** still do not reach a Linux screen reader, which R.1a's limit keeps. A change to Avalonia's `AtSpiNode`, watching the peers between a selection container and its rows, was drafted for a pull request to Avalonia with its description; it is unbuilt, needs a real accessibility bus to check, and was not opened.
- **2.8.0 not crashing** in 60 starts is evidence, not KeePassXC's diagnosis, which it never made.
- **2.8 on Linux.** No runner has KeePassXC 2.8 on Linux, where Ubuntu 24.04 ships 2.7.6, so the first run's new location is held by unit tests only.
- **No screen reader was run.** The idle warning's single announcement rests on its peer's events and on Avalonia's bridges announcing a live element's change of name.
- **A `{S:…}` whose name holds braces,** such as `{S:a{PASSWORD}b}`, which KeePassXC 2.7 and 2.8 read as one field named `a{PASSWORD}b`, keypaste still fills inside; that divergence predates this step and is unchanged.
