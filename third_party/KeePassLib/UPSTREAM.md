# Vendored KeePassLib provenance

| Field | Value |
|---|---|
| Upstream repository | <https://github.com/TimothyByrd/KeePassNetStandard> |
| Tag | `v2.61` |
| Commit | `87c2770496ecef9e41ab86f198c9426e2f0039e3` |
| Vendored on | 2026-07-25 |
| Original work | KeePass 2.61 by Dominik Reichl, <https://keepass.info/> |
| Port author | Timothy Byrd |
| Licence | GPL-2.0-or-later; see `LICENSE` |

`KeePassNetStandard` ports KeePassLib to .NET Standard. Only `KeePassLib/` is vendored. Keypaste supplies its own project file and excludes the upstream signing key. The selected tag did not publish a NuGet package; [DECISIONS.md](../../DECISIONS.md) D-0007 records the selection.

## Licence

KeePass 2.x permits GNU GPL version 2 or later, as stated in its [licence](https://keepass.info/help/v2/license.html) and source headers. Keypaste takes the GPLv3 option, which AGPL-3.0 §13 permits combining into an AGPL-3.0-only distribution. The historical O-0001 claim that KeePassLib was GPL-2.0-only was incorrect.

## Local modifications

Each source modification uses a `KEYPASTE_*` guard defined in `KeePassLib.csproj`. Other source remains verbatim. Unused files are excluded from compilation and retained on disk for upstream comparisons.

### `KEYPASTE_NO_DPAPI`

The port replaces upstream DPAPI protection with ASP.NET Core DataProtection, adding three packages and a key directory under `%APPDATA%/KeePass2`. This guard selects upstream's managed ChaCha20 fallback for in-memory protection on all platforms, without those dependencies or filesystem writes.

| File | Change |
|---|---|
| `Security/ProtectedBinary.cs` | `ProtectedMemorySupported` returns `false`; the DPAPI branches of `Encrypt()` and `Decrypt()` are excluded |
| `Cryptography/CryptoUtil.cs` | `IsProtectedDataSupported` returns `false`; `ProtectData` and `UnprotectData` are excluded to prevent plaintext-returning stubs |
| `Utility/StrUtil.cs` | Unused `EncryptString` and `DecryptString` methods are excluded |
| `Keys/CompositeKey.cs` | The `KcpUserAccount` count in `ValidateUserKeys` is excluded |

### `KEYPASTE_NO_GFX`

This guard excludes `System.Drawing.Common` image decoding, which is Windows-only on the target runtime. Stored PNG bytes in `PwCustomIcon.ImageDataPng` remain unchanged and round-trip through save/load. Desktop rendering is separate.

| File | Change |
|---|---|
| `PwCustomIcon.cs` | `Image`, `GetImage()`, `GetImage(w,h)`, `IsImageValid`, `GetKey` and the image cache are excluded |
| `PwDatabase.cs` | `GetCustomIcon` overloads are excluded |

### Files excluded from compilation

| Path | Reason |
|---|---|
| `Translation/**` | UI translation; also excluded by the upstream port |
| `Native/ClipboardU.cs` | WinForms clipboard; also excluded upstream |
| `Properties/AssemblyInfo.cs` | Assembly attributes are SDK-generated; also excluded upstream |
| `Utility/GfxUtil.cs` | System.Drawing image decoding, used only by `PwCustomIcon` |
| `Keys/KcpUserAccount.cs` | Machine-bound Windows account key; incompatible with the portable-vault scope and the last consumer of `CryptoUtil.ProtectData` |

The project has zero `PackageReference` entries and an empty `net10.0` dependency set in `packages.lock.json` (D-0004).

## Re-merging an upstream security patch

1. Clone `https://github.com/TimothyByrd/KeePassNetStandard` and check out the selected tag. If the port lacks an upstream patch, compare against `dlech/KeePass2.x`'s `KeePassLib/` and apply it manually.
2. Compare that tree with this directory, excluding `KeePassLib.csproj`, `UPSTREAM.md`, `LICENSE` and `packages.lock.json`. Review every difference outside the documented guards.
3. Copy the updated source, reapply the guards and update the provenance table. `rg KEYPASTE_ third_party/KeePassLib` finds modification sites.
4. Run D-0007's verification chain, including real KeePassXC compatibility.

Review new package references. The upstream commit after `v2.61` added `System.Security.Cryptography.ProtectedData` and NuGet packaging metadata, which informed the original tag selection.
