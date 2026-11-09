#!/usr/bin/env bash
set -Eeuo pipefail

[[ $# -ge 1 ]] || { echo "path to extracted WSL installer is required" >&2; exit 2; }
embedded="$1"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

origin="$tmp/origin.git"
work="$tmp/work"
project="$tmp/project"
user_project="$tmp/user-project"
mkdir -p "$tmp/logs" "$tmp/user-logs"

git init --bare -q --initial-branch=main "$origin"
git init -q --initial-branch=main "$work"
git -C "$work" config user.name "Control Center CI"
git -C "$work" config user.email "control-center-ci@example.invalid"

mkdir -p "$work/web/server-rs" "$work/web/test"
cat > "$work/web/server-rs/Dockerfile" <<'EOF'
FROM alpine
COPY web/src src
COPY web/tools tools
EOF
cat > "$work/web/server-rs/Dockerfile.dockerignore" <<'EOF'
*
!web/src/
!web/tools/
EOF
cat > "$work/web/test/docker.test.mjs" <<'EOF'
const ALLOWED = ['web/src/', 'web/tools/'];
const CONTEXT_ROOTS = ['web/src/', 'web/tools/'];
EOF
mkdir -p "$work/web"
cat > "$work/web/build.sh" <<'EOF'
#!/usr/bin/env bash
echo build
EOF
chmod a-x "$work/web/build.sh"
echo base > "$work/README.md"
git -C "$work" add .
git -C "$work" commit -qm "base checkout"
git -C "$work" remote add origin "$origin"
git -C "$work" push -q -u origin main

git clone -q --depth 1 "file://$origin" "$project"

# Simulate the exact source changes made by the older Control Center installer.
cat > "$project/web/server-rs/Dockerfile" <<'EOF'
FROM alpine
COPY web/src src
COPY web/tools/i18n-keys.mjs tools/i18n-keys.mjs
EOF
cat > "$project/web/server-rs/Dockerfile.dockerignore" <<'EOF'
*
!web/src/
!web/tools/
!web/tools/i18n-keys.mjs
EOF
cat > "$project/web/test/docker.test.mjs" <<'EOF'
const ALLOWED = ['web/src/', 'web/tools/i18n-keys.mjs'];
const ALLOWED_IGNORE_DIRS = ['web/tools/'];
const CONTEXT_ROOTS = ['web/src/', 'web/tools/i18n-keys.mjs'];
EOF
chmod u+x "$project/web/build.sh"

# Advance origin/main to the same known generated content.
cp "$project/web/server-rs/Dockerfile" "$work/web/server-rs/Dockerfile"
cp "$project/web/server-rs/Dockerfile.dockerignore" "$work/web/server-rs/Dockerfile.dockerignore"
cp "$project/web/test/docker.test.mjs" "$work/web/test/docker.test.mjs"
git -C "$work" add .
git -C "$work" commit -qm "upstream Docker allowlist fix"
git -C "$work" push -q origin main

python3 - "$embedded" "$tmp/updater-functions.sh" <<'PY'
from pathlib import Path
import sys
source = Path(sys.argv[1]).read_text(encoding="utf-8")
start = source.index("save_project_changes_and_stop() {")
end = source.index('\nif [[ -d "$PROJECT" ]]; then', start)
Path(sys.argv[2]).write_text(source[start:end] + "\n", encoding="utf-8")
PY

cat > "$tmp/run-updater.sh" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
die() { echo "EXPECTED_STOP: $*" >&2; exit 1; }
source "$UPDATER_FUNCTIONS"
update_existing_checkout
EOF

UPDATER_FUNCTIONS="$tmp/updater-functions.sh" PROJECT="$project" LOG_DIR="$tmp/logs" bash "$tmp/run-updater.sh"
[[ -z "$(git -C "$project" status --porcelain)" ]]
[[ "$(git -C "$project" rev-parse HEAD)" == "$(git --git-dir="$origin" rev-parse refs/heads/main)" ]]
[[ "$(git -C "$project" ls-files -s -- web/build.sh | awk '{print $1}')" == "100644" ]]

# A real user edit must not be reset: preserve it and save a patch instead.
git clone -q "file://$origin" "$user_project"
echo "user-owned edit" >> "$user_project/README.md"
set +e
UPDATER_FUNCTIONS="$tmp/updater-functions.sh" PROJECT="$user_project" LOG_DIR="$tmp/user-logs" bash "$tmp/run-updater.sh" >/tmp/nfsu2-checkout-user-edit.log 2>&1
rc=$?
set -e
[[ "$rc" -ne 0 ]]
grep -q "user-owned edit" "$user_project/README.md"
compgen -G "$tmp/user-logs/nfsu2-project-local-changes-*.patch" >/dev/null

echo "PASS: generated installer changes were repaired and fast-forwarded."
echo "PASS: unrelated user edits were preserved with a binary Git patch backup."
