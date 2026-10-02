---
title: keypaste and Bitwarden
description: Bitwarden asks a person for agent requests from its cloud or your server; keypaste does it on your KeePass file.
---

Bitwarden is an open-source password manager you can use in its cloud or host yourself. Its Secrets Manager is a separate product for project secrets, with `bws run`. Its Agent Access SDK pairs an agent with a Bitwarden client: the request appears in a CLI, a person approves it, and the credential is injected into a child process.

## How a secret moves

```mermaid
flowchart LR
  subgraph bw["Bitwarden"]
    b1["Bitwarden cloud or your server"] --> b2["Bitwarden client"]
    b3["agent"] --> b4["Agent Access CLI asks you"]
    b4 --> b2
    b2 -->|injects| b5["child process"]
  end
  subgraph kp["keypaste"]
    k1[("vault.kdbx on your disk")] --> k2["keypaste app"]
    k3["agent"] --> k4["keypaste-mcp"] --> k2
    k2 -->|asks you, then one field| k3
  end
```

Both ask a person before an agent gets a credential. Bitwarden does it through an account and a server; keypaste does it on a file you own, with logins and project secrets in one vault.

## Pick Bitwarden if

- You want hosted or self-hosted sync, sharing and phone apps, with an open-source server.

## Pick keypaste if

- You keep your passwords in KeePass and want agents and projects on that file, with no account.

## Learn more

- [bitwarden.com](https://bitwarden.com/), [Secrets Manager](https://bitwarden.com/products/secrets-manager/) and [the Agent Access SDK](https://bitwarden.com/blog/introducing-agent-access-sdk/)
