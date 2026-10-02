---
title: keypaste and Infisical
description: Infisical runs a secrets platform for teams from a server; keypaste keeps secrets in a file and plans an end-to-end relay for teams.
---

Infisical is a secrets platform for teams, with an open-source core, in its cloud or on your server. The server encrypts secrets with its own keys and delivers them through `infisical run`, SDKs, a Kubernetes operator and syncs to hosting platforms, with rotation, dynamic secrets, certificates and privileged access beside them. Its Agent Vault gives an agent only a session token: a proxy matches each request's host, method and path, and attaches the real credential on the way out.

## How a secret moves

```mermaid
flowchart LR
  subgraph inf["Infisical"]
    i1["Infisical server, holds the keys"] --> i2["infisical run, SDKs, syncs"]
    i3["agent with a session token"] --> i4["Agent Vault proxy"]
    i1 --> i4
    i4 -->|attaches the credential| i5["the API"]
  end
  subgraph kp["keypaste"]
    k1[("vault.kdbx on your disk")] --> k2["keypaste app, unlocked"]
    k2 --> k3["keypaste run"]
    k4["agent"] -->|asks| k2
  end
```

keypaste's planned [agents without values](/products/agents-without-values/) does Agent Vault's job from the unlocked app on your machine, with no server. Its planned [team projects](/products/team-projects/) share secrets through a relay that stores only ciphertext, where Infisical's server can decrypt.

## Pick Infisical if

- A team needs a secrets platform today: syncs, rotation, dynamic secrets, Kubernetes and certificates.

## Pick keypaste if

- You want your secrets in a file you own, with your logins, and an answer from you before any agent gets one.

## Learn more

- [infisical.com](https://infisical.com/) and [Agent Vault](https://infisical.com/docs/documentation/platform/agent-vault/overview)
