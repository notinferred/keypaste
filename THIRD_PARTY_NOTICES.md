# Third-party notices

keypaste is distributed under AGPL-3.0-only and incorporates KeePassLib, the EFF long word list, the Instrument Sans and Fragment Mono typefaces and Lucide icons.

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
