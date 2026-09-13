# Third-party notices

keypaste is distributed under AGPL-3.0-only and incorporates KeePassLib.

## KeePassLib

Copyright © 2003–2021 Dominik Reichl <dominik.reichl@t-online.de>. Licensed under GNU GPL version 2 or later; the full text is in `third_party/KeePassLib/LICENSE`.

The [upstream library](https://keepass.info/) is KeePass 2.61, using Timothy Byrd's [KeePassNetStandard port](https://github.com/TimothyByrd/KeePassNetStandard), tag `v2.61`. Source is vendored at `third_party/KeePassLib/`; [UPSTREAM.md](third_party/KeePassLib/UPSTREAM.md) records provenance and compile-time modifications.

KeePassLib provides the KDBX4 format, Argon2 key derivation and AES-256/ChaCha20 ciphers. Keypaste implements no cryptography of its own.

The GPL-2.0-or-later grant permits the GPLv3 option, and AGPL-3.0 §13 permits the combination. The combined distribution is AGPL-3.0-only; KeePassLib remains available under GPL-2.0-or-later.
