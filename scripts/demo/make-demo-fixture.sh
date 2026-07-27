#!/usr/bin/env bash
#
# make-demo-fixture.sh
#
# Builds the vault and the project the demo is recorded against.
#
# Modelled on scripts/make-compat-fixture.sh, and for the same reason: it drives the SHIPPED
# binary rather than calling a library, so the fixture exercises argument parsing, group-path
# splitting and the redirected-stdin prompt exactly as a person's typing would.
#
# Run it between takes, not just once. It empties the audit log, and an empty log is the only way
# the table at the end of the demo shows exactly this take's rows.
#
# Usage:  scripts/demo/make-demo-fixture.sh
set -euo pipefail

cd "$(dirname "$0")/../.."
REPO="$PWD"
# shellcheck source=scripts/demo/demo-env.sh
. scripts/demo/demo-env.sh

refuse_outside_wsl
refuse_inside_repo "$DEMO_ROOT"

# ------------------------------------------------------------------- the guard that matters most
# A released credential is returned to the agent twice, so it is rendered in Claude's transcript,
# written to its session file, and captured in a .cast this repository commits. Recording this
# against a real vault publishes a real credential into git. The sentinel must be visibly worthless.
[[ "$DEMO_SENTINEL" =~ $DEMO_SENTINEL_PATTERN ]] \
  || die "the demo sentinel does not look like an obvious fake. Refusing: see the comment above this check."

CLI="$(resolve_bin "$DEMO_BIN/keypaste")"

# --------------------------------------------------------------------------------------- vault
say "==> vault"
rm -f "$DEMO_VAULT" "$DEMO_VAULT.lock"
mkdir -p "$DEMO_ROOT"

printf '%s\n%s\n' "$DEMO_MASTER" "$DEMO_MASTER" | "$CLI" init "$DEMO_VAULT" >/dev/null \
  || die "could not create the demo vault"

# Four entries, so list_entry_names returns something worth looking at, all under env/ so the
# DEFAULT exposure covers them. The demo must show what a reader gets without widening anything.
set_var() {
  printf '%s\n%s\n' "$DEMO_MASTER" "$2" \
    | "$CLI" env set "${1%%/*}" "${1##*/}" --vault "$DEMO_VAULT" >/dev/null \
    || die "could not store $1"
}
set_var billing/STRIPE_KEY   "$DEMO_SENTINEL"
set_var billing/DATABASE_URL 'postgres://demo:demo@localhost:5432/billing'
set_var billing/SENTRY_DSN   'https://example.invalid/0'
set_var web/NEXT_PUBLIC_API_URL 'https://staging.example.invalid'

# ------------------------------------------------------------------------------------- project
say "==> project"
rm -rf "$DEMO_PROJECT"
mkdir -p "$DEMO_PROJECT"

# Copied, never duplicated: scripts/demo/deploy.sh is the committed source of truth and the one
# scripts/verify-demo.sh asserts against. A second copy would drift from the gated one.
cp "$REPO/scripts/demo/deploy.sh" "$DEMO_PROJECT/deploy.sh"
chmod +x "$DEMO_PROJECT/deploy.sh"

# Legitimate steering, and disclosed rather than hidden: this is what a real repository using
# keypaste would say, and it is the difference between a take that works and a coin flip. It does
# not name the entry or the tool call - the agent still has to go and look.
cat > "$DEMO_PROJECT/CLAUDE.md" <<'EOF'
# billing-service

Deploying to staging is `./deploy.sh`.

It needs a Stripe API key in `STRIPE_KEY`. There is no `.env` file in this project and there
should not be one: the key lives in the developer's keypaste vault. Get it from there.
EOF

# Exactly the shape docs/mcp-setup.md documents, written from the resolved paths so it cannot
# drift. No --expose and no --approver: every flag added here is a thing the demo does that the
# setup guide does not tell a reader to do.
cat > "$DEMO_PROJECT/.mcp.json" <<EOF
{
  "mcpServers": {
    "keypaste": {
      "command": "$DEMO_BIN/keypaste-mcp",
      "args": ["--vault", "$DEMO_VAULT", "--client-label", "claude-code"]
    }
  }
}
EOF

git -C "$DEMO_PROJECT" init --quiet
git -C "$DEMO_PROJECT" add -A
git -C "$DEMO_PROJECT" -c user.email=demo@example.invalid -c user.name=demo \
  commit --quiet -m 'billing service' || true

# --------------------------------------------------------------------------------------- state
say "==> state"
mkdir -p "$DEMO_STATE"
chmod 700 "$DEMO_STATE"
rm -f "$DEMO_STATE/audit.jsonl" "$DEMO_STATE/audit.jsonl.lock"

say ""
say "    vault    $DEMO_VAULT"
say "    master   $DEMO_MASTER   (throwaway; it is never on screen)"
say "    key      $DEMO_SENTINEL"
say "    project  $DEMO_PROJECT"
say "    log      $DEMO_STATE/audit.jsonl   (emptied)"
say ""
say "    next: scripts/demo/record-demo.sh"
