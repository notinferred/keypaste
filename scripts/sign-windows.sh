#!/usr/bin/env bash
# Authenticode-signs and timestamps the Windows files keypaste compiles, as the definition's signing
# policy says (docs/STEPS.md 3.6b, D-0143, D-0144, D-0204).
#
# Usage:
#   sign-windows.sh --component <cli|app> [--rehearsal <thumbprint>] <file-or-directory>...
#   sign-windows.sh --restore-dlib
#   sign-windows.sh --selftest
set -euo pipefail

readonly TIMESTAMP_URL='http://timestamp.acs.microsoft.com'
readonly FIRST_PARTY='keypaste.exe keypaste-mcp.exe keypaste-app.exe keypaste-app.dll Keypaste.Core.dll KeePassLib.dll'
readonly IDENTITY_VARS='KEYPASTE_SIGNING_ENDPOINT KEYPASTE_SIGNING_ACCOUNT KEYPASTE_SIGNING_PROFILE KEYPASTE_SIGNING_CLIENT_ID KEYPASTE_SIGNING_TENANT_ID'
DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"

die() { echo "::error::$*" >&2; exit 1; }

on_windows() { case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) return 0 ;; *) return 1 ;; esac; }

jqr() { jq -r "$@" | tr -d '\r'; }

sha256_of() { sha256sum < "$1" | cut -d' ' -f1; }

find_signtool() {
  local found
  found="$(find '/c/Program Files (x86)/Windows Kits/10/bin' -path '*/x64/signtool.exe' 2>/dev/null | sort -V | tail -n 1 || true)"
  [ -n "$found" ] || die "no x64 signtool.exe under the Windows 10 SDK"
  echo "$found"
}

dlib_record() { jqr ".signing.dlib.$1 // empty" "$DEFINITION"; }

package_cache() {
  local packages="${NUGET_PACKAGES:-$(dotnet nuget locals global-packages --list | sed -E 's/^global-packages: //' | tr -d '
')}"
  on_windows && packages="$(cygpath -u "$packages")"
  echo "${packages%/}/$(dlib_record package | tr '[:upper:]' '[:lower:]')/$(dlib_record version)"
}

find_dlib() {
  [ -n "$(dlib_record package)" ] && [ -n "$(dlib_record version)" ] && [ -n "$(dlib_record path)" ]     || die "$DEFINITION pins no signing.dlib package, version and path"
  echo "$(package_cache)/$(dlib_record path)"
}

check_dlib() {
  local dlib want
  dlib="$(find_dlib)" || exit 1
  want="$(dlib_record path_sha256)"
  [ "${#want}" -eq 64 ] || die "$DEFINITION pins no SHA-256 for the dlib"
  [ -f "$dlib" ] || die "no restored dlib at $dlib; run sign-windows.sh --restore-dlib first"
  [ "$(sha256_of "$dlib")" = "$want" ] || die "$dlib is not the dlib $DEFINITION pins"
  echo "$dlib"
}

# NuGet resolves a PackageDownload outside packages.lock.json, so the package is held to its digest here (D-0204).
restore_dlib() {
  local project id version want nupkg observed dlib
  project="$(dlib_record project)"; id="$(dlib_record package)"; version="$(dlib_record version)"; want="$(dlib_record sha512)"
  [ -n "$project" ] && [ -n "$want" ] || die "$DEFINITION pins no signing.dlib project and SHA-512"
  [ -f "$project" ] || die "no dlib restore project at $project"
  grep -qF "<PackageDownload Include=\"$id\" Version=\"[$version]\" />" "$project"     || die "$project does not download $id $version, which $DEFINITION pins"
  dotnet restore "$project" --locked-mode
  nupkg="$(package_cache)/$(echo "$id" | tr '[:upper:]' '[:lower:]').$version.nupkg"
  [ -f "$nupkg" ] || die "restoring $project left no $nupkg"
  observed="$(openssl dgst -sha512 -binary "$nupkg" | base64 | tr -d '
')"
  [ "$observed" = "$want" ] || die "$id $version is not the pinned package: sha512 $observed"
  dlib="$(check_dlib)" || exit 1
  echo "$id $version restored; x64 dlib at $dlib matches its pin"
}

select_files() {
  local target name
  for target in "$@"; do
    if [ -d "$target" ]; then
      for name in $FIRST_PARTY; do
        if [ -f "$target/$name" ]; then echo "$target/$name"; fi
      done
    elif [ -f "$target" ]; then
      case " $FIRST_PARTY " in
        *" $(basename "$target") "*) echo "$target" ;;
        *) [ "${target##*.}" = msi ] || die "$target is not a file keypaste compiles"; echo "$target" ;;
      esac
    else
      die "no file or directory at $target"
    fi
  done
}

native_path() { if on_windows; then cygpath -w "$1"; else echo "$1"; fi; }

main() {
  local component="" rehearsal=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --component) component="${2:-}"; shift 2 ;;
      --rehearsal) rehearsal="${2:-}"; [ -n "$rehearsal" ] || die "--rehearsal needs a certificate thumbprint"; shift 2 ;;
      --) shift; break ;;
      -*) die "unknown option $1" ;;
      *) break ;;
    esac
  done
  [ -n "$component" ] && [ $# -ge 1 ] || die "usage: sign-windows.sh --component <cli|app> [--rehearsal <thumbprint>] <file-or-directory>..."

  local policy
  policy="$(jqr ".components.\"$component\".signing.policy // empty" "$DEFINITION")"
  [ -n "$policy" ] || die "$DEFINITION records no signing policy for $component"

  local selected files=()
  selected="$(select_files "$@")" || exit 1
  [ -n "$selected" ] || die "nothing keypaste compiles is in: $*"
  mapfile -t files <<< "$selected"

  local args=()
  if [ -n "$rehearsal" ]; then
    [ "$policy" = "none" ] || die "a rehearsal signature is refused while the $component policy is $policy"
    case "${GITHUB_REF:-}" in refs/tags/*) die "a rehearsal signature is refused on tag $GITHUB_REF" ;; esac
    args=(/sha1 "$rehearsal" /s My)
  else
    case "$policy" in
      none)
        echo "$component signing policy is none; ${#files[@]} files left unsigned"
        return 0 ;;
      authenticode) ;;
      *) die "unknown $component signing policy '$policy'" ;;
    esac
    local var
    for var in $IDENTITY_VARS; do
      [ -n "${!var:-}" ] || die "$component asks for authenticode and $var is absent; nothing was signed"
    done
    local dlib
    dlib="$(check_dlib)" || exit 1
    local metadata="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/keypaste-signing-metadata.json"
    jq -n --arg e "$KEYPASTE_SIGNING_ENDPOINT" --arg a "$KEYPASTE_SIGNING_ACCOUNT" \
      --arg p "$KEYPASTE_SIGNING_PROFILE" --arg c "${GITHUB_RUN_ID:-local}" \
      '{Endpoint: $e, CodeSigningAccountName: $a, CertificateProfileName: $p, CorrelationId: $c}' > "$metadata"
    export AZURE_CLIENT_ID="$KEYPASTE_SIGNING_CLIENT_ID" AZURE_TENANT_ID="$KEYPASTE_SIGNING_TENANT_ID"
    args=(/dlib "$(native_path "$dlib")" /dmdf "$(native_path "$metadata")")
  fi

  local signtool file
  signtool="${SIGNTOOL:-$(find_signtool)}"
  for file in "${files[@]}"; do
    MSYS_NO_PATHCONV=1 "$signtool" sign /v /fd SHA256 /tr "$TIMESTAMP_URL" /td SHA256 "${args[@]}" "$(native_path "$file")"
  done
  echo "signed ${#files[@]} $component files"
}

selftest() {
  local work cases=0 failures=0
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN
  on_windows() { return 1; }

  mkdir -p "$work/payload" "$work/empty" "$work/packages/microsoft.artifactsigning.client/1.0.128/bin/x64"
  printf 'app' > "$work/payload/keypaste-app.exe"
  printf 'core' > "$work/payload/Keypaste.Core.dll"
  printf 'avalonia' > "$work/payload/Avalonia.Base.dll"
  printf 'dlib' > "$work/packages/microsoft.artifactsigning.client/1.0.128/bin/x64/Azure.CodeSigning.Dlib.dll"

  export SIGNTOOL="$work/signtool" NUGET_PACKAGES="$work/packages" RUNNER_TEMP="$work"
  printf '#!/usr/bin/env bash\nprintf "%%s\\n" "$*" >> "%s/calls"\n' "$work" > "$SIGNTOOL"
  chmod +x "$SIGNTOOL"

  dotnet() { :; }
  printf 'nupkg' > "$work/packages/microsoft.artifactsigning.client/1.0.128/microsoft.artifactsigning.client.1.0.128.nupkg"
  printf '<PackageDownload Include="Microsoft.ArtifactSigning.Client" Version="[1.0.128]" />
' > "$work/Keypaste.Signing.csproj"
  local dlib_sha nupkg_sha before
  dlib_sha="$(sha256_of "$work/packages/microsoft.artifactsigning.client/1.0.128/bin/x64/Azure.CodeSigning.Dlib.dll")"
  nupkg_sha="$(openssl dgst -sha512 -binary "$work/packages/microsoft.artifactsigning.client/1.0.128/microsoft.artifactsigning.client.1.0.128.nupkg" | base64 | tr -d '
')"

  definition() {
    jq -n --arg policy "$1" --arg sha "$2" --arg nupkg "${3:-$nupkg_sha}" --arg project "$work/Keypaste.Signing.csproj"       '{components: {app: {signing: {policy: $policy}}},
        signing: {dlib: {package: "Microsoft.ArtifactSigning.Client", version: "1.0.128", sha512: $nupkg, project: $project,
          path: "bin/x64/Azure.CodeSigning.Dlib.dll", path_sha256: $sha}}}' > "$work/definition.json"
  }
  before="$(cat "$work/payload"/* | sha256sum)"

  # expect <name> <pass|refuse> <message or argument the run must print or pass> -- <main arguments>
  expect() {
    local name="$1" want="$2" needle="$3" out status
    shift 4
    cases=$((cases + 1))
    rm -f "$work/calls"
    set +e
    out="$(DEFINITION="$work/definition.json" "${ENTRY:-main}" "$@" 2>&1)"
    status=$?
    set -e
    local seen="$out"
    [ -f "$work/calls" ] && seen="$seen$(cat "$work/calls")"
    if [ "$want" = pass ] && [ "$status" -ne 0 ]; then
      echo "::error::$name failed: $out"; failures=$((failures + 1)); return
    fi
    if [ "$want" = refuse ]; then
      if [ "$status" -eq 0 ]; then echo "::error::$name was accepted: $out"; failures=$((failures + 1)); return; fi
      if [ -f "$work/calls" ]; then echo "::error::$name refused after calling signtool"; failures=$((failures + 1)); return; fi
    fi
    case "$seen" in *"$needle"*) ;; *) echo "::error::$name did not show '$needle': $seen"; failures=$((failures + 1)); return ;; esac
    [ "$(cat "$work/payload"/* | sha256sum)" = "$before" ] || { echo "::error::$name changed a payload file"; failures=$((failures + 1)); return; }
    echo "  $name: $want"
  }

  definition none "$dlib_sha"
  expect none-signs-nothing pass "files left unsigned" -- --component app "$work/payload"
  expect nothing-selected refuse "nothing keypaste compiles" -- --component app "$work/empty"
  expect runtime-dll-named refuse "is not a file keypaste compiles" -- --component app "$work/payload/Avalonia.Base.dll"
  expect unknown-component refuse "records no signing policy" -- --component relay "$work/payload"
  GITHUB_REF=refs/tags/v1.0.0 expect rehearsal-on-a-tag refuse "refused on tag" -- --component app --rehearsal ABC "$work/payload"
  expect rehearsal-signs-with-the-given-certificate pass "/sha1 ABC /s My" -- --component app --rehearsal ABC "$work/payload"
  expect rehearsal-timestamps pass "/tr $TIMESTAMP_URL /td SHA256" -- --component app --rehearsal ABC "$work/payload"
  case "$(cat "$work/calls")" in
    *Avalonia*) echo "::error::a runtime DLL was signed"; failures=$((failures + 1)) ;;
    *keypaste-app.exe*Keypaste.Core.dll*) ;;
    *) echo "::error::the rehearsal did not sign both first-party files"; failures=$((failures + 1)) ;;
  esac
  printf 'msi' > "$work/keypaste-app.msi"
  expect installer-named-explicitly pass "keypaste-app.msi" -- --component app --rehearsal ABC "$work/keypaste-app.msi"

  definition bogus "$dlib_sha"
  expect unknown-policy refuse "unknown app signing policy" -- --component app "$work/payload"

  definition authenticode "$dlib_sha"
  expect rehearsal-under-a-real-policy refuse "while the app policy is authenticode" -- --component app --rehearsal ABC "$work/payload"
  export KEYPASTE_SIGNING_ENDPOINT=https://eus.codesigning.azure.net KEYPASTE_SIGNING_ACCOUNT=acct     KEYPASTE_SIGNING_PROFILE=prof KEYPASTE_SIGNING_CLIENT_ID=client KEYPASTE_SIGNING_TENANT_ID=tenant
  local var value
  for var in $IDENTITY_VARS; do
    value="${!var}"
    unset "$var"
    expect "authenticode-without-$var" refuse "$var is absent" -- --component app "$work/payload"
    export "$var=$value"
  done
  expect authenticode-signs-through-the-dlib pass "/dlib $work/packages/microsoft.artifactsigning.client/1.0.128/bin/x64/Azure.CodeSigning.Dlib.dll /dmdf $work/keypaste-signing-metadata.json" -- --component app "$work/payload"
  [ "$(jq -r '[.Endpoint, .CodeSigningAccountName, .CertificateProfileName] | join(" ")' "$work/keypaste-signing-metadata.json" 2>/dev/null)" = "https://eus.codesigning.azure.net acct prof" ]     || { echo "::error::metadata.json does not carry the identity"; failures=$((failures + 1)); }
  ENTRY=restore_dlib expect restore-accepts-the-pinned-package pass "matches its pin" --
  definition authenticode "$dlib_sha" "$(printf 'A%.0s' {1..88})"
  ENTRY=restore_dlib expect restore-refuses-another-package refuse "is not the pinned package" --
  definition authenticode "$dlib_sha"
  sed -i 's/1.0.128/1.0.115/' "$work/Keypaste.Signing.csproj"
  ENTRY=restore_dlib expect restore-refuses-a-project-on-another-version refuse "does not download" --
  sed -i 's/1.0.115/1.0.128/' "$work/Keypaste.Signing.csproj"
  definition authenticode "$(printf '%064d' 0)"
  ENTRY=restore_dlib expect restore-refuses-a-changed-dlib refuse "is not the dlib" --
  expect authenticode-dlib-changed refuse "is not the dlib" -- --component app "$work/payload"
  definition authenticode "$dlib_sha"
  rm "$work/packages/microsoft.artifactsigning.client/1.0.128/bin/x64/Azure.CodeSigning.Dlib.dll"
  expect authenticode-dlib-absent refuse "no restored dlib" -- --component app "$work/payload"
  definition authenticode ""
  expect authenticode-dlib-hash-unpinned refuse "pins no SHA-256" -- --component app "$work/payload"
  jq 'del(.signing)' "$work/definition.json" > "$work/unpinned.json" && mv "$work/unpinned.json" "$work/definition.json"
  expect authenticode-dlib-unpinned refuse "pins no signing.dlib" -- --component app "$work/payload"
  for var in $IDENTITY_VARS; do unset "$var"; done

  [ "$failures" -eq 0 ] || die "$failures of $cases sign-windows.sh cases failed"
  echo "ok: $cases sign-windows.sh cases"
}

if [ "${1:-}" = "--selftest" ]; then
  selftest
elif [ "${1:-}" = "--restore-dlib" ]; then
  on_windows || die "the Artifact Signing dlib loads only into x64 signtool on Windows"
  restore_dlib
else
  on_windows || die "sign-windows.sh signs with signtool and runs only on Windows"
  main "$@"
fi
