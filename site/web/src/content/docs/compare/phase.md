---
title: keypaste and Phase
description: Phase shares end-to-end encrypted secrets with a team; keypaste keeps them in your KeePass file and asks you about agents.
---

Phase is an open-source secrets manager that encrypts secrets on the client before its server, in its cloud or on yours, ever sees them. It delivers them through `phase run`, SDKs, a Kubernetes operator and syncs, and it has sealed secrets nobody can read back. A skill lets coding agents drive its CLI, with values masked by default.

## How a secret moves

```mermaid
flowchart LR
  subgraph ph["Phase"]
    p1["Phase console, encrypted on the client"] --> p2["phase run, SDKs, syncs"]
    p3["agent with the Phase skill"] -->|drives the CLI as you| p2
  end
  subgraph kp["keypaste"]
    k1[("vault.kdbx on your disk")] --> k2["keypaste"]
    k3["agent"] -->|asks| k2
    k2 -->|after your answer| k3
  end
```

Phase and keypaste share the principle that no server should read your secrets. keypaste's planned team projects use the same end-to-end design. The difference today is the agent: Phase's skill acts with your login, while keypaste asks you about each request.

## Pick Phase if

- A team needs end-to-end encrypted shared secrets with syncs and Kubernetes today.

## Pick keypaste if

- You want project secrets in the same KeePass file as your logins, with no account.

## Learn more

- [phase.dev](https://phase.dev/) and [its documentation](https://docs.phase.dev/)
