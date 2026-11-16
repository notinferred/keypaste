---
title: keypaste and Keeper
description: Keeper serves security teams from its cloud; keypaste serves a developer from a file they own.
---

Keeper is a hosted, zero-knowledge password manager built for companies, with Secrets Manager for project secrets, Commander for administration and KeeperPAM for privileged access, rotation and session recording. Its MCP server reaches only the vault folders you choose and confirms sensitive operations.

## How a secret moves

```mermaid
flowchart LR
  subgraph kpr["Keeper"]
    c1["Keeper cloud"] --> c2["Secrets Manager"]
    c2 -->|ksm exec, keeper:// notation| c3["app or CI"]
    c2 --> c4["MCP server, chosen folders"]
    c4 -->|confirms sensitive operations| c5["agent"]
  end
  subgraph kp["keypaste"]
    k1[("vault.kdbx on your disk")] --> k2["keypaste"]
    k2 -->|one field after your answer| k3["agent"]
  end
```

## Pick Keeper if

- A security team needs administration, privileged access and session recording across a company.

## Pick keypaste if

- You are one developer, or a small team to come, and want your secrets in a file with no account.

## Learn more

- [keepersecurity.com](https://www.keepersecurity.com/) and [Keeper's MCP server](https://docs.keeper.io/en/keeperpam/secrets-manager/integrations/model-context-protocol-mcp-for-ai-agents)
