# Install keypaste

`v0.3.0` is the `keypaste` command-line tool and the `keypaste-mcp` bridge that connects AI tools. Each block below downloads from `https://dl.keypaste.com/v0.3.0/`, checks the checksum and extracts both binaries. They need no .NET runtime.

## macOS

Apple Silicon.

<!-- install:macos -->
```sh
curl -fLO https://dl.keypaste.com/v0.3.0/keypaste-0.3.0-osx-arm64.tar.gz
curl -fLO https://dl.keypaste.com/v0.3.0/keypaste-0.3.0-osx-arm64.tar.gz.sha256
shasum -a 256 -c keypaste-0.3.0-osx-arm64.tar.gz.sha256
tar -xzf keypaste-0.3.0-osx-arm64.tar.gz
mkdir -p ~/.local/bin && mv keypaste keypaste-mcp ~/.local/bin/
```
<!-- /install:macos -->

**macOS 14 or later.** The floor follows [.NET 10 support](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md); no run backs this floor. Intel Macs need a [source build](#building-from-source). If macOS blocks the binary, run `xattr -d com.apple.quarantine ~/.local/bin/keypaste`.

## Linux

x64 and arm64.

<!-- install:linux -->
```sh
curl -fLO https://dl.keypaste.com/v0.3.0/keypaste-0.3.0-linux-x64.tar.gz
curl -fLO https://dl.keypaste.com/v0.3.0/keypaste-0.3.0-linux-x64.tar.gz.sha256
sha256sum -c keypaste-0.3.0-linux-x64.tar.gz.sha256
tar -xzf keypaste-0.3.0-linux-x64.tar.gz
mkdir -p ~/.local/bin && mv keypaste keypaste-mcp ~/.local/bin/
```
<!-- /install:linux -->

For arm64, substitute `linux-arm64` in all three filenames. Both are built against glibc 2.35. The x64 binary is checked on a clean Debian 12 container; the equivalent container check is not implemented for arm64. Alpine and other musl distributions need a [source build](#building-from-source).

## Windows

x64.

<!-- install:windows -->
```powershell
$a = "keypaste-0.3.0-win-x64.zip"
Invoke-WebRequest -OutFile $a "https://dl.keypaste.com/v0.3.0/$a"
Invoke-WebRequest -OutFile "$a.sha256" "https://dl.keypaste.com/v0.3.0/$a.sha256"
$want = (Get-Content "$a.sha256" -Raw).Split()[0]
if ((Get-FileHash $a -Algorithm SHA256).Hash -ne $want) { throw "checksum mismatch" }
Expand-Archive $a -DestinationPath .
```
<!-- /install:windows -->

Windows 10 1809 or later. The floor follows [.NET 10 support](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md); no run backs this floor. Put the extracted binaries in a directory on your `PATH`.

## Checking the download

The binaries are unsigned. The checksum catches a damaged download; it cannot prove who published it. Each asset carries a build attestation: [SECURITY](../SECURITY.md#verifying-a-release) shows how to check it, and [THREATS](../THREATS.md) T-21 explains the trust boundary. If you run `v0.1.0` or `v0.2.0`, replace it; [SECURITY](../SECURITY.md) lists their defects.

## Building from source

Build from source to inspect the code you run, or on Intel Macs, musl distributions such as Alpine, and Windows on ARM. With the .NET SDK pinned in [`global.json`](../global.json):

```sh
git clone --recurse-submodules https://github.com/notinferred/keypaste
cd keypaste
dotnet build keypaste.slnx -c Release
```

`keypaste` lands in `artifacts/bin/Keypaste.Cli/release/` (`.exe` on Windows). Built this way, it is also the bridge, as `keypaste mcp`. The vault is KDBX 4 with AES-256 and Argon2d, written through [KeePassLib](../third_party/KeePassLib/UPSTREAM.md) vendored from KeePass 2.61.
