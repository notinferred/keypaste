# Vendored BouncyCastle Twofish provenance

| Field | Value |
|---|---|
| Upstream repository | <https://github.com/bcgit/bc-csharp> |
| Tag | `release-2.7.0`, tagged 2026-07-30 |
| Commit | `4007498b13582d90ee1eda5d9920c324428b98b3` |
| Vendored on | 2026-10-22 |
| Original work | The Bouncy Castle Cryptography Library for .NET, by The Legion of the Bouncy Castle Inc. |
| Licence | MIT; `LICENSE` is upstream's `LICENSE.md` byte for byte |

KeePass writes Twofish only through its TwofishCipher plugin and KeePassXC writes it natively, so KeePassLib carries no Twofish. These files give KeePassLib's `KEYPASTE_TWOFISH` engine ([UPSTREAM.md](../KeePassLib/UPSTREAM.md)) the Twofish block cipher, the CBC mode and the PKCS#7 padding a Twofish KDBX file is encrypted with, so keypaste writes none of that cryptography itself (docs/PRODUCT.md §3.6). [DECISIONS.md](../../DECISIONS.md) D-0292 records why and the §3.9 justification. They compile into `KeePassLib.dll` through `KeePassLib.csproj`, under the quarantine of [third_party/Directory.Build.props](../Directory.Build.props), and add no package.

## Files

`src/` mirrors upstream's `crypto/src/`. Each file is upstream's file at the same path with these changes and no others, so `diff -r` against a checkout of the tag shows only them:

- Every type upstream declares `public` is `internal`, so `KeePassLib.dll` exports no BouncyCastle type and a later BouncyCastle package cannot clash with one.
- A file marked "excerpt" keeps only the members something here reaches, each as upstream writes it.
- A `using` of a namespace not vendored is dropped.
- Two additions marked `keypaste addition`: `TwofishEngine.Clear()` zeroes the key and its schedule, and `KeyParameter.Clear()` zeroes the key's copy, which upstream leaves to the garbage collector; the engine's transform calls both when it is disposed.

| File | Upstream SHA-256 | Change |
|---|---|---|
| `crypto/engines/TwofishEngine.cs` | `7075db7c0dda14523225a89e2d806d9a214de124c6fbb2120fdfe97be04ea905` | `internal`; `Clear()` added |
| `crypto/modes/CbcBlockCipher.cs` | `abf5f4bf3506576c4d33b2a477d7b191aec942551cb15449ad5732a710fd21dd` | `internal` |
| `crypto/modes/IBlockCipherMode.cs` | `d465e292278ba917edf5742f0542b4544f9b6c421638a84a5d8b97b4e0ef7899` | `internal` |
| `crypto/paddings/Pkcs7Padding.cs` | `c55492618a769f448c42e0389ffb1f30108d09c08616d9e0fefd49ce07f75580` | `internal`; drops `IBlockCipherPadding` and `Init(SecureRandom)`, whose `SecureRandom` is not vendored and which PKCS#7 ignores |
| `crypto/IBlockCipher.cs` | `e5493fe9c06a9406213016e0515a33c5e5598ccc40857cf58fa0be97327c2354` | `internal` |
| `crypto/ICipherParameters.cs` | `efb38f239406200445f75cba5b68348e494e7bc164e9fd1e91504606f09cd414` | `internal` |
| `crypto/Check.cs` | `f7c795dc32480382c79346616c87ce41de140b89f899f3b76caf549f66186068` | none |
| `crypto/CryptoException.cs` | `748b829819e4f80d884272f38848a46d66458116f99d42876ef375e10787be74` | excerpt: the message constructor |
| `crypto/DataLengthException.cs` | `7ca22df1ba69f71cd9f03e2b4cc8a2b0f5550c61b57de131e31f133b0b5eac1f` | excerpt: the message constructor |
| `crypto/OutputLengthException.cs` | `40234b4a76f700e159aed64d816f5da68e3f303465f48819d36aa3fdcc410136` | excerpt: the message constructor |
| `crypto/InvalidCipherTextException.cs` | `b699a753246add67d84a098006e51343380714908042c38912ad0cf29d2fbc5d` | excerpt: the message constructor |
| `crypto/parameters/KeyParameter.cs` | `5cec32a72fbaa91b8ccffd7008b062642dde2728556b6482f4ee49de70c23e62` | excerpt: `KeyParameter(byte[])`, `GetKey()`; `Clear()` added |
| `crypto/parameters/ParametersWithIV.cs` | `b1c929fce9732db8b342c534b6edc6221b0bfb0aa1f9de161212d845b00ac6c4` | excerpt: `ParametersWithIV(ICipherParameters, byte[])`, `CopyIVTo`, `IVLength`, `Parameters` |
| `crypto/util/Pack.cs` | `1b6e5429479d852937d6f7a694678c0bf2ddeac29ace39c4b33d891046f7a828` | excerpt: `LE_To_UInt32(byte[], int)`, `LE_To_UInt32(ReadOnlySpan<byte>)`, `UInt32_To_LE(uint, Span<byte>)` |
| `util/Arrays.cs` | `c3dc3e52c26c7fdacecb2e2591e97e1324086fa152e19ca4c6d517732e3de4c0` | excerpt: `Fill`, `CopyBuffer`, `CopyBufferToSegment` and the internal copies and checks they call |
| `util/Integers.cs` | `d56466c84c9d8203bc31141d81bc13f851cac0a46a70dd1e94019959140c7e48` | excerpt: `RotateLeft(int, int)`, `RotateRight(int, int)` |
| `util/Platform.cs` | `7f449afb0b28723362d09cd522a32c4d677d801b4818b43365f6261270df6a88` | excerpt: `GetTypeName` |
| `LICENSE` (upstream `LICENSE.md`) | `0e01f1549c9022f406392ac2947d32223b7c2e977d21ea2f8c182fdeb4dae5fd` | none |

## Licence

The MIT licence permits use, modification and redistribution provided its copyright and permission notice accompany the software. [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md), which every package ships, carries both. MIT code may be combined into this AGPL-3.0-only distribution; these files remain available under MIT.

## Review and evidence

`Keypaste.Core.Tests.TwofishCipherTests` holds the registered engine to the Twofish authors' published known answers for 256-bit keys (the full encryptions of the paper's appendix and the 49-step table chain from its known-answer diskette) and to the first iterations of the diskette's CBC Monte Carlo encryption, and refuses damaged padding and a partial block; `TwofishVaultTests` opens and saves KDBX 4 and 3.1 Twofish vaults. [verify-keepassxc-workflows.sh](../../scripts/verify-keepassxc-workflows.sh) has real KeePassXC open a Twofish vault the shipped CLI saved and keypaste read one KeePassXC saved.

## Updating

1. Check out the new tag of <https://github.com/bcgit/bc-csharp> and compare each file above with `crypto/src/<path>`, reading every change to the Twofish engine, the CBC mode and the PKCS#7 padding.
2. Copy the new files, reapply the changes listed above and update the tag, commit and SHA-256 columns.
3. Run the core tests and the workflows gate.
