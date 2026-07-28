# Vendored KeePassLib — provenance

| | |
|---|---|
| **Upstream repository** | <https://github.com/TimothyByrd/KeePassNetStandard> |
| **Tag** | `v2.61` |
| **Commit** | `87c2770496ecef9e41ab86f198c9426e2f0039e3` |
| **Vendored on** | 2026-07-25 |
| **Original work** | KeePass 2.61 by Dominik Reichl, <https://keepass.info/> |
| **Port author** | Timothy Byrd |
| **Licence** | GPL-2.0-**or-later** (see `LICENSE`) |

`KeePassNetStandard` is a source port of upstream KeePass's `KeePassLib/` to .NET Standard. Only the `KeePassLib/` directory is vendored; `KeePassLib.csproj` and `KeePassLib.pfx` were not copied (we supply our own project file and do not sign the assembly).

Why vendored rather than referenced as a package: see `DECISIONS.md` D-0007. In short, no maintained, adopted KDBX4 NuGet package for .NET exists, and the port publishes no package at this tag.

## Licence

KeePass 2.x is distributed under the GNU GPL **version 2 or later** — verified at
<https://keepass.info/help/v2/license.html> and in the header of every source file here
("either version 2 of the License, or (at your option) any later version"). The "or later" grant is what makes this combinable with keypaste's AGPL-3.0: the GPLv3 option is taken, and AGPL-3.0 §13 permits the combination. keypaste is distributed as a whole under AGPL-3.0-only.

> Note for anyone re-reading `DECISIONS.md` history: the original O-0001 asserted that KeePassLib is "GPL-2.0-only" and therefore incompatible. That premise was factually wrong.

## Local modifications

Source files are otherwise **verbatim**. Every change below is guarded by a grep-able `KEYPASTE_*` symbol defined in `KeePassLib.csproj`, so `git diff` against upstream shows exactly this list and nothing else. Code is never deleted from disk — files keypaste does not build are removed from the *compilation* in `KeePassLib.csproj`, which keeps re-merges clean.

### `KEYPASTE_NO_DPAPI` — drop the ASP.NET Core DataProtection substitution

Upstream KeePass uses Windows DPAPI (`ProtectedMemory`) to protect secrets **in memory**, and falls back to an in-tree, fully managed **ChaCha20** implementation where DPAPI is unavailable — which is the path real KeePass takes on Linux and macOS.

The port replaced that with `Microsoft.AspNetCore.DataProtection`, pulling in three ASP.NET packages and constructing an *ephemeral* provider that also creates a key directory under `%APPDATA%/KeePass2`. For keypaste that is three unwanted dependencies on the secret path (docs/PRODUCT.md §3.9) and a filesystem side effect we did not ask for.

Defining `KEYPASTE_NO_DPAPI` makes `ProtectedBinary.ProtectedMemorySupported` report `false`, which selects **upstream's own ChaCha20 path** — identical behaviour on all three platforms, zero dependencies, and no code of ours anywhere near a cipher (docs/PRODUCT.md §3.6).

| File | Change |
|---|---|
| `Security/ProtectedBinary.cs` | `ProtectedMemorySupported` returns `false`; the `ProtectedMemory` branches of `Encrypt()`/`Decrypt()` are compiled out |
| `Cryptography/CryptoUtil.cs` | `IsProtectedDataSupported` returns `false`; `ProtectData`/`UnprotectData` are **removed, not stubbed** — a stub returning plaintext would be a fail-open error path (docs/PRODUCT.md §3.7) |
| `Utility/StrUtil.cs` | `EncryptString`/`DecryptString` compiled out (they had no callers) |
| `Keys/CompositeKey.cs` | the `is KcpUserAccount` count in `ValidateUserKeys` compiled out, following the exclusion below |

### `KEYPASTE_NO_GFX` — drop `System.Drawing.Common`

`System.Drawing.Common` is Windows-only since .NET 7 and throws `PlatformNotSupportedException` elsewhere, so it cannot be part of a cross-platform vault library (docs/PRODUCT.md §4.4).

Only the *presentation* surface is affected — decoding a stored PNG into a `Bitmap`. `PwCustomIcon.ImageDataPng`, the byte array that actually lives in the KDBX file, is untouched, so **custom icons still round-trip through save/load correctly**. keypaste renders nothing.

| File | Change |
|---|---|
| `PwCustomIcon.cs` | `Image` property, `GetImage()`, `GetImage(w,h)`, `IsImageValid`, `GetKey`, and the image cache compiled out |
| `PwDatabase.cs` | the `GetCustomIcon` overloads compiled out |

### Files excluded from compilation (`KeePassLib.csproj`)

| Path | Reason |
|---|---|
| `Translation/**` | UI translation subsystem; not a library concern (upstream port excludes it too) |
| `Native/ClipboardU.cs` | desktop clipboard; needs WinForms (upstream port excludes it too) |
| `Properties/AssemblyInfo.cs` | assembly attributes are SDK-generated (upstream port excludes it too) |
| `Utility/GfxUtil.cs` | image load/scale; needs `System.Drawing.Common`; called only from `PwCustomIcon` |
| `Keys/KcpUserAccount.cs` | machine-bound key derived from the Windows account, the opposite of a portable vault file (docs/PRODUCT.md §2); also the last consumer of `CryptoUtil.ProtectData` |

**Result: zero `PackageReference` entries.** `packages.lock.json` resolves to `"net10.0": {}`. That is load-bearing, not incidental — see `DECISIONS.md` D-0004.

## Re-merging an upstream security patch

1. `git clone https://github.com/TimothyByrd/KeePassNetStandard` and check out the new tag. If the port has not tracked upstream yet, diff against `dlech/KeePass2.x` `KeePassLib/` directly and apply by hand.
2. `diff -r` that tree against `third_party/KeePassLib/`, ignoring `KeePassLib.csproj`, `UPSTREAM.md`, `LICENSE`, and `packages.lock.json`. Every hunk that is not one of the `KEYPASTE_*` guards above is an upstream change to review.
3. Copy the updated files, re-apply the guards (`grep -rn KEYPASTE_ third_party/KeePassLib` finds every site), and update the commit/tag/date in the table at the top of this file.
4. Re-run the verification chain in `DECISIONS.md` D-0007 — in particular the KeePassXC compatibility gate, which is what actually proves the merge did not break the format.

**Watch for**: any new `PackageReference` the port adds. At the time of vendoring, upstream `HEAD` (one commit past `v2.61`) had already added `System.Security.Cryptography.ProtectedData` and NuGet packaging metadata. `v2.61` was chosen over `HEAD` for that reason.
