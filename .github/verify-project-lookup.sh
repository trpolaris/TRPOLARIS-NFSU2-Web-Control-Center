#!/usr/bin/env bash
set -Eeuo pipefail
repo_root="$(cd "$(dirname "\${BASH_SOURCE[0]}")/.." && pwd)"
lookup="\${1:-$repo_root/src/TRPOLARIS.NFSU2.WebControlCenter/Resources/nfsu2-project-lookup.sh}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
home="$tmp/home"
project="$home/nfsu2-web-trpolaris"
control="$tmp/control"
mkdir -p "$project/web/server-rs/src" "$project/web" "$control"
: > "$project/web/server-rs/Dockerfile"
: > "$project/web/server-rs/src/main.rs"
: > "$project/web/server-rs/Cargo.lock"
: > "$project/web/package.json"
: > "$project/web/package-lock.json"
: > "$project/web/gen.sh"
: > "$project/web/build.sh"
: > "$project/requirements.txt"
git -C "$project" init -q -b main
git -C "$project" config user.name "Control Center CI"
git -C "$project" config user.email "control-center-ci@example.invalid"
git -C "$project" add .
git -C "$project" commit -qm "test fixture"
git -C "$project" remote add origin https://github.com/trpolaris/NFSU2-Web.git
[[ ! -e "$project/compose.local.yml" ]]
[[ ! -e "$project/.env.local" ]]
output="$(
  NFSU2_EXPECTED_PROJECT_DIR="$project" \
  NFSU2_DEFAULT_HOME="$home" \
  NFSU2_CONTROL_ROOT="$control" \
  bash "$lookup"
)"
grep -q '^NFSU2_PROJECT_B64=' <<<"$output"
grep -q '^NFSU2_PROJECT_VALIDATED ' <<<"$output"
echo "PASS: project resolver accepts a valid checkout before Compose/.env generation."
