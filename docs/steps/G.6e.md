# G.6e — Install Cursor and VS Code from a link

Completed 2026-10-21 in `<sha>`, source only; runs RUN_IDS.

## Amendments

The row asked for Cursor and VS Code install links composed from the registration `setup` writes, used in the app, in `setup`'s output and on the site.

- **The site part waits for G.7.** The row ships after 4.7c2, and keypaste.com deploys on every push to `main`, so nothing under `site/` changed. The links `setup` and the app compose name one computer's paths, so they cannot be the site's; G.7's row now carries that part and the question of what a site link starts.
- **Formats.** Confirmed on 2026-10-21 from Cursor's [install links page](https://cursor.com/docs/context/mcp/install-links), which gives `cursor://anysphere.cursor-deeplink/mcp/install?name=$NAME&config=$BASE64_ENCODED_CONFIG` with the config `JSON.stringify`'d and then base64-encoded, its example decoding to the server's entry alone; VS Code's [MCP developer guide](https://code.visualstudio.com/api/extension-guides/ai/mcp), which gives `vscode:mcp/install?${encodeURIComponent(JSON.stringify(obj))}` with a `name` beside the server's configuration; and VS Code's [configuration reference](https://code.visualstudio.com/docs/agents/reference/mcp-configuration), which makes `type` required for a stdio server and shows `code --add-mcp` taking `{"name","type":"stdio","command","args"}`.
- **Founder-delegated choices** (decision rule approved 2026-10-21). Where the row left a choice open, the option recommended was taken; none touches a PRODUCT §3 law, changes what leaves the vault or who may approve it, or publishes anything.
  1. One composer, `McpInstallLink.TryCreate` in Core, used by `setup` and Connect, encodes the very `McpServerRegistration` the run writes through `FileEntry`, so the link and the file entry cannot differ. Each tool's format is a catalog property, `McpClient.InstallLink`.
  2. VS Code's link carries `"type": "stdio"`, because the entry `setup` writes to VS Code's `mcp.json` has it and VS Code's reference requires it; the task's example JSON had only `name`, `command` and `args`.
  3. Cursor's base64 is percent-encoded in the query (`+`, `/` and `=` as `%2B`, `%2F` and `%3D`), because a query reader can take `+` for a space; Cursor's page shows `=` padding unencoded and says nothing about it. VS Code's JSON is percent-encoded with `Uri.EscapeDataString`, which also encodes the `!`, `'`, `(`, `)` and `*` that `encodeURIComponent` leaves.
  4. A registration the tool would rewrite (`${`) gets no link, for the same reason it gets no plan; the check moved from `TryConnect` into `McpClientSetup.Rewritten`, which both use.
  5. On request, a new `keypaste setup --link` prints the run's header, then each link alone on its own unindented line so it is copied whole, then the usual next step. It finds no tool, starts nothing and writes nothing, so it needs no terminal and no `--yes`; with no `--client` it prints Cursor's and VS Code's, `--client` naming another tool is a usage error naming the two that take one, and so is `--remove`. It exits 6 when no link could be composed.
  6. Without `--link`, a run prints a tool's link after its own lines only when keypaste cannot write that tool's file: the tool is skipped because an environment variable moves its file where keypaste does not follow, or keypaste cannot tell where it is, and after an edit is refused, such as a file keypaste cannot read safely. A tool `setup` configured gets no link, and neither does a removal.
  7. Every new fixed line is within 80 columns; a link is as long as its paths make it. The usage line now reads `[--expose <globs>]`, the option list's own spelling, so `[--link]` fits within 80 columns.
  8. Connect shows the link in a selectable box under the plan, with a caption, Open in Cursor or Open in VS Code, and Copy link, from the preview until Run it connects the tool, and still after a refused edit, where it is the way left. It is not offered for a removal or a tool that is not installed. Opening goes through the app's `IWebLauncher`, whose failure says to copy the link into a browser; Copy link is `CopyPlainAsync`, the plain copy the run command already uses, since the link holds no secret.
  9. Inside an AppImage whose `~/.local/bin/keypaste` link `setup` has yet to make or re-point, `--link` refuses and Connect offers no link, because the registration starts that path and a link opened first would start nothing. A run that made the link offers the tool's link after a refused edit.

## Evidence

Source only: no build or test ran on the founder's machine, and the integrator's compile check and RUN_IDS are the evidence.

- `McpInstallLinkTests`: Cursor's link decodes to exactly `FileEntry(typed: false)`, its only members `command` and `args` and its argv the registration's `CommandLine()`; VS Code's decodes to `name`, `type`, `command` and `args`, the rest exactly `FileEntry(typed: true)`; each over three pairs of server and vault paths, plain, with spaces, `&`, `+`, `#`, `%` and double quotes, and in Japanese, each link pure ASCII with no space, quote or `#`, one `&` in Cursor's and none in VS Code's, and equal to its own `Uri.AbsoluteUri`; an unpinned registration's link names no `--vault`; a `${` in the vault gives no link with `TryConnect`'s own refusal; only Cursor and VS Code take one.
- `SetupVerbTests`: `--link` prints both links in a harness with no tool installed, starts nothing and creates no file, and Cursor's decodes to the entry a following run writes; `--client cursor,codex --link` and `--link --remove` are usage errors; VS Code skipped under `VSCODE_PORTABLE` and Cursor refused over `{ "mcpServers": [] }` each print their link, the refused file unchanged; a configured Cursor prints none.
- `ConnectClientViewModelTests`: Cursor's pane offers the composer's link for the registration Run it writes, opens exactly that address, copies it as plain text, writes nothing, and stops offering it once Run it connects; a refused edit keeps it offered; a system that does not open it says what to do next; Claude Code, a removal and an AppImage whose link is not made yet get none.
- `PlainWordsTests` reads the link box as data, as it reads the plan.
- No secret: `McpServerRegistration` holds the bridge path, the pinned vault, log and ceilings paths, the approver's pipe name, the label and the globs, and the tests hold each link to that registration's argv and nothing else.

## Decisions

D-0472 and D-0473. T-40 names the links' path and what a link reveals.

## Limits and follow-ups

- No test opens a link in the real Cursor or VS Code: `verify-real-tools.sh` and `verify-connect-client.sh` still check only the file path, so whether each tool accepts these links, and what it shows before adding keypaste, is unverified. Extending `verify-real-tools.sh` is the follow-up once a runner can drive either tool's URL handler.
- The tests hold the link to its `Uri.AbsoluteUri`; that Avalonia's launcher hands it to the platform unchanged is unverified.
- The row ships after 4.7c2. The CLI and app parts reach people with the first release that includes them; whether that is 0.5.0 is decided at release.
- The site part is G.7's.
