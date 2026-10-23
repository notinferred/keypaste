# G.9 — The owner writes the audit line of every request

Completed 2026-10-13 at `2b2219f` on `task/g9`: the dispatched ci 38390577935 and app 38390590990 runs passed every job on all three runners; ci's `tools` jobs, which G.9 does not touch, were not asked for (D-0438). The integrated commit differs from that tree only in this record.

## Amendments

The founder settled the row on 2026-10-10 while planning the 0.5.0 rows and answered the design's questions on 2026-10-12. D-0439 and D-0440 record what it decided.

- **One line per request, written by the owner** (founder, 2026-10-10). The app or `keypaste agent` writes one audit line for every request it answers, listings, credentials and runs, refusals at admission included, before it replies, and refuses a request it cannot record (T-6). The bridge writes a line only when no owner answered, because the app was closed or locked, or when it refused something itself. There is one line per request and no field naming the writer. G.9 exists so that G.8's Automatic releases have a record the agent cannot redirect; T-14 is amended.
- **The second witness is dropped** (founder, 2026-10-10). The STEPS row said "The bridge's line stays as a second witness"; that line is dropped as a founder amendment.
- **Before G.8** (founder, 2026-10-10). G.9 needs nothing and integrates just before G.8, so Automatic never lands without the owner's line. The plan review asked the design to decide what the bridge records when `RunTool` refuses after the owner recorded a release, and on the undeliverable path, so each outcome still has one line; the first design answer settles it.
- **Designed up front** (founder, 2026-10-11). G.9 was designed and critic-checked alongside G.8, N.3, N.6 and E.1d while G.5 and G.2 finished, its questions went to the founder in one round, and it then built at once.
- **A release the bridge drops after the owner recorded it** (design answer 1, 2026-10-12). It keeps only the owner's "allowed" line: the client withdrew after the yes, the names did not match or the command could not start. That keeps one line per request, and T-6 accepts the over-report.
- **A log that cannot be written** (design answer 2, 2026-10-12). `keypaste agent` refuses to start before asking for the master password, printing the log's error. The app keeps running but refuses each agent request, and the agent is told the request could not be recorded.
- **keypaste.com** (design answer 3, 2026-10-12). G.9's push corrects the how-it-works and products/agent-approvals pages, which said the bridge writes the audit record, and `scripts/verify-site-disclosure.sh` runs after the deploy. The founder added later that day that the same push rewords `site/public/index.html`'s "The bridge excludes the returned field value" to "keypaste leaves the returned field value out of the log".
- **Wording** (design answer 4, 2026-10-12). G.9's new lines ship as the design wrote them, and N.6 rewords them: the `--audit-log` line of `keypaste mcp --help`, `keypaste agent`'s `keypaste: <error>` and tightened-permissions lines, the narration `refused <entry> for <client>: the audit log could not be written, so nothing was sent`, and the log reasons. `ToolText.RunMismatched` loses "This call was recorded in the audit log as denied.", which is false after a recorded reply.
- **The owner checks its log before it asks** (founder, 2026-10-12). The adversarial review found that a bridge dying while its request was decided, at a time the owner could not write its log, left no line anywhere. The founder chose to have the owner check that its log opens before it asks anyone about a request and refuse the request at once if it does not, so a line is lost only when the log fails during the decision, as T-6 states. This also stops a person from being asked about a release that could only be refused.
- **G.8 builds on this branch** (founder, 2026-10-12). G.8's later parts merge `task/g9` until G.9 is on main, and the full ci and app runs follow each row's review fixes, with the adversarial review running alongside the final run. The landing order did not change.

## What changed

- `Core/Audit/RequestRecords.cs` builds the line for each kind of request: listing, credential, agent run, `run --session` and token run. The owner and the bridge both build their lines there, so the two cannot drift. `AuditClient.Of` sanitises a client's name as the bridge did, and `AuditTools` holds the tool names.
- `Core/Ownership/RequestRecorder.cs` appends the owner's line before the reply leaves. When the append fails, a release becomes a refusal with nothing in it, and a listing that names anything counts as a release. A refusal is answered either way, and its reply says whether its line was written.
- `SessionAuthority` takes the audit log's opener. It records every listing, credential, run, `run --session` and token request it decodes:
  - It checks that the log opens before anyone is asked. When it does not, the request is refused unasked as "the audit log could not be opened".
  - A refusal at admission is recorded without a session. An exception while deciding is recorded as `failed`, and a request its peer withdrew or hung up on as `cancelled`.
  - Before a run's release is recorded, the owner checks that the resolved names are the ones the request named (`RunRequestRules.Answers`). A mismatch is recorded and answered as `failed`.
  - A reply is checked to fit one frame as it will be sent, with its session and `recorded`. That way a release the owner recorded is never replaced by an undeliverable refusal. A listing is fitted before its names are counted.
  - A refused run whose entries cannot fit one line names no entries, so the refusal is still recorded.
  - Only a recorded release enters the release ledger.
- The wire: `NamesReply`, `CredentialReply` and `RunReply` carry `recorded`. A missing member reads as not recorded, which fails closed and is not compatibility with earlier owners (D-0416). A frame that names it twice is refused. The encoder's stand-ins keep the session and `recorded`.
- A listing, credential or run request that the bridge's client gives up on after it was written is withdrawn with a new `Withdraw` frame on the same connection, replacing the design's `ApproverOutcome.Withdrawn`. The owner still answers it and records it as `cancelled`, and the bridge waits up to five seconds for that answer. The connection is spent afterwards. A `run --session` or token runner still hangs up, since it writes no line.
- The bridge writes a line only for a call no owner recorded:
  - one it refused before asking;
  - one it could not send;
  - one that drew no reply;
  - one whose withdrawal went unanswered for five seconds;
  - one whose reply says it was not recorded. The bridge refuses such a reply with `ToolText.AuditUnavailable`, whatever it carried.

  After a recorded reply the bridge writes nothing. `RunTool` keeps its own name check as a second guard. `--audit-log` now places only the bridge's lines.
- `keypaste agent` opens its home's audit log before its claim and the master password, and refuses to start with `keypaste: <error>` and exit 2 when it cannot. The app opens its log at the first agent request, refuses every request while the log cannot be opened, and tries again on the next request, so mending the file needs no new unlock.
- The Agents screen's client cards no longer count the owner's `run --session` lines, which are unlabelled and named `keypaste run --session`, as an MCP client. A labelled bridge of that name still gets a card.
- Gates read the log where its writer puts it:
  - `verify-session-authority.sh` now asserts the split. While the app is unlocked, the app's log gains the listing and the request and the bridge's log gains nothing. While it is locked, the bridge records both refusals and the app records nothing.
  - In `verify-desktop-approval.sh`, a cancelled call and the call of a bridge that went away each leave exactly one `cancelled` line, which the app records.
  - `verify-agent-activity.sh`, `verify-policy-e2e.sh`, `verify-held-saves.sh`, `verify-approval-e2e.sh`, `verify-mcp-run.sh`, `verify-demo.sh`, `verify-lock-boundary.sh`, `verify-current-state.sh` and `verify-session-lifecycle.sh` read the owner's home log.
  - `verify-keepassxc-fields.sh` and `verify-keepassxc-projects.sh` read each phase's lines through `lines_in` and `lines_after` in `scripts/lib/common.sh`.
  - `exercise-desktop-install.sh` drops `--audit-log`.
- DECISIONS gains D-0439 and D-0440 and amends D-0358 and D-0425. THREATS rewrites "What the bridge does", T-6, T-14, T-30, T-32 and T-35, and updates the evidence of T-3, T-12 and T-16. ARCHITECTURE, `docs/approvals.md`, `docs/mcp-setup.md`, `launch.md`'s prose, FEATURES' unlock-lifecycle and run rows, and the three keypaste.com pages say who writes each line.

## Evidence

- New test classes:
  - `SessionAuthorityRecordsTests` runs over a real pipe with a real log. It checks:
    - a credential grant is recorded before its reply is read, without its value;
    - a listing is recorded under the client its connection attached as;
    - a person's refusal and a policy release are each recorded as theirs;
    - an admission refusal is recorded with no session, and an invalid request as invalid;
    - a run is recorded with its command and entries before its values leave;
    - a release the owner cannot record is refused with nothing in it;
    - an owner whose log will not open refuses a credential, a listing, a run and a `run --session` with nobody asked;
    - a failure while deciding is recorded as `failed`;
    - a peer that hangs up while asked is recorded once as `cancelled`;
    - a request straight to the pipe is recorded.
  - `RequestRecordsTests` pins each builder's fields and checks that no line can carry a released value. It absorbs `AuditRunLineTests`; its two incremental-read tests moved to `AuditLogTests`.
  - `OneLinePerRequestTests` runs the shipped bridge against a real owner, each with its own log. An answered call is one line in the owner's log, wherever the bridge logs. A call nothing answered is one line in the bridge's log. A client that gives up while asked is one `cancelled` line, the owner's, or one line in the bridge's log when the owner's log fails meanwhile. A call made while the owner's log will not open is refused unasked, in one bridge line.
- Changed test classes:
  - `ApproverProtocolTests`: `recorded` round-trips on the three replies, an absent member reads as false, a duplicate is refused, and the fit is measured on the reply as sent.
  - `ApproverListenerTests`: a withdrawn request is still answered, and then its connection ends.
  - `ApproverConnectionTests`, `ServerToolsTests`, `RunToolTests`, `OwnerDenialTests`, `LateHandshakeTests`, `SecretHygieneTests`, `LargeCredentialTests`, `LargeVaultListingTests`, `ConcurrentRequestsTests` and `SessionAttachmentTests` hold the bridge's rule. `ServerToolsTests.EveryCall_WritesOneAuditLine_NamingTheClientAndTheExposure` became `EveryCallNothingAnswered_…`. `LateHandshakeTests` proves F.61 through what reaches the owner, as T-3 keeps it.
  - `SessionAuthorityEnvTests`, `SessionAuthorityTokenTests`, `SessionAuthorityRunTests`, `SessionServerTests`, `McpClientCardsTests`, `RunSessionTests` and `RunTokenTests` cover recorded refusals, the owner's name check and the runner's cards.
  - In the CLI, `AgentAuditLogTests` covers `keypaste agent` stopping before the password.
  - In the app, `SessionOwnershipTests` checks that a log that could not be opened is tried again on the next request. `DesktopApprovalTests` covers a bridge that withdraws its request taking the prompt down, and `FourPlacesJourneyTests` shows History the line the app wrote rather than a simulated bridge line.
- Regressions, run on control branches carrying the new tests over the old code (since deleted), failed there on `ubuntu-24.04`:

| Test | Control (old code) | Fix |
|---|---|---|
| `SessionAuthorityRecordsTests.ARequestStraightToThePipeIsRecorded` | `9406312` over main at `6336e6e`, dev 38282025852: the owner's log held no line where `list_entry_names` and `request_credential` were expected | ci 38282163952 and every full ci since: passed on all three |
| `McpClientCardsTests.ASessionRun_IsNoMcpClient_ButALabelledBridgeOfThatName_Is` | `50b104e` over `186d602`, dev 38329595914: a `run --session` line made a client card | ci 38328568513: passed on all three |
| `OneLinePerRequestTests.AClientThatGivesUpWhileAsked_IsOneLineInTheBridgesLog_WhenTheOwnerCannotRecordIt` (now `…_WhenTheOwnersLogFailsMeanwhile`) | `50b104e`, dev 38329631961: the bridge's log held no line | ci 38328568513: passed on all three |
| `SessionAuthorityRunTests.EntriesTooLongForOneAuditLine_AreRefusedUnasked_AndTheRefusalIsRecorded` | `50b104e`, dev 38329636684: the refusal still carried the entries its line could not hold | ci 38328568513: passed on all three |
| `SessionAuthorityRecordsTests.AnOwnerWhoseLogWillNotOpen_RefusesEveryRequest_BeforeAnybodyIsAsked` | `ef0f17a` over `5ad3460`, dev 38388903812: the person was asked three times | ci 38389258609 and 38390577935: passed on all three |
| `OneLinePerRequestTests.ACallWhileTheOwnersLogWillNotOpen_IsRefusedUnasked_InOneBridgeLine` | `ef0f17a`, dev 38389092639: the person was asked | ci 38389258609 and 38390577935: passed on all three |

- The owner's name check cannot be reached through the real resolver, which never returns other names. It is tested on `SessionAuthority.Deliverable` and `RunRequestRules.Answers` directly, and `RunToolTests` checks that the bridge's second check refuses a mismatch and writes no line.
- Runs on `task/g9`:

| Run | Commit | What | Result |
|---|---|---|---|
| dev 38262715554, 38263024398, 38263300184, 38263608850, 38264048671, 38264308595, 38264611145, 38264921136 | `1834518` | Part 1: `RequestRecordsTests`, `ServerToolsTests`, `RunToolTests`, `AuditLogTests`, `SessionAuthorityEnvTests`, `SessionAuthorityTokenTests`, `McpClientCardsTests`, `LateHandshakeTests` | Passed on all three |
| dev 38266414739, 38266869984, 38267725772 | `4e5ae6b` | Part 3: `ApproverRunProtocolTests`, `ListingSizeTests`, `ApproverProtocolTests` (38266388817 cancelled by the next dispatch) | Passed on all three |
| dev 38269743569, 38270237525, 38270603275, 38270875295, 38271188514, 38271505573 | `c807ea7` | Part 4: `SessionAuthorityEnvTests`, `SessionAuthorityTokenTests`, `SessionServerTests`, `RunSessionTests`, `RunTokenTests`, `OwnerDenialTests` | Passed on all three |
| ci 38272929276, app 38272942596 | `4a2cfdc` | Full, part 2 | app passed; ci failed integration on `windows-2025`, where `verify-approval-e2e.sh` passed but its cleanup could not remove the agent's home, which now lies in the gate's directory, while the agent still held its audit log and session lock (`Device or resource busy`). `verify-approval-e2e.sh` and `verify-mcp-run.sh` now wait for the agent before removing it (`d25ca60`) |
| ci 38274258195, app 38274271888 | `d25ca60` | Full, part 2 | Passed |
| install-desktop 38275577339 | `d25ca60` | Rehearsal on the branch's packages for `exercise-desktop-install.sh` | Passed for `win-x64` and `linux-x64` |
| ci 38281935636, app 38281947987 | `55f5ab6` | Full, part 5 | Cancelled by the next dispatch |
| ci 38282163952, app 38282176761 | `d67a627` | Full, part 5 | Passed |
| ci 38325149065, app 38325158139 | `186d602` | Full, part 6 | Passed |
| ci 38328568513, app 38328576952 | `5ad3460` | Full, after the review's fixes | Passed |
| ci 38389258609, app 38389272033 | `2b2219f` | Full, with the log check | Passed |
| ci 38390577935, app 38390590990 | `2b2219f` | Full, final | Passed every job on all three runners |

## Decisions

D-0439, D-0440; D-0358 and D-0425 amended, D-0020's line from the bridge for every call superseded; THREATS "What the bridge does", T-6, T-14, T-30, T-32 and T-35 rewritten, T-3, T-12 and T-16 evidence updated.

## Limits and follow-ups

- A release the bridge drops after a recorded reply stays recorded as allowed, as does a release lost when the owner crashes after its append (T-6).
- A reply lost after the owner recorded it also leaves the bridge's own line. That includes a withdrawn call whose answer took longer than five seconds. For a listing, the bridge's one retry instead leaves a second owner line; a credential call is no longer retried, so it never does (D-0517) (`4f19fbdc`; ci 39850506100, app 39850506098). These are the only cases with two lines for one call.
- A call has no line only when the owner's log fails during the decision and the bridge then dies or cannot write its own log; nothing is released then.
- A line names the client the requester claims, which is unauthenticated (T-3). A reply's `recorded` is the answering process's word; a process answering in the owner's place holds no vault value (T-10, T-14).
- **For 1.0.0.** A listing the bridge records itself is filed as `no-approver` whatever ended it: `ApproverEntryNameSource.ListAsync` returns `VaultAvailability.Failed` alike for a withdrawn listing, a failed exchange with an owner listening and an unreachable owner, `ListEntryNamesTool` turns a throwing source into the same, and its `Method` maps every `Failed` to `NoApprover`. Carry the cause on `EntryNameListing`, as new availability values or an explicit audit method, and record a withdrawn listing as `cancelled`, a failed exchange or a throwing source as `failed`, and only an unreachable owner as `no-approver`, as `request_credential` does. `ServerToolsTests` must show a listing whose exchange fails with an approver present recorded as `failed`, and one with nothing listening still recorded as `no-approver`.
- While the app's log would not open, the person learned of it only from the agents' refusals. Fixed: once a request finds the log will not open, Agents' status says every agent request is refused and why, and the tooltip on the sidebar's Agents row says so too, both read from that refusal, so an unlock alone still opens no log (D-0494) (`1b16a9ca`; ci 39785584625, app 39825609666 at `d57aa9b7`).
- The control runs ran on `ubuntu-24.04` only.
- The keypaste.com pages change with G.9's push to main. `scripts/verify-site-disclosure.sh` runs after that deploy, so this record does not cover the live site.
- G.9's new lines ship as written; N.6 rewords them.
- An observation with no fix proposed. Main's app run 38260912148 at `d66ef5b` ([F.62](F.62.md)'s push) failed. Its `publish + selftest (windows-2025)` job 114850921917 hit the 20-minute job limit and was cancelled while building the installer, and `app ok` failed. Earlier in that job, `dotnet restore keypaste.app.slnx --locked-mode` had taken 10.3 minutes for `Keypaste.App` and `Keypaste.App.Tests`, where Core restored in 17 s. Main's next app run, 38263044847 at `294e376`, restored in about a minute and passed. That points to a slow package download on the runner. The passing run does not close it.
