# Release vaults

Each `v<version>/vault.kdbx` was written by that published release's own CLI and is never changed afterwards. `ReleaseVaultFixtureTests` opens every one with today's Core, checks its exact contents and that a save reopens, holding [law 14](../../../SECURITY.md#security-laws). Each future release adds its own directory here, written by its published binary, and its version to the test's `Releases`.

The master password of every vault here is `keypaste-fixture-password`. These are test data and hold no real secret.

v0.1.0, v0.2.0 and v0.3.0 were made on 2026-11-18 on macOS arm64 with `keypaste-<version>-osx-arm64.tar.gz` from `https://dl.keypaste.com/v<version>/`, checked against its `.sha256` and `SHA256SUMS`, with `HOME` and `KEYPASTE_VAULT` pointing at a scratch directory and `V` set to the version:

```sh
P=keypaste-fixture-password
printf '%s\n%s\n' $P $P | keypaste init vault.kdbx
printf '%s\n%s\n' $P "forge-pässword-$V" | keypaste add Forge --group web --username octo --url https://forge.example/login --notes "Fixture notes, $V
second line"
printf '%s\n%s\n' $P "mail-password-$V" | keypaste add Mail
printf '%s\n%s\n' $P "removed-password-$V" | keypaste add Removed --group web
printf '%s\n' $P | keypaste rm web/Removed --yes
printf '%s\n%s\n' $P "postgres://fixture:pw@localhost/app" | keypaste env set app DATABASE_URL
printf '%s\n%s\n' $P "sk-fixture-$V" | keypaste env set app API_KEY
```

These releases could write no custom field, tag, attachment, recycled item or key file: `rm` deletes outright and `init` takes only a password, so their vaults hold none.
