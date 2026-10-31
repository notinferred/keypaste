# Third-party notices

keypaste is distributed under AGPL-3.0-only and incorporates KeePassLib, BouncyCastle's Twofish, the Argon2 reference implementation, libsodium, the EFF long word list, SecLists' list of common passwords, Mozilla's Public Suffix List, the Instrument Sans and Fragment Mono typefaces and Lucide icons, and links Polly. Its desktop app also links Yubico's .NET SDK, and its Windows package the Windows SDK's .NET projection, and its tests carry one KeePassXC test database.

## KeePassLib

Copyright © 2003–2021 Dominik Reichl <dominik.reichl@t-online.de>. Licensed under GNU GPL version 2 or later; the full text is in `third_party/KeePassLib/LICENSE`.

The [upstream library](https://keepass.info/) is KeePass 2.61, using Timothy Byrd's [KeePassNetStandard port](https://github.com/TimothyByrd/KeePassNetStandard), tag `v2.61`. Source is vendored at `third_party/KeePassLib/`; [UPSTREAM.md](third_party/KeePassLib/UPSTREAM.md) records provenance and compile-time modifications.

KeePassLib provides the KDBX4 format, Argon2 key derivation and AES-256/ChaCha20 ciphers, and reads and writes Twofish through BouncyCastle's code below. Keypaste implements no cryptography of its own.

The GPL-2.0-or-later grant permits the GPLv3 option, and AGPL-3.0 §13 permits the combination. The combined distribution is AGPL-3.0-only; KeePassLib remains available under GPL-2.0-or-later.

## BouncyCastle Twofish

Copyright (c) 2000-2026 The Legion of the Bouncy Castle Inc. (https://www.bouncycastle.org). Licensed under the MIT License, whose notice follows as upstream's `LICENSE.md` gives it, which `third_party/bouncycastle-twofish/LICENSE` holds:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sub license, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The [upstream library](https://github.com/bcgit/bc-csharp) is BouncyCastle C#, tag `release-2.7.0`. Its Twofish block cipher, CBC mode and PKCS#7 padding, with the interfaces and helpers they reach, are vendored at `third_party/bouncycastle-twofish/` and compiled into KeePassLib, every type internal; [UPSTREAM.md](third_party/bouncycastle-twofish/UPSTREAM.md) records each file's upstream SHA-256 and every change. They encrypt and decrypt a vault whose cipher is Twofish. The combined distribution is AGPL-3.0-only; the code remains available under the MIT License.

## Argon2 reference implementation

Copyright 2015 Daniel Dinu, Dmitry Khovratovich, Jean-Philippe Aumasson and Samuel Neves. Licensed under the Creative Commons CC0 1.0 Universal waiver or the Apache License, Version 2.0, at your option; both texts are in the release archive's `LICENSE`, and at <https://creativecommons.org/publicdomain/zero/1.0/> and <https://www.apache.org/licenses/LICENSE-2.0>.

On Windows and macOS the app and the CLI carry `argon2.dll` or `libargon2.1.dylib` beside their binaries, which [build-argon2.sh](scripts/build-argon2.sh) compiles unmodified from release `20190702` of the Password Hashing Competition's [phc-winner-argon2](https://github.com/P-H-C/phc-winner-argon2), its archive pinned by SHA-256. KeePassLib derives vault keys with it (DECISIONS.md D-0522). Either licence permits combining it into this AGPL-3.0-only distribution, and the library remains available under both.

## libsodium

Copyright (c) 2013-2026 Frank Denis <j at pureftpd dot org>. Licensed under the ISC License, whose notice follows as the release archive's `LICENSE` gives it:

> Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.
>
> THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

The NativeAOT `keypaste`, in the CLI archives and beside the desktop app, links libsodium, which [build-libsodium.sh](scripts/build-libsodium.sh) compiles unmodified from [release `1.0.22`](https://github.com/jedisct1/libsodium/releases/tag/1.0.22-RELEASE), its archive pinned by SHA-256 and checked against upstream's Minisign signature. keypaste seals and opens NaCl boxes with it for browser fill (DECISIONS.md D-0611), and embeds NaCl's box test vectors from libsodium's `test/default` as the answers its box must give. The ISC licence permits combining it into this AGPL-3.0-only distribution, and the library remains available under it.

## EFF Large Wordlist for Passphrases

Copyright (c) Electronic Frontier Foundation; the list was compiled by Joseph Bonneau. Licensed under CC-BY-4.0; the full text is in `third_party/eff-large-wordlist/LICENSE`.

The [upstream file](https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt) is EFF's large wordlist for passphrases, published 2016-07-18 on its [document page](https://www.eff.org/document/passphrase-wordlists). It is vendored verbatim at `third_party/eff-large-wordlist/`; [UPSTREAM.md](third_party/eff-large-wordlist/UPSTREAM.md) records provenance, the licence reading and what the file is asserted to prove.

Keypaste reads it as data: `Keypaste.Core.WordList` embeds the file, verifies its SHA-256 at first use and draws passphrase words from it. The list is not code and is not modified. The combined distribution is AGPL-3.0-only; the list remains available under CC-BY-4.0.

## SecLists common passwords

Copyright (c) 2018 Daniel Miessler. Licensed under the MIT License; the full text is in `third_party/seclists-common-passwords/LICENSE`.

The [upstream file](https://github.com/danielmiessler/SecLists/blob/913b327317496d062bcc7cace524aaad8a693be2/Passwords/Common-Credentials/10k-most-common.txt) is SecLists' `Passwords/Common-Credentials/10k-most-common.txt` at commit `913b3273`. It is vendored gzipped at `third_party/seclists-common-passwords/`; [UPSTREAM.md](third_party/seclists-common-passwords/UPSTREAM.md) records provenance and how the compressed file is made from upstream's bytes.

keypaste reads it as data: `Keypaste.Core.Recommendations.CommonPasswords` embeds the file, verifies the SHA-256 of what it decompresses to at first use, and KeePassLib's password estimate rates each password against it for Settings › Recommendations. The list is not code and its contents are not modified. The combined distribution is AGPL-3.0-only; the list remains available under the MIT License.

## Public Suffix List

Mozilla's Public Suffix List, by the Mozilla Foundation and the list's contributors. Licensed under the Mozilla Public License, Version 2.0; the full text is in `third_party/publicsuffix/LICENSE`.

The [upstream list](https://publicsuffix.org/list/) is vendored at `third_party/publicsuffix/` with each internationalised rule written as its A-labels and nothing else changed; [UPSTREAM.md](third_party/publicsuffix/UPSTREAM.md) records its version, commit and SHA-256 and how the change is made. keypaste reads it as data: `Keypaste.Core` embeds the file, and browser fill takes each host's registrable domain from it. MPL-2.0 is a file-level licence: the vendored file remains under MPL-2.0, its source form is that directory, published with keypaste's source, and MPL-2.0 §3.3 permits distributing it within this AGPL-3.0-only work. The tests also read the list's `test_psl.txt`, dedicated to the public domain under CC0 1.0, which no shipped binary contains.

## Instrument Sans

Copyright 2022 The Instrument Sans Project Authors (https://github.com/Instrument/instrument-sans). Licensed under the SIL Open Font License, Version 1.1; the full text is in `src/Keypaste.App/Assets/Fonts/OFL-InstrumentSans.txt`.

The Regular, Medium and SemiBold faces are embedded unmodified in the desktop app as its interface typeface. The fonts remain under the OFL.

## Fragment Mono

Copyright 2022 The Fragment-Mono Project Authors (https://github.com/weiweihuanghuang/fragment-mono). Licensed under the SIL Open Font License, Version 1.1; the full text is in `src/Keypaste.App/Assets/Fonts/OFL-FragmentMono.txt`.

The Regular face is embedded unmodified in the desktop app for keys, values, paths and commands. The font remains under the OFL.

## Lucide

Copyright (c) Lucide Contributors 2022; portions copyright (c) Cole Bemis 2013-2022 as part of Feather (MIT). Licensed under the ISC License; the full text is in `third_party/lucide/LICENSE`, and [UPSTREAM.md](third_party/lucide/UPSTREAM.md) records the release.

The icons are vendored as SVG at `third_party/lucide/icons/`. [lucide-to-axaml.py](scripts/lucide-to-axaml.py) converts the ones the desktop app uses into path geometry in `src/Keypaste.App/Theme/Icons.axaml`, which the app strokes at its own width; the shapes are not otherwise modified.

## Polly

Copyright (c) 2015-2025, App vNext. Licensed under the BSD 3-Clause License, whose text follows as the package's `LICENSE` gives it:

> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
> 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
> 3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

`Keypaste.Core` references `Polly.Core` 8.9.0 from NuGet for its retries and timeouts ([Directory.Packages.props](Directory.Packages.props), D-0677), so the desktop app, the CLI and the MCP bridge contain it. It is not vendored or modified. The BSD 3-Clause License permits combining it into this AGPL-3.0-only distribution, and the library remains available under it.

## Yubico .NET SDK

Copyright (c) Yubico AB. Licensed under the Apache License, Version 2.0; the full text is at <https://www.apache.org/licenses/LICENSE-2.0> and in each package as `LICENSE.txt`.

The desktop app references `Yubico.YubiKey` 1.18.0 from NuGet, with its `Yubico.Core` and `Yubico.NativeShims` packages, to ask a YubiKey for HMAC-SHA1 challenge-response ([Directory.Packages.props](Directory.Packages.props), D-0366). They are not vendored or modified, and the CLI and the MCP bridge do not contain them. `Yubico.NativeShims` is a native library that statically contains OpenSSL's libcrypto, which is also Apache-2.0. Apache-2.0 code may be combined into a GPLv3 work, and so into this AGPL-3.0-only distribution; the SDK remains under Apache-2.0.

## Windows SDK projection for .NET

Copyright (c) Microsoft Corporation. Licensed under the MIT License; the text is at <https://github.com/microsoft/CsWinRT/blob/master/LICENSE>.

The desktop app's Windows target, which only its Windows package carries, references the targeting pack `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57 that the .NET SDK supplies, and so ships `Microsoft.Windows.SDK.NET.dll` and C#/WinRT's `WinRT.Runtime.dll`, to reach Windows Hello (D-0549). They are not vendored or modified, and the CLI, the MCP bridge and the Linux and macOS packages do not contain them.

## KeePassXC test database

`tests/Keypaste.Core.Tests/HardwareKeys/KeePassXcChallengeResponseTests.cs` embeds KeePassXC's `tests/data/YubiKeyProtectedPasswords.kdbx`, copyright the KeePassXC Team, licensed under GNU GPL version 2 or 3 as KeePassXC is. It is test data that proves keypaste opens a database KeePassXC protected with a YubiKey; no shipped binary contains it. keypaste takes the GPLv3 option, which AGPL-3.0 §13 permits combining.
