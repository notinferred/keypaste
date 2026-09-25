# Third-party notices

keypaste is distributed under AGPL-3.0-only and incorporates KeePassLib, the EFF long word list, the Instrument Sans and Fragment Mono typefaces and Lucide icons. Its desktop app also links Yubico's .NET SDK, and its tests carry one KeePassXC test database.

## KeePassLib

Copyright © 2003–2021 Dominik Reichl <dominik.reichl@t-online.de>. Licensed under GNU GPL version 2 or later; the full text is in `third_party/KeePassLib/LICENSE`.

The [upstream library](https://keepass.info/) is KeePass 2.61, using Timothy Byrd's [KeePassNetStandard port](https://github.com/TimothyByrd/KeePassNetStandard), tag `v2.61`. Source is vendored at `third_party/KeePassLib/`; [UPSTREAM.md](third_party/KeePassLib/UPSTREAM.md) records provenance and compile-time modifications.

KeePassLib provides the KDBX4 format, Argon2 key derivation and AES-256/ChaCha20 ciphers. Keypaste implements no cryptography of its own.

The GPL-2.0-or-later grant permits the GPLv3 option, and AGPL-3.0 §13 permits the combination. The combined distribution is AGPL-3.0-only; KeePassLib remains available under GPL-2.0-or-later.

## EFF Large Wordlist for Passphrases

Copyright (c) Electronic Frontier Foundation; the list was compiled by Joseph Bonneau. Licensed under CC-BY-4.0; the full text is in `third_party/eff-large-wordlist/LICENSE`.

The [upstream file](https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt) is EFF's large wordlist for passphrases, published 2016-07-18 on its [document page](https://www.eff.org/document/passphrase-wordlists). It is vendored verbatim at `third_party/eff-large-wordlist/`; [UPSTREAM.md](third_party/eff-large-wordlist/UPSTREAM.md) records provenance, the licence reading and what the file is asserted to prove.

Keypaste reads it as data: `Keypaste.Core.WordList` embeds the file, verifies its SHA-256 at first use and draws passphrase words from it. The list is not code and is not modified. The combined distribution is AGPL-3.0-only; the list remains available under CC-BY-4.0.

## Instrument Sans

Copyright 2022 The Instrument Sans Project Authors (https://github.com/Instrument/instrument-sans). Licensed under the SIL Open Font License, Version 1.1; the full text is in `src/Keypaste.App/Assets/Fonts/OFL-InstrumentSans.txt`.

The Regular, Medium and SemiBold faces are embedded unmodified in the desktop app as its interface typeface. The fonts remain under the OFL.

## Fragment Mono

Copyright 2022 The Fragment-Mono Project Authors (https://github.com/weiweihuanghuang/fragment-mono). Licensed under the SIL Open Font License, Version 1.1; the full text is in `src/Keypaste.App/Assets/Fonts/OFL-FragmentMono.txt`.

The Regular face is embedded unmodified in the desktop app for keys, values, paths and commands. The font remains under the OFL.

## Lucide

Copyright (c) Lucide Contributors 2022; portions copyright (c) Cole Bemis 2013-2022 as part of Feather (MIT). Licensed under the ISC License; the full text is in `third_party/lucide/LICENSE`, and [UPSTREAM.md](third_party/lucide/UPSTREAM.md) records the release.

The icons are vendored as SVG at `third_party/lucide/icons/`. [lucide-to-axaml.py](scripts/lucide-to-axaml.py) converts the ones the desktop app uses into path geometry in `src/Keypaste.App/Theme/Icons.axaml`, which the app strokes at its own width; the shapes are not otherwise modified.

## Yubico .NET SDK

Copyright (c) Yubico AB. Licensed under the Apache License, Version 2.0; the full text is at <https://www.apache.org/licenses/LICENSE-2.0> and in each package as `LICENSE.txt`.

The desktop app references `Yubico.YubiKey` 1.18.0 from NuGet, with its `Yubico.Core` and `Yubico.NativeShims` packages, to ask a YubiKey for HMAC-SHA1 challenge-response ([Directory.Packages.props](Directory.Packages.props), D-0366). They are not vendored or modified, and the CLI and the MCP bridge do not contain them. `Yubico.NativeShims` is a native library that statically contains OpenSSL's libcrypto, which is also Apache-2.0. Apache-2.0 code may be combined into a GPLv3 work, and so into this AGPL-3.0-only distribution; the SDK remains under Apache-2.0.

## KeePassXC test database

`tests/Keypaste.Core.Tests/HardwareKeys/KeePassXcChallengeResponseTests.cs` embeds KeePassXC's `tests/data/YubiKeyProtectedPasswords.kdbx`, copyright the KeePassXC Team, licensed under GNU GPL version 2 or 3 as KeePassXC is. It is test data that proves keypaste opens a database KeePassXC protected with a YubiKey; no shipped binary contains it. keypaste takes the GPLv3 option, which AGPL-3.0 §13 permits combining.
