# F.67 — Show the NativeAOT keypaste deriving with native Argon2

Completed 2026-10-23 at `e5613ead` on `gaps1/argon2-cli`, source only: ci 39983551797 and app 39983568251 passed every job on all three runners on their first attempt. The integrated commit differs from that tree only in this record, the CHANGELOG, F.66's limit and a comment in `aot-trim-baseline.txt`.

It had no STEPS row. F.66's limits and the CHANGELOG said no run showed the NativeAOT `keypaste` deriving with the libargon2 D-0522 has it carry, and on 2026-10-23 the founder asked to complete that gap.

## Amendments

- **The surface.** On 2026-10-23 the founder chose a `keypaste doctor` finding over a hidden `--selftest` flag in the CLI (D-0525).
- **Wording.** The preview the founder chose read "2–3× slower". The rows say that unlocking and saving take longer, without a ratio, because F.66 measured 1.5–3.1× across costs and no run measures it for a person's vault.

## Diagnosis

- On Windows, KeePassLib picks its Argon2 import from `RuntimeInformation.ProcessArchitecture`, which it reads by reflection the trim cannot see through ([aot-trim-baseline.txt](../../scripts/aot-trim-baseline.txt)). A type found without its property would read no architecture, and KeePassLib would then derive in managed code without calling any import. On macOS and Linux it takes its Unix imports, which need no reflection.
- On `windows-2025` the NativeAOT `keypaste` bound `argon2.dll`. KeePassLib calls no import while its architecture reads none, so the binding shows that it read one. The run does not show whether the property was read or the type was not found, which falls back to `IntPtr.Size`. Either gives X64 on win-x64, keypaste's only Windows target, so no trim root was added.

## What changed

- `keypaste doctor` derives one Argon2d key through KeePassLib's own `Transform`, 1 pass over 64 KiB in 1 lane from constant bytes (`KeePassInterop.DeriveArgon2Once`). `Health/Argon2Reading.cs` reports, as an `argon2` finding after the versions, what KeePassLib's import was then bound to:
  - ok, naming the carried file and its path, or on Linux the system's soname;
  - a fix when the carried file is missing or was not used;
  - a note on a Linux with no libargon2.
- app.yml's package job requires the `keypaste` that `publish-desktop-bridge.sh` adds to each payload to report ok, naming the payload's file on Windows and macOS. release.yml's build job requires the same of each archive's `keypaste`, linux-arm64's included.

## Evidence

The NativeAOT `keypaste` in each payload of app 39983568251, at `e5613ead`:

| Runner | `argon2` finding |
|---|---|
| windows-2025, win-x64 | ok: "this keypaste derives vault keys with argon2.dll", at `D:\a\keypaste\keypaste\artifacts\app\win-x64\argon2.dll` |
| macos-15, osx-arm64 | ok: "this keypaste derives vault keys with libargon2.1.dylib", at `/Users/runner/work/keypaste/keypaste/artifacts/app/osx-arm64/libargon2.1.dylib` |
| ubuntu-24.04, linux-x64 | ok: "this keypaste derives vault keys with the system's libargon2.so.1" |

No per-commit job builds linux-arm64, so a probe did, once. Probe 39993800232 ran at `96728d96`, which is `main` at `552005c0` plus only the probe's workflow. On `ubuntu-22.04-arm` it published the NativeAOT `keypaste` for linux-arm64 as release.yml does: an aarch64 ELF with no JIT files beside it, reporting 0.5.0. Its doctor said ok: "this keypaste derives vault keys with the system's libargon2.so.1", the file that image's `libargon2-1:arm64` package installs. The workflow ran only on a push to its own branch and was deleted with that branch.

In ci 39983551797's test jobs, which run JIT builds:

- `HealthCheckTests` holds each wording of the finding.
- `NativeArgon2Tests.Doctor_s_derivation_names_the_library_KeePassLib_derives_with` requires doctor's derivation to bind the carried file, or on Linux the system's library.
- `DoctorVerbTests` carries the row in its transcripts and in the JSON's order.

## Decisions

- D-0525.

## Limits and follow-ups

- No run has executed release.yml's step; it first runs at the next tag. Between tags no job builds linux-arm64's NativeAOT `keypaste`; the probe showed it once.
- app.yml runs doctor on the payload before it is signed, so the `keypaste` in the signed macOS bundle and in a Developer ID-signed CLI archive is not checked. 3.5b's row holds the signed DMG's `--selftest` to the library; the signed `keypaste` has no such check yet.
