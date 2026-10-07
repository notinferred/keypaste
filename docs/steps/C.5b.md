# C.5b — Expose tagged projects to agents

Completed 2026-10-06 at `c4c1e22` on `task/c5b`. Runs: ci 37483552108 and app 37483557657, dispatched so every job ran, and dev 37478702958 on all three runners with the integration and KeePassXC gates.

## Amendments

The row was written before D-0416, which left `env/**` reaching only ordinary untagged entries filed under a group named `env`, with every field. The founder chose on 2026-10-06:

- the default exposure is `tag:env:*` alone, not `env/**` plus `tag:env:*`;
- a selector matches by project membership, as `ProjectTag` reads a tag: `tag:env:<project>[:<environment>]`, `*` in either part, an omitted environment meaning every one, so `tag:env:billing:dev` also matches an entry tagged plain `env:billing`. Only `env:` tags are selectable.

## Evidence

- **Core.** The exposure is enforced by the owner, which holds the vault and reads tags:
  - `EntryExposureTests`: a selector matches membership, not the tag's text; reach is per entry, so an entry in two environments is reached through either; a place pattern reaches the whole entry even when a selector also matches; a tag reaches only custom fields named like variables, never a standard field; the bridge's check refuses only what no tag could reach; a malformed selector in any case is refused; on a real vault, the lister names the fields that hold something and the project tags, and a tag's reach drops the standard fields.
  - `ApproverHandlerTests`: a tagged entry outside `env/` is listed with its variables only, releases a variable after a person says yes and is refused its password before anyone is asked; an untagged entry under `env/` is outside the default.
  - `PolicyRuleTests.ARuleThroughATag_ReleasesOnlyProjectVariables`.
  - `SessionAuthorityRunTests.UnderTheDefault_ATaggedProjectRunsFromAnyGroup_AndATaggedEntrysPasswordDoesNot`: a set with a tagged entry under `services/` runs, which C.1b left refused, and a reference to that entry's password is refused unasked.
  - `ApproverProtocolTests`: a listing's fields and tags survive the frame, and every bound still holds with the larger element.
  - `EntryActivityTests`: the Agent access card shows for an entry the default reaches by its tag.
- **Bridge.** `ServerToolsTests`: under the default, `list_entry_names` shows a tagged entry with `STRIPE_SECRET_KEY` and its tags and not its password, and no untagged entry; a password request never reaches the approver and a variable request does. `ServerOptionsTests`: no `--expose` is `tag:env:*`, and a malformed selector refuses to start.
- **KeePassXC.** The C.5b step of `verify-keepassxc-projects.sh`, on a copy of the third vault KeePassXC makes, held by its own `keypaste agent` and asked through `keypaste mcp` with no `--expose`:
  - the listing names Database, Shared and Stripe, Stripe with `STRIPE_SECRET_KEY` alone and `env:billing`, no untagged entry and no value;
  - `STRIPE_SECRET_KEY` is released after the hour, and Stripe's password is refused out of scope with no prompt;
  - `run` of billing gives the child exactly `STRIPE_SECRET_KEY` and `DATABASE_URL` from entries under `services/`, and the audit line names both.
- **App.** `verify-connect-client.sh`: a client connected from the app with no exposure lists only the two projects' tagged entries in the Check's picker, and the picked one is asked for its variable, which the Check used to ask as `password`.
- **Gates.** `verify-mcp-run.sh` now runs on the default exposure, its project's home entry being tagged. The gates that ask for the password of an untagged entry under `env/` to exercise sessions, approvals and policy name `--expose 'env/**'`, as do the bridge tests that name no exposure, through `McpHarness`.

## Decisions

- D-0422: the default is `tag:env:*`, a selector reaches per entry only the variable-named fields of entries with a matching project tag, and a listing names each entry's fields and project tags.
- T-38 records one secret shared by two environments; T-1 and T-4 are rewritten to the listing and exposure as they now are.

## Limits and follow-ups

- README's `list_entry_names` sentence, written for 0.3.0, does not mention field names and tags; L.1 rewrites it for 0.5.0.
- The `env` root stays reserved for the home entries keypaste writes there (D-0413); it no longer guards the default exposure.
- A deliberate cross-environment entry is reached through either environment (T-38); keypaste does not split it.
