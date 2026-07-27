# The shell both recorded panes run. Sourced with `bash --rcfile`, so the author's own dotfiles
# stay out of the recording.
#
# KEYPASTE_DEMO_ROOT is exported into the session by record-demo.sh; this file only reads it.

DEMO_ROOT="${KEYPASTE_DEMO_ROOT:-$HOME/kp}"

export PATH="$DEMO_ROOT/bin:$HOME/.local/bin:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

# The demo's own audit log, never the author's. record-demo.sh refuses to start if this resolves
# to ~/.keypaste.
export KEYPASTE_HOME="$DEMO_ROOT/state"

# Claude may compose a shell command containing the released credential. There is no reason for
# that to outlive the recording.
export HISTFILE=/dev/null
unset HISTSIZE

# A bare prompt. The default WSL prompt is `wsl@HOSTNAME:~/kp/billing$` in colour, which spends
# twenty-five columns on nothing and stamps one machine's hostname onto a published asset.
export PS1='$ '
export TERM=xterm-256color

# nvm's node, where Claude Code was installed.
# shellcheck disable=SC1090
[ -s "$HOME/.nvm/nvm.sh" ] && . "$HOME/.nvm/nvm.sh" && nvm use --lts >/dev/null 2>&1

clear
