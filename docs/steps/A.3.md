# A.3 — The bridge, the browser relay and the CLI cannot use the network

Completed 2026-11-18, source only.

`src/Directory.Build.props` gives `BannedSymbols.NoNetwork.txt` to `Keypaste.Mcp`, `Keypaste.Browser` and `Keypaste.Cli`, and the list now also bans `HttpListener`. The bridge and the relay needed no exemption. `keypaste share`'s upload goes through Core's `ShareClient`, already exempt in Core, so the CLI's two exemptions only name that client and its transport: all of `Commands/ShareCommand.cs`, and the test-replaceable `ShareTransport` in `CliContext.cs`. Local pipes stay allowed, as the list bans no `System.IO.Pipes` type.

## Evidence
`verify.sh compile` passes with the ban active. A probe file naming `HttpClient`, `HttpListener`, `Dns` or `TcpClient` failed the build with RS0030 in each of the three projects and was deleted. `BanExemptionsTests` lists the two new exemptions.

## Limits and follow-ups
The ban binds keypaste's own code, not its packages or Core, which the three reference (THREATS T-9).
