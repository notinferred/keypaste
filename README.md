<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/brand/keypaste-wordmark-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="assets/brand/keypaste-wordmark-light.svg">
    <img alt="keypaste" src="assets/brand/keypaste-wordmark-light.svg" width="240">
  </picture>
</h1>

<p align="center">AI agents get your secrets only when you say yes.</p>

keypaste keeps your passwords, API keys and project values in a KeePass file on your computer, with no account. When an AI agent such as Claude Code needs a secret, it asks for that one value, and you allow or deny it. Every request is logged, and KeePassXC opens the same file.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="assets/screenshots/approval-dark.png"><img src="assets/screenshots/approval-light.png" width="360" alt="An agent's request: claude-code asks for STRIPE_SECRET_KEY, with its reason quoted and the choices Deny, Always allow and Allow once."></picture><br><sub>The desktop app, coming in 1.0</sub></p>

## Why keypaste

- **Your vault, your file.** A standard KDBX file with no account or cloud. KeePassXC opens it too, and keypaste writes no cryptography of its own.
- **Agents ask, you decide.** An agent gets one field per request, only after you allow it. Every request, allowed or refused, goes in a local log.
- **No more `.env` files.** `keypaste run myapp -- npm start` gives a project's values to the command and writes nothing to disk.
- **Works with your tools.** `keypaste setup` connects Claude Code and Codex, and prints the configuration for Cursor and Claude Desktop.

## Install

`v0.3.0`, the command-line tool and AI bridge, is out for macOS, Linux and Windows: [install it](docs/install.md).

## Start

```sh
keypaste init ~/vault.kdbx
keypaste setup --vault ~/vault.kdbx
keypaste agent --vault ~/vault.kdbx
```

Create a vault, connect your AI tools, then answer their requests in this terminal.

## Docs

[Guides](docs/) · [Features](docs/FEATURES.md) · [Security](SECURITY.md) · [Contributing](CONTRIBUTING.md) · [keypaste.com](https://keypaste.com)

Report security problems privately to <security@keypaste.com>.

[AGPL-3.0](LICENSE)
