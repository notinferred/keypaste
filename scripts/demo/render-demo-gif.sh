#!/usr/bin/env bash
#
# render-demo-gif.sh
#
# Renders the committed cast to the committed GIF, and refuses to hand over one too large to put
# at the top of a README.
#
# Every agg value is pinned explicitly rather than left to a default, so that upgrading agg cannot
# quietly change a committed asset. The theme is pinned for the same reason: a re-record in a
# different palette reads as a different product.
#
# THE SIZE PROBLEM, because it will surprise you. --idle-time-limit only collapses stretches where
# nothing changes, and Claude Code animates a spinner and a token counter the entire time it is
# thinking. Every one of those seconds is a fully-changed frame. Expect the first render to be
# several times the budget, and escalate in the order listed under "too big" below.
#
# Usage:  scripts/demo/render-demo-gif.sh [--select <agg selector>]
set -euo pipefail

cd "$(dirname "$0")/../.."
# shellcheck source=scripts/demo/demo-env.sh
. scripts/demo/demo-env.sh

SELECT=""
if [ "${1:-}" = "--select" ]; then
  [ -n "${2:-}" ] || die "--select needs a selector, e.g. 5..45 or 10%..90%"
  SELECT="$2"
fi

AGG="${AGG:-$HOME/.local/bin/agg}"
[ -x "$AGG" ] || die "no agg at $AGG (run scripts/demo/install-recording-tools.sh)"
[ -f "$DEMO_CAST" ] || die "no cast at $DEMO_CAST (run scripts/demo/record-demo.sh)"

# Ship at or under 2 MB; refuse over 4. A README that takes ten seconds to paint is not marketing.
readonly TARGET=2097152
readonly CEILING=4194304

raw="$(mktemp -d)/raw.gif"

# --text-font-family rather than --font-family: the latter bypasses the automatic fallbacks,
# including the emoji list, and Claude Code draws glyphs we do not control.
agg_args=(
  --text-font-family "JetBrains Mono,DejaVu Sans Mono"
  --font-size 14
  --line-height 1.4
  --theme dracula
  --speed 1.0
  --fps-cap 10
  --idle-time-limit 1
  --last-frame-duration 3
  --renderer resvg
)
[ -n "$SELECT" ] && agg_args+=(--select "$SELECT")

say "==> agg"
"$AGG" "${agg_args[@]}" "$DEMO_CAST" "$raw" || die "agg failed"

say "==> gifsicle"
if command -v gifsicle >/dev/null 2>&1; then
  gifsicle -O3 --lossy=60 --colors 128 -o "$DEMO_GIF" "$raw" || die "gifsicle failed"
else
  say "    not installed; shipping agg's output unoptimised"
  cp "$raw" "$DEMO_GIF"
fi
rm -rf "$(dirname "$raw")"

size="$(wc -c <"$DEMO_GIF")"
say ""
say "    $DEMO_GIF"
say "    $size bytes ($((size / 1024)) KiB)"

if [ "$size" -gt "$CEILING" ]; then
  say ""
  say "  Too big. In order, cheapest first:"
  say ""
  say "    1. gifsicle --lossy=100 --colors 96   (shows only on glyph edges)"
  say "    2. agg --fps-cap 8"
  say "    3. render a 20-30 second cut for the README and keep the full cast:"
  say "         scripts/demo/render-demo-gif.sh --select 5..35"
  say "       This is the one to reach for. The README wants prompt -> dialog -> y ->"
  say "       masked deploy -> log table; the whole session belongs on the site, played"
  say "       back from the same committed cast."
  say "    4. --speed 1.25, AND say '(1.25x)' in the caption. Disclosed is fine."
  say "    5. --font-size 12, last, because legibility is the one thing a terminal GIF"
  say "       cannot spend."
  say ""
  say "  Never edit timestamps inside the cast. A global --speed is a stated"
  say "  transformation; a rewritten timeline is a fabrication."
  die "the GIF is $size bytes, over the $CEILING ceiling"
fi

[ "$size" -gt "$TARGET" ] && say "    over the ${TARGET}-byte target but under the ceiling - acceptable, trim if you can"
say ""
say "    Both artefacts are committed: the cast is the reviewable one."
