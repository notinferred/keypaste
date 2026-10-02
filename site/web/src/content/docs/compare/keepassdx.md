---
title: keypaste and KeePassDX
description: KeePassDX carries your vault on Android; keypaste adds projects and agents on the desktop.
---

KeePassDX is an open-source KeePass app for Android with autofill, passkeys and biometric unlock, and it needs no network. keypaste does not build an Android app: KeePassDX already opens the same file.

## How a secret moves

```mermaid
flowchart LR
  vault[("vault.kdbx, synced by you")]
  subgraph android["KeePassDX"]
    d1["KeePassDX on Android"] --> d2["autofill and passkeys"]
  end
  subgraph desk["keypaste"]
    p1["keypaste on your desktop"] --> p2["project runs and agent approvals"]
  end
  vault <--> d1
  vault <--> p1
```

## Using them together

Use KeePassDX for logins on your phone and keypaste for projects and agents on your desktop, with your own file sync between them.

## Learn more

- [KeePassDX on GitHub](https://github.com/Kunzisoft/KeePassDX)
