# V.9 — Report local password health

Completed 2026-10-21 on `main` at `c208ad6`, source only: ci 39633992142 and app 39633992293, every job green, and ci 39634014306 ran the `tools` jobs.

## Amendments

The row asked for weak, reused and expired credentials found locally, with no network request, and reported in the same Recommendations list. The task as assigned on 2026-10-21 set its shape: weak by KeePassLib's `QualityEstimation` below a threshold to be recommended, reused as the same password on two or more items compared in memory and never written, expired as KeePass's expiry flag with a time in the past, password fields of ordinary items only, C.2's way of listing, dismissing and keeping dismissals, the CLI extended only if it already had a recommendations surface, and a rule test for the network if the code had a pattern for one. Each choice that shape left open was taken as recommended, under the decision rule of 2026-10-21 the task carried, and is founder-delegated:

- **Weak is below 64 bits** of `QualityEstimation`'s estimate. Every password and passphrase keypaste generates at its defaults clears it, which a test holds, while a short word with a number, such as `hunter2`, does not. The assigned shape suggested about 64 bits.
- **Reuse is compared by HMAC-SHA256**, under a key drawn for each read and zeroed after it, over a UTF-8 copy of each password that is zeroed at once. Two equal passwords share a digest within one read, and no digest from one read matches another, so nothing kept after the check can confirm a guess. A row names the other items: every one up to three, or two and how many more.
- **Expired is at or before now**, the rule D-0337 refuses a release by, read from the session's clock.
- **Only the Password field of a live ordinary item** is read: no custom field, note or history revision, and nothing in the recycle bin or `.keypaste`, which C.2 and V.11 leave out too. An item's expiry counts whatever its password, since the item is what expires.
- **A password holding a placeholder**, such as `{REF:…}` or `{USERNAME}`, is neither rated nor compared, because its text is not the password it fills in to; two items naming one reference are not a reuse. An empty password is skipped.
- **An item KeePass or KeePassXC leaves out of quality checks**, whose KDBX 4.1 `QualityCheck` is off or whose custom data `KnownBad` is `true`, is not reported, but its password still counts when another item reuses it.
- **Dismissal keys** (D-0479). A weak or reused password is dismissed by its problem alone and stays dismissed when the password changes, since the common reason to dismiss one is an item that must keep it, such as a PIN or one account under two items, and re-listing it would ask again about exactly those. When a password changed can only be read from history, which KeePass prunes, so a dismissal keyed to it would also come back unasked. An expiry is dismissed with its date, so a later expiry that passes is listed again.
- **No CLI surface.** The CLI has none for recommendations (C.2), so none was added.
- **A rule test for the network.** No test held network-free code, but `DoctorSourceRulesTests` and `InfrastructureBoundaryTests` hold a folder away from named APIs, so `RecommendationsSourceRulesTests` does the same for the checks, `KeePassInterop.cs` and the app's list.

## What changed

`PasswordHealthCheck.Scan` in `Recommendations/PasswordHealth.cs` reports a `PasswordFinding` by item and problem; `KeePassInterop.ReadPasswordHealth` rates and hashes each password under the vault's lock, the only file that touches KeePassLib. The app's `RecommendationsViewModel` lists `PasswordRow`s after the dropped tags, in the Settings count, with Dismiss and Review again, and a caption saying a weak or reused password is replaced from Items and an expiry date is changed in KeePassXC or the app that set it. The card's opening and empty texts name passwords too.

The app test fixtures' filler passwords were made strong and used once, so the C.2 and V.11 tests still count only their own findings: `TempVault`'s one item is now `ExamplePassword`, and `RecommendationsTests`, `LostTagRecommendationsTests` and `DrawnRecommendationsTests` replaced `p`. `Keypaste.AppDriver`'s `notes-review` and `tags-review` now print the count of their own findings rather than the Settings total, which password health would raise in the KeePassXC gates' vaults, whose seeded passwords include short ones; their gates' expected counts are unchanged.

## Evidence

Nothing was built or run on this machine; every test runs on GitHub.

- **Core.** [PasswordHealthCheckTests](../../tests/Keypaste.Core.Tests/PasswordHealthCheckTests.cs), 8, each on a vault saved and reopened:
  - a weak, a three-way reused and an expired item are found by item in tree order, each reuse naming the other two, with keys `weak`, `reused` and `expired@<expiry>`; an expiry 30 days off, an empty password and a strong unique one are not;
  - an expiry at the moment of the check has passed and one a second later has not;
  - the recycle bin and `.keypaste` are neither reported nor compared;
  - a `{REF:…}` password on two items and `{username}` are skipped, and `{ab}`, braces naming no placeholder, is rated weak;
  - `QualityCheck` off and `KnownBad`, written through KeePassLib as another program would, leave an item unreported while it still counts as reused;
  - five generated passwords and five passphrases at their defaults are never weak;
  - one read's digests match each other and not the next read's, and the read's text is its title;
  - a reflection sweep finds no password in any public member or text of a finding.
- **Rule.** [RecommendationsSourceRulesTests](../../tests/Keypaste.Core.Tests/RecommendationsSourceRulesTests.cs), 2: no network API is named in the core checks, `KeePassInterop.cs` or the app's list, and each name appended to `PasswordHealth.cs` is found.
- **App.** [PasswordHealthRecommendationsTests](../../tests/Keypaste.App.Tests/ViewModels/PasswordHealthRecommendationsTests.cs), 5, through the shell a real unlock builds:
  - four rows needing review with their words, a Settings count of 4 and no notice or toast; an item expiring after the session's clock but before the computer's is not listed, and a recycled item sharing a password is not named;
  - no row's text holds a password, and after dismissing all four the stored file holds the kind and no password or title;
  - a dismissal survives a lock and unlock, and Review again counts it again;
  - a dismissed expiry is listed again once a later one passes;
  - replacing the weak and one reused password leaves only the expiry.
- **Drawn.** [DrawnRecommendationsTests](../../tests/Keypaste.App.Tests/Rendering/DrawnRecommendationsTests.cs), 1 new: Settings draws "Weak password" and the reused item's caption, neither password is drawn in either style or exposed to automation, and the count of 3 carries no amber. The main-screen test also refuses "Weak password".
- **Not run.** No mutation was tried, no KeePassXC gate step was added, and `ScreenRenderer`'s Settings render, which now lists its demo vault's findings, among them `Services/OpenAI`'s expiry, was not looked at.

## Decisions

- D-0478: what the check reads and calls weak, reused and expired, where it reports, and that nothing leaves the machine.
- D-0479: how a password finding's dismissal is keyed.

## Limits and follow-ups

- The estimator rates each password against SecLists' 10,000 most common passwords, vendored and pinned by digest, so a password on that list or made of its entries is weak however long it is (D-0499) (`1b16a9ca`; ci 39785584625, app 39825609666 at `d57aa9b7`).
- A dismissed weak or reused password stays dismissed after it changes (D-0479).
- **For 1.0.0.** An expiry that passes while the vault is unlocked is listed at the next save or unlock: `PasswordHealthCheck.Scan` finds an item expired only when `RecommendationsViewModel.Check` runs, at build, save and reload, and nothing checks again when an expiry passes. After each check, arm a one-shot timer on the session's clock for the earliest future expiry among live items, found by `PasswordHealthCheck.Scan` and returned beside its findings, that runs `Check` again on the UI thread; the next check replaces it, and lock and `Dispose` dispose of it. `PasswordHealthRecommendationsTests` must list an item expiring two minutes after the start as expired once the `ManualClock` advances three minutes with no save.
- The check rates every password on the thread pool after each save, under the vault's lock, so another read of the vault, the window's included, waits for it to finish (`4f19fbdc`; ci 39850506100, app 39850506098); the estimator rates a password of at most 256 characters, so a longer one is never reported weak though its reuse is, and a vault of many thousands of items has not been measured.
- Custom fields are not rated or compared, because they mostly hold API keys a password-strength estimate misjudges, so an API key reused across items is not found (D-0500) (`1b16a9ca`; ci 39785584625, app 39825609666 at `d57aa9b7`).
- Which of `QualityCheck` and `KnownBad` KeePassXC 2.7 writes when an entry is excluded from its reports was not observed here; both are read, and only KeePassLib wrote them in the tests.
- A password row's Open item shows its item selected in its group in Items, found by the UUID the finding carries, so a rename since the check still finds it; note-key and dropped-tag rows have no such button yet (`1b16a9ca`; ci 39785584625, app 39825609666 at `d57aa9b7`).
- Source only.
