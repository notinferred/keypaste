---
title: keypaste and Vault or OpenBao
description: Vault and OpenBao are infrastructure secrets engines for platform teams; keypaste is for a developer's own machine.
---

HashiCorp Vault and its open-source fork OpenBao are the secrets engines platform teams run: key-value secrets, dynamic credentials with leases, certificates, encryption as a service and many ways for machines to sign in. They answer a different question than keypaste: how a fleet of services gets credentials, not how a developer keeps theirs.

## How a secret moves

```mermaid
flowchart LR
  subgraph hv["Vault or OpenBao"]
    v1["your cluster"] -->|OIDC, Kubernetes, AppRole| v2["Vault Agent, envconsul, operator"]
    v2 --> v3["service"]
    v1 -->|dynamic credentials with a lease| v4["workload or agent"]
  end
  subgraph kp["keypaste"]
    k1[("vault.kdbx on your disk")] --> k2["keypaste"]
    k2 --> k3["your program or an agent, after your answer"]
  end
```

## Pick Vault or OpenBao if

- You run infrastructure and need dynamic credentials, certificates and machine sign-in at scale.

## Pick keypaste if

- You want your own logins and project secrets on your laptop, and agents that ask you.

## Learn more

- [Vault](https://developer.hashicorp.com/vault) and [OpenBao](https://openbao.org/)
