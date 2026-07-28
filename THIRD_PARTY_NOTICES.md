# Third-party notices

keypaste is distributed under AGPL-3.0-only. It incorporates the following third-party work.

## KeePassLib

- **Copyright** © 2003–2021 Dominik Reichl <dominik.reichl@t-online.de>
- **Licence** GNU General Public License, **version 2 or later**
- **Upstream** <https://keepass.info/> — KeePass 2.61
- **Port** <https://github.com/TimothyByrd/KeePassNetStandard> (Timothy Byrd), tag `v2.61`
- **Vendored at** `third_party/KeePassLib/`
- **Full licence text** `third_party/KeePassLib/LICENSE`
- **Provenance and local modifications** `third_party/KeePassLib/UPSTREAM.md`

KeePassLib provides keypaste's KDBX4 container format, Argon2 key derivation, and AES-256/ChaCha20 ciphers. keypaste implements no cryptography of its own (docs/PRODUCT.md §3.6).

The GPL-2.0-**or-later** grant permits taking the GPLv3 option, and AGPL-3.0 §13 permits combining GPLv3 work with AGPLv3 work. The combined distribution is AGPL-3.0-only; the KeePassLib portions remain available under GPL-2.0-or-later.

Source for the vendored portion is in this repository. Modifications are limited to the compile-time guards documented in `third_party/KeePassLib/UPSTREAM.md`.
