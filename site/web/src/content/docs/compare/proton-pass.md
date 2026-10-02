---
title: keypaste and Proton Pass
description: Proton Pass injects vault items by reference from its cloud; keypaste asks you before an agent gets one.
---

Proton Pass is an end-to-end encrypted password manager in Proton's cloud, with email aliases and secure links. Its CLI resolves `pass://vault/item/field` references and runs commands with them, masking values in the output, and personal access tokens scope it to chosen vaults for scripts and agents.

## How a secret moves

```mermaid
flowchart LR
  subgraph pp["Proton Pass"]
    p1["Proton cloud"] --> p2["pass-cli with a scoped token"]
    p3["agent holding the token"] --> p2
    p2 -->|run, output masked| p4["child process"]
  end
  subgraph kp["keypaste"]
    k1[("vault.kdbx on your disk")] --> k2["keypaste"]
    k3["agent"] -->|asks| k2
    k2 -->|after your answer| k3
  end
```

`pass://` references and `pass-cli run` work like keypaste's `kp://` references and `keypaste run`. The difference is who decides: a token in the agent's environment acts without asking anyone, while keypaste asks you each time.

## Pick Proton Pass if

- You already live in Proton's apps and want aliases and hosted sync.

## Pick keypaste if

- You want a person's answer before each agent request, and your vault in a file you own.

## Learn more

- [Proton Pass](https://proton.me/pass) and [its CLI](https://proton.me/blog/proton-pass-cli)
