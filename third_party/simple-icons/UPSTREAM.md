# Vendored Simple Icons provenance

| Field | Value |
|---|---|
| Upstream package | [`simple-icons`](https://www.npmjs.com/package/simple-icons) 16.36.0 on npm |
| Upstream repository | [simple-icons/simple-icons](https://github.com/simple-icons/simple-icons), commit `27a47e6540abf45107f0bfe50fd0d1f8e6395263` (the package's `gitHead`) |
| Tarball | `simple-icons-16.36.0.tgz`, SHA-1 `45f50dc2ec8fa7a14b68b6eb1b8ac1c1e4340772`, integrity `sha512-+VNPPkqSOLidFeSgBKUWX4n50WPpDC09hav+BgS26anIF1oQOlGBb8LvwGR+6fYRjo+B6CiKzNBceMPkSVXANQ==`, both as the npm registry lists them |
| Retrieved on | 2026-11-17 |
| Licence | CC0 1.0 Universal, `LICENSE.md` being upstream's text; no vendored icon carries a licence of its own in upstream's data |

Simple Icons draws brands' marks as single paths on a 24-unit grid, each with its brand colour. keypaste draws an item's tile with the mark of the service its website belongs to, from these files alone and never from the network. CC0 covers the drawings, not the marks: `DISCLAIMER.md`, upstream's, says each is a trademark of its owner.

## Files

[simple-icons-to-axaml.py](../../scripts/simple-icons-to-axaml.py) `--from <unpacked package>` copies the icons its `ICONS` names into `icons/`, their entries from the package's `data/simple-icons.json` into `simple-icons.json`, and `LICENSE.md` and `DISCLAIMER.md`, refusing a slug the package lacks or one with a licence of its own; without `--from` it only writes `src/Keypaste.App/Theme/ServiceIcons.axaml` from this directory. No file changes in copying. Upstream no longer publishes some services' marks at their owners' request, such as Microsoft's, Amazon's, OpenAI's, Slack's and LinkedIn's, and no other password manager's mark is vendored, so those items keep their letter tile.

To update: download the package's tarball from the registry, check its SHA-1 and integrity against the registry's listing, unpack it into an empty directory, run the script with `--from` on its `package/` directory, and record the version, commit, hashes and date here.
