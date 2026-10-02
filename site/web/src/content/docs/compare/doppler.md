---
title: keypaste and Doppler
description: Doppler is a hosted control plane for team secrets; keypaste keeps them in a file you own.
---

Doppler is a hosted secrets manager for teams: projects and configs, `doppler run`, and syncs to many hosting platforms and cloud secret stores, with an MCP server for agents and an on-premises option for enterprises.

## How a secret moves

```mermaid
flowchart LR
  subgraph dop["Doppler"]
    d1["Doppler cloud"] -->|service token| d2["doppler run"]
    d1 -->|syncs| d3["Vercel, AWS, GitHub and more"]
    d1 --> d4["Doppler MCP server"] --> d5["agent"]
  end
  subgraph kp["keypaste"]
    k1[("vault.kdbx on your disk")] --> k2["keypaste run"]
    k3["agent"] -->|asks| k4["keypaste"]
    k1 --> k4
  end
```

## Pick Doppler if

- A team wants one hosted place for secrets that syncs to every platform it deploys to.

## Pick keypaste if

- You want free, unlimited project secrets in a file no vendor can read.

## Learn more

- [doppler.com](https://www.doppler.com/) and [its documentation](https://docs.doppler.com/)
