# F.56 — Move the bindings into their own project and generate the approver frames

Completed 2026-10-07 at `56faf30` on `task/f55`, with F.55: the dispatched ci 37653540314 and app 37653558120 runs passed every job on all three runners. The integrated commit differs from that tree only in the records.

It had no STEPS row: the founder chose this group from the audit of keypaste against two engineering references on 2026-10-07, keeping the bindings hand-written in a project of their own rather than taking a package (D-0427).

## Amendments

- The approver's decoders stay on `JsonDocument`. Generated deserialization would accept frames today's decoders refuse: a field sent as `null` where omission is allowed, a null array element, a property named twice inside an unknown field; and it would refuse a wrongly typed optional field the decoders read as missing. Matching them needs custom converters and a second pass, which would be no shorter.
- The byte-parity test and the copy of the hand-written encoder it compared against were removed once the parity runs passed; the runs are below.

## What changed

- `src/Keypaste.Mvvm` holds `ObservableObject`, `RelayCommand`, `RelayCommand<T>`, `AsyncRelayCommand` and `IRelayCommand`, references nothing, and is referenced only by the app. `DependsOn` declares once what a property or command's `CanExecute` reads; `Set` and `Raise` then raise the dependents and refresh the commands, so view models no longer chain `Raise` and `RaiseCanExecuteChanged` calls by hand. The Windows signing list, the publisher-metadata gate and `app.yml`'s paths include the new DLL.
- Every approver frame is encoded through `ApproverJsonContext`'s generated serialization; `ApproverProtocol.cs` went from 1,842 to 1,487 lines with the context adding 267.
- Every view model with hand-written notification chains declares its dependents instead; a `Raise` stays only where a value is recomputed from state no property exposes, such as a secret buffer, the audit log or another object's property.
- Fixed while converting: Rotate is disabled while an entry is being edited, the create commands grey out while Argon2 runs, and Check is re-enabled when a client's paste-it-yourself block is shown.

## Evidence

- `ApproverProtocolParityTests` compared about 250 messages across all 16 kinds byte for byte and about 130 malformed frames' accept-or-refuse answers against the old encoder and decoders: green on the old encoder (dev 37634972120, `fddcdc8`) and on the new one on all three runners (dev 37638154576, `c8bcfa9`).
- `ObservableObjectTests` and `RelayCommandTests` cover dependents, cycles and a second run of an async command while the first runs; the converted view models' tests fail when a `DependsOn` line is missing.
- Against main, the view models lost 846 lines net; the bindings grew from 133 lines inside the app to 269 in `src/Keypaste.Mvvm`, which adds `DependsOn`; the approver encoder lost 69 net.

## Decisions

D-0427.

## Limits and follow-ups

- A caller that sets a non-nullable frame field to null now has it left out where the old encoder wrote `null` or threw; the decoders treat both alike.
