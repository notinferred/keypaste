# shellcheck shell=bash
# What a new terminal's keypaste is, for the desktop exercises (G.5). The shell starts as a new terminal would,
# never with this process's PATH: on Windows a PowerShell whose PATH is HKLM's then HKCU's, as a new session
# composes it; on Linux a login bash from an empty environment whose PATH lacks ~/.local/bin, so it finds
# keypaste only through the account's profile.
#
#   terminal_keypaste <windows|linux>  path<TAB>version<TAB>first line of `keypaste mcp --help`; absent when no
#                                      keypaste is found; fails<TAB>why when it does not run
#   terminal_mismatch <windows|linux> <answer> <path> <version>
#                                      why a terminal_keypaste answer is not <path> at <version> with its bridge
#                                      answering, or nothing when it is
#   user_path_entries <dir>            Windows: how many entries of HKCU's Path name <dir>
#   stock_login_profile                Linux: gives the account the login files `useradd -m` gives a new Ubuntu
#                                      account and says which profile a login bash then reads. A ~/.bash_profile
#                                      or ~/.bash_login, which /etc/skel has neither of and the runner image's
#                                      nvm setup adds, would hide ~/.profile, so each is set aside and its text
#                                      recorded; ~/.profile is seeded from /etc/skel when the account has none.

readonly BRIDGE_USAGE='usage: keypaste mcp'

terminal_keypaste() {
  local user
  case "$1" in
    windows)
      powershell -NoProfile -NonInteractive -Command "
        \$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
        \$found = Get-Command keypaste -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not \$found) { 'absent'; exit 0 }
        \$version = (keypaste --version 2>&1 | ForEach-Object { \"\$_\" }) -join ' '
        if (\$LASTEXITCODE -ne 0) { \"fails\`t\$(\$found.Source) exited \$LASTEXITCODE\`: \$version\"; exit 0 }
        \$usage = @(keypaste mcp --help)[0]
        \"\$(\$found.Source)\`t\$version\`t\$usage\"
      " | tr -d '\r'
      ;;
    linux)
      user="$(id -un)"
      # shellcheck disable=SC2016 # the new shell expands these, not this one.
      env -i HOME="$HOME" USER="$user" LOGNAME="$user" SHELL=/bin/bash TERM=dumb PATH=/usr/bin:/bin:/usr/sbin:/sbin \
        /bin/bash -l -c 'found="$(command -v keypaste)" || { echo absent; exit 0; }
          version="$(keypaste --version 2>&1)" || { printf "fails\t%s exited %s: %s\n" "$found" "$?" "$version"; exit 0; }
          printf "%s\t%s\t%s\n" "$found" "$version" "$(keypaste mcp --help 2>&1 | head -n 1)"'
      ;;
    *) echo "no terminal is checked on $1"; return 1 ;;
  esac
}

terminal_mismatch() {
  local platform="$1" said="$2" want="$3" version="$4" path reported bridge found wanted
  case "$said" in
    absent) echo "a new terminal finds no keypaste; it should run $want"; return ;;
    fails$'\t'*) echo "keypaste does not run in a new terminal: ${said#fails$'\t'}"; return ;;
  esac
  path="$(cut -f1 <<< "$said")"; reported="$(cut -f2 <<< "$said")"; bridge="$(cut -f3- <<< "$said")"
  found="$path"; wanted="$want"
  # Windows resolves paths without regard to case.
  if [ "$platform" = windows ]; then found="${path,,}"; wanted="${want,,}"; fi
  if [ "$found" != "$wanted" ]; then echo "a new terminal runs keypaste from $path, not $want"
  elif [ "$reported" != "$version" ]; then echo "a new terminal runs keypaste $reported, not $version"
  elif [[ $bridge != "$BRIDGE_USAGE"* ]]; then echo "keypaste mcp --help in a new terminal printed '$bridge'"
  fi
}

user_path_entries() {
  local want="${1%\\}" entry count=0
  while IFS= read -r entry; do
    entry="${entry%\\}"
    if [ -n "$entry" ] && [ "${entry,,}" = "${want,,}" ]; then count=$((count + 1)); fi
  done < <(powershell -NoProfile -NonInteractive -Command "[Environment]::GetEnvironmentVariable('Path', 'User')" | tr -d '\r' | tr ';' '\n')
  echo "$count"
}

stock_login_profile() {
  local name said=''
  for name in .bash_profile .bash_login; do
    [ -e "$HOME/$name" ] || continue
    mv "$HOME/$name" "$HOME/$name.set-aside" || return 1
    said="$said; set aside $HOME/$name, which read: $(tr '\n' ' ' < "$HOME/$name.set-aside")"
  done
  if [ ! -f "$HOME/.profile" ]; then
    cp /etc/skel/.profile "$HOME/.profile" || return 1
    said="$said; seeded $HOME/.profile from /etc/skel/.profile"
  fi
  echo "$HOME/.profile$said"
}
