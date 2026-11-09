#!/usr/bin/env bash
# TRPOLARIS Control Center updater.
# Fetches the stable main branch, refreshes WSL build tools and Python packages,
# regenerates the game-specific WASM module, builds the current React + Rust
# Docker image, swaps the module volume, recreates the server and verifies health.
# Existing game-data and state volumes are never removed or recreated.
set -Eeuo pipefail

SCRIPT_PATH="$(realpath -e -- "${BASH_SOURCE[0]}")"
SCRIPT_DIR="$(cd -- "$(dirname -- "$SCRIPT_PATH")" && pwd -P)"
PROJECT_DIR="$(pwd -P)"
ROOT="${NFSU2_CONTROL_ROOT:-$SCRIPT_DIR}"
# Defensive fallback: an empty environment variable must never produce an empty mkdir target.
if [[ -z "${ROOT//[[:space:]]/}" ]]; then ROOT="$SCRIPT_DIR"; fi
ROOT="${ROOT%/}"
if [[ -z "$ROOT" ]]; then ROOT="$SCRIPT_DIR"; fi
MODULE_DIR="$ROOT/module"
LOG_DIR="$ROOT/logs"
LOG="$LOG_DIR/nfsu2-update-$(date +%Y%m%d-%H%M%S).log"
STOP_REQUEST_FILE="${NFSU2_STOP_REQUEST_FILE:-$LOG_DIR/.nfsu2-safe-stop.request}"
REPO_URL="https://github.com/trpolaris/NFSU2-Web.git"
MODULE_VOLUME="nfsu2-web-local_module-data"
GAME_VOLUME="nfsu2-web-local_game-data"
LOCAL_IMAGE="nfsu2-web-rs:local"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
ROLLBACK_IMAGE="nfsu2-web-rs:rollback-$TIMESTAMP"
ROLLBACK_DIR="$ROOT/rollback/$TIMESTAMP"
EXPECTED_EXE_SHA256="f9dd86c054878ce6276beb07c1fd61874f7a1e4bf1f241b084c65b73e24168a7"
EXPECTED_EXE_SIZE="4800512"
WASI_DIR="$PROJECT_DIR/deps/wasi-sdk-34.0-x86_64-linux"
TMP_DIR=""
PROJECT_UID=""
PROJECT_GID=""
SERVER_STOPPED=0
MODULE_VOLUME_UPDATED=0
HOST_MODULE_UPDATED=0
ROLLBACK_READY=0
STASH_REF=""

printf '%s\\n' "[UPDATER_BOOT] script=$SCRIPT_PATH project=$PROJECT_DIR control_root=${NFSU2_CONTROL_ROOT:-<unset>} root=$ROOT log_dir=$LOG_DIR" >&2
mkdir -p -- "$LOG_DIR"
exec > >(tee -a "$LOG") 2>&1

say() { printf '[%s] %s\n' "$(date '+%F %T')" "$*"; }
progress() {
  local value="$1" label="$2" quality="${3:-estimated}"
  label="${label//$'\n'/ }"; label="${label//$'\r'/ }"; label="${label//|/ }"
  printf '[NFSU2-PROGRESS]|%s|%s|%s\n' "$value" "$quality" "$label"
}
fail() { say "HATA: $*"; exit 1; }
restore_pre_stop_image_tag() {
  if (( SERVER_STOPPED == 0 && ROLLBACK_READY == 1 )); then
    docker image tag "$ROLLBACK_IMAGE" "$LOCAL_IMAGE" && \
      say 'Sunucu henüz durdurulmadı; yerel image etiketi çalışan eski sürüme geri alındı.'
  fi
}
check_stop() {
  if [[ -f "$STOP_REQUEST_FILE" ]]; then
    say 'Güvenli durdurma isteği alındı.'
    if (( SERVER_STOPPED == 1 )); then
      rollback_update 'Güvenli durdurma isteği'
    else
      restore_pre_stop_image_tag
    fi
    echo 'UPDATE_STATUS=STOPPED'
    exit 75
  fi
}

[[ -d "$PROJECT_DIR/.git" || -f "$PROJECT_DIR/.git" ]] || fail "Git çalışma kopyası bulunamadı: $PROJECT_DIR"
[[ -f "$PROJECT_DIR/web/server-rs/Dockerfile" ]] || fail 'Güncel Rust sunucu Dockerfile dosyası bulunamadı.'
[[ -f "$PROJECT_DIR/web/server-rs/Cargo.toml" && -f "$PROJECT_DIR/web/server-rs/Cargo.lock" ]] || fail 'Rust Cargo manifest/lock dosyaları bulunamadı.'
[[ -f "$PROJECT_DIR/web/server-rs/Dockerfile.dockerignore" ]] || fail 'Güncel Docker build context allowlist dosyası bulunamadı.'
[[ -f "$PROJECT_DIR/web/package.json" && -f "$PROJECT_DIR/web/package-lock.json" ]] || fail 'Web package.json/package-lock.json bulunamadı.'
[[ -f "$PROJECT_DIR/web/build.sh" && -f "$PROJECT_DIR/web/gen.sh" ]] || fail 'Güncel WASM üretim/derleme betikleri bulunamadı.'
[[ -f "$PROJECT_DIR/requirements.txt" ]] || fail 'Python requirements.txt bulunamadı.'
[[ -f "$PROJECT_DIR/compose.local.yml" && -f "$PROJECT_DIR/.env.local" ]] || fail 'Kurulumun yerel Compose dosyaları bulunamadı. Önce tek tık kurulumu tamamla.'

PROJECT_UID="$(stat -c '%u' "$PROJECT_DIR")"
PROJECT_GID="$(stat -c '%g' "$PROJECT_DIR")"

git_safe() { git -c "safe.directory=$PROJECT_DIR" -C "$PROJECT_DIR" "$@"; }
normalize_remote() {
  local v="${1,,}"
  v="${v#https://github.com/}"; v="${v#http://github.com/}"
  v="${v#ssh://git@github.com/}"; v="${v#git://github.com/}"
  v="${v#git@github.com:}"; v="${v#github.com:}"
  v="${v%.git}"; v="${v%/}"
  printf '%s' "$v"
}

COMPOSE=(docker compose --env-file .env.local -f compose.local.yml)
run_compose() { "${COMPOSE[@]}" "$@"; }

# Restore the previous module and image if a failure occurs after the old
# server has been stopped. This never touches game-data or state volumes.
rollback_update() {
  local reason="${1:-update failure}" restore_rc=0 health_rc=0
  say "GERİ DÖNÜŞ: $reason; önceki Docker image ve WASM modülü geri yükleniyor."
  set +e
  run_compose stop server
  if [[ -n "$TMP_DIR" && -s "$TMP_DIR/old-module.tar" ]]; then
    cat "$TMP_DIR/old-module.tar" | docker run --rm --network none -i \
      -v "$MODULE_VOLUME:/module" alpine:3.22 sh -ec '
        stage="/module/.nfsu2-rollback-stage"
        rm -rf "$stage"; mkdir -p "$stage"
        tar -xf - -C "$stage"
        test -s "$stage/nfsu2.wasm" && test -s "$stage/nfsu2.json"
        cp "$stage/nfsu2.wasm" /module/nfsu2.wasm.rollback
        cp "$stage/nfsu2.json" /module/nfsu2.json.rollback
        mv -f /module/nfsu2.wasm.rollback /module/nfsu2.wasm
        mv -f /module/nfsu2.json.rollback /module/nfsu2.json
        rm -rf "$stage"
      '
    restore_rc=$?
    if (( restore_rc != 0 )); then say 'UYARI: Docker module-data volume geri yüklenemedi.'; fi
  fi
  if [[ -n "$TMP_DIR" && -d "$TMP_DIR/old-module" ]]; then
    mkdir -p "$MODULE_DIR"
    if [[ -s "$TMP_DIR/old-module/nfsu2.wasm" && -s "$TMP_DIR/old-module/nfsu2.json" ]]; then
      cp "$TMP_DIR/old-module/nfsu2.wasm" "$MODULE_DIR/nfsu2.wasm.rollback"
      cp "$TMP_DIR/old-module/nfsu2.json" "$MODULE_DIR/nfsu2.json.rollback"
      mv -f "$MODULE_DIR/nfsu2.wasm.rollback" "$MODULE_DIR/nfsu2.wasm"
      mv -f "$MODULE_DIR/nfsu2.json.rollback" "$MODULE_DIR/nfsu2.json"
    fi
  fi
  if (( ROLLBACK_READY == 1 )); then docker image tag "$ROLLBACK_IMAGE" "$LOCAL_IMAGE"; fi
  run_compose up -d --no-build --force-recreate server
  if [[ $? -eq 0 ]]; then
    local cid state health
    cid="$(run_compose ps -q server 2>/dev/null | tail -n 1)"
    if [[ -n "$cid" ]]; then
      for _ in $(seq 1 90); do
        read -r state health < <(docker inspect --format '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}' "$cid" 2>/dev/null)
        [[ "$health" == healthy ]] && { health_rc=0; break; }
        if [[ "$health" == no-healthcheck ]]; then
          port="$(grep -E '^NFSU2_HTTP_PORT=' .env.local | tail -n 1 | sed -E 's/^[^=]+="?([0-9]+)"?.*/\1/')"
          [[ "$port" =~ ^[0-9]+$ ]] && curl --noproxy '*' -fsS --max-time 2 "http://127.0.0.1:$port/game.json" -o /dev/null && { health_rc=0; break; }
        fi
        sleep 2
        health_rc=1
      done
    else health_rc=1; fi
  else health_rc=1; fi
  set -e
  SERVER_STOPPED=0
  MODULE_VOLUME_UPDATED=0
  HOST_MODULE_UPDATED=0
  if (( restore_rc != 0 || health_rc != 0 )); then
    say 'KRİTİK: Otomatik geri dönüş tamamlanamadı. Docker durumunu ve günlüklerini kontrol et.'
    run_compose ps -a || true
    run_compose logs --tail=160 server || true
  else
    say "Önceki sunucu yeniden başlatıldı ve geri dönüş sağlığı doğrulandı. Yedek image: $ROLLBACK_IMAGE"
  fi
}

on_error() {
  local rc=$?
  trap - ERR
  if (( SERVER_STOPPED == 1 )); then
    rollback_update "Beklenmeyen komut hatası (kod $rc)"
  else
    restore_pre_stop_image_tag
  fi
  say "Güncelleme işlem hattı hata ile durdu (kod $rc). Yukarıdaki günlükleri ve varsa rollback çıktısını kontrol et."
  exit "$rc"
}
trap on_error ERR

say "TRPOLARIS NFSU2 güncellemesi başlıyor. Proje: $PROJECT_DIR"
progress 2 'GitHub, WSL ve Docker ortamı doğrulanıyor'
check_stop
command -v docker >/dev/null 2>&1 || fail 'Ubuntu WSL içinde Docker CLI bulunamadı. Docker Desktop WSL Integration ayarını kontrol et.'
docker info >/dev/null 2>&1 || fail 'Docker Engine hazır değil. Docker Desktop uygulamasını başlat ve Ubuntu WSL Integration ayarını doğrula.'
docker compose version >/dev/null 2>&1 || fail 'Docker Compose v2 eklentisi bulunamadı.'

origin="$(git_safe config --local --get remote.origin.url 2>&1)" || fail "Git remote okunamadı: $origin"
[[ "$(normalize_remote "$origin")" == 'trpolaris/nfsu2-web' ]] || fail "Beklenmeyen GitHub remote: $origin"
branch="$(git_safe symbolic-ref --quiet --short HEAD 2>&1)" || fail "Git HEAD bir branch üzerinde değil: $branch"
[[ "$branch" == main ]] || fail "Güncelleme yalnızca kararlı main dalında yapılır. Mevcut dal: $branch. Dal otomatik değiştirilmedi."

say 'WSL paket yöneticisi ve temel derleme paketleri güncelleniyor'
progress 5 'Ubuntu/WSL paket indeksleri güncelleniyor'
export DEBIAN_FRONTEND=noninteractive NEEDRESTART_MODE=a
apt-get update -o Acquire::Retries=3
check_stop
progress 9 'Git, Python, derleme ve arşiv paketleri kuruluyor'
apt-get install -y --no-install-recommends \
  git curl ca-certificates tar xz-utils python3 python3-venv python3-pip python3-dev \
  build-essential pkg-config libssl-dev perl coreutils jq

# Node 24 is the web build toolchain used by the current Dockerfile. The image
# itself also runs npm ci; the lockfile-driven Docker layer is refreshed when
# its inputs change or the build cache is invalidated.
node_major=0
if command -v node >/dev/null 2>&1; then node_major="$(node -p 'Number(process.versions.node.split(".")[0])' 2>/dev/null || echo 0)"; fi
if [[ "$node_major" != 24 ]] || ! command -v npm >/dev/null 2>&1; then
  progress 12 'Node.js 24 ve npm hazırlanıyor'
  node_setup="$(mktemp)"
  curl -fsSL --retry 3 --retry-delay 2 https://deb.nodesource.com/setup_24.x -o "$node_setup"
  bash "$node_setup"
  rm -f "$node_setup"
  apt-get install -y --no-install-recommends nodejs
fi
node --version
npm --version

# The installer already provisions rustup. Refresh that toolchain as part of
# the Windows/WSL setup; the server's exact Rust build and locked crates are
# fetched again inside the Rust Docker build stage below.
export CARGO_HOME=/root/.cargo RUSTUP_HOME=/root/.rustup
export PATH="$CARGO_HOME/bin:$PATH"
if ! command -v rustup >/dev/null 2>&1; then
  progress 16 'Rust stable toolchain indiriliyor'
  rustup_script="$(mktemp)"
  curl -fsSL --retry 3 --retry-delay 2 https://sh.rustup.rs -o "$rustup_script"
  sh "$rustup_script" -y --profile minimal
  rm -f "$rustup_script"
fi
progress 19 'Rust stable toolchain güncelleniyor'
rustup update stable
rustup default stable
rustc --version
cargo --version

check_stop

# Fast-forward only: never reset, rebase or discard local commits. Tracked local
# edits are stashed and restored; untracked files remain in place and can make
# the fast-forward fail safely rather than being deleted.
status="$(git_safe status --porcelain --untracked-files=all 2>&1)" || fail "Git status alınamadı: $status"
if [[ -n "$status" ]]; then
  say 'Yerel takip edilen değişiklikler geçici Git stash içine alınıyor.'
  before="$(git_safe rev-parse -q --verify refs/stash 2>/dev/null || true)"
  git_safe stash push -m "TRPOLARIS-ControlCenter-main-update-$TIMESTAMP"
  after="$(git_safe rev-parse -q --verify refs/stash 2>/dev/null || true)"
  if [[ -n "$after" && "$after" != "$before" ]]; then STASH_REF="$(git_safe stash list -1 --format='%gd')"; fi
fi

progress 24 'GitHub main dalı indiriliyor'
say "GitHub main fetch: $REPO_URL"
if ! git_safe fetch --prune origin '+refs/heads/main:refs/remotes/origin/main'; then
  if [[ -n "$STASH_REF" ]]; then git_safe stash pop "$STASH_REF" || say "UYARI: Yerel stash korundu: $STASH_REF"; fi
  fail 'GitHub main fetch başarısız oldu. Ağ/kimlik doğrulaması kontrol edilmeli.'
fi
check_stop
if ! git_safe merge --ff-only refs/remotes/origin/main; then
  if [[ -n "$STASH_REF" ]]; then git_safe stash pop "$STASH_REF" || say "UYARI: Yerel stash korundu: $STASH_REF"; fi
  fail 'main dalı fast-forward ile güncellenemedi. Yerel commitler silinmedi; Git geçmişini incele.'
fi
if [[ -n "$STASH_REF" ]]; then
  say "Yerel değişiklikler geri yükleniyor: $STASH_REF"
  if ! git_safe stash pop "$STASH_REF"; then fail "Yerel değişiklikler çakıştı; Git stash içinde korundu: $STASH_REF"; fi
  STASH_REF=""
fi
COMMIT="$(git_safe rev-parse --short HEAD)"
say "GitHub main güncellendi: $COMMIT"

# Refresh Python tools used by web/gen.sh. The venv is isolated from Ubuntu's
# system Python; requirements.txt pins the lifter-compatible Capstone range.
progress 30 'Python sanal ortamı ve lifter paketleri kuruluyor'
cd "$PROJECT_DIR"
if [[ ! -x .venv/bin/python ]] || ! .venv/bin/python -c 'import sys' >/dev/null 2>&1; then
  python3 -m venv .venv
fi
.venv/bin/python -m pip install --disable-pip-version-check --upgrade pip
.venv/bin/python -m pip install --disable-pip-version-check --upgrade -r requirements.txt

# WASI SDK 34 compiles the C game runtime to WebAssembly. This is separate from
# the Rust HTTP server, which is compiled in the Dockerfile's Rust stage.
progress 36 'WASI SDK 34 kontrol ediliyor'
if [[ ! -x "$WASI_DIR/bin/clang" ]]; then
  say 'WASI SDK 34 indiriliyor'
  mkdir -p "$PROJECT_DIR/deps"
  sdk_tmp="$(mktemp)"
  curl -fL --retry 3 --retry-delay 2 \
    'https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-34/wasi-sdk-34.0-x86_64-linux.tar.gz' \
    -o "$sdk_tmp"
  tar -xzf "$sdk_tmp" -C "$PROJECT_DIR/deps"
  rm -f "$sdk_tmp"
fi
[[ -x "$WASI_DIR/bin/clang" ]] || fail "WASI SDK clang bulunamadı: $WASI_DIR/bin/clang"
"$WASI_DIR/bin/clang" --version | head -n 1

progress 40 'Güncel web/Docker kaynak testleri çalıştırılıyor'
node --test web/test/docker.test.mjs
check_stop

# Retrieve only the licensed executable from the existing Docker-managed game
# volume. No game files are downloaded, copied back, or modified.
progress 44 'Mevcut oyun volume içinden SPEED2.EXE ve server.dll doğrulanıyor'
TMP_DIR="$(mktemp -d /tmp/nfsu2-control-update.XXXXXX)"
docker volume inspect "$GAME_VOLUME" >/dev/null
docker volume inspect "$MODULE_VOLUME" >/dev/null
docker image inspect "$LOCAL_IMAGE" >/dev/null

# gen.sh needs server.dll beside SPEED2.EXE to regenerate the multiplayer
# guest module. Extract both source files from the existing game volume.
docker run --rm --network none -v "$GAME_VOLUME:/game:ro" alpine:3.22 sh -ec '
  test -s /game/SPEED2.EXE
  cd /game
  if [ -s server.dll ]; then tar -cf - SPEED2.EXE server.dll
  else echo "[UYARI] server.dll oyun klasöründe yok; web/gen.sh resmi stub modül davranışını kullanacak." >&2; tar -cf - SPEED2.EXE
  fi
' | tar -xf - -C "$TMP_DIR"
exe_size="$(stat -c '%s' "$TMP_DIR/SPEED2.EXE")"
exe_sha="$(sha256sum "$TMP_DIR/SPEED2.EXE" | awk '{print $1}')"
[[ "$exe_size" == "$EXPECTED_EXE_SIZE" && "$exe_sha" == "$EXPECTED_EXE_SHA256" ]] || \
  fail "Oyun volume içindeki SPEED2.EXE beklenen North America v1.2 sürümü değil. Dosya değiştirilmedi."
if [[ ! -s "$TMP_DIR/server.dll" ]]; then say 'UYARI: server.dll bulunmadı; upstream web/gen.sh davranışı gereği LAN server modülü stub olarak üretilecek.'; fi

# Rebuild generated C against the exact game executable. Never follow shared
# worktree symlinks; the generated code belongs to this checkout.
progress 49 'Oyun kodu yeni kaynaklarla yeniden üretiliyor'
if [[ -L gen/all || -L gen/server ]]; then
  fail 'gen/all veya gen/server başka bir worktree bağlantısı. Güvenlik için otomatik silinmedi; bağımsız checkout kullan.'
fi
rm -rf gen/all gen/server
mkdir -p gen/all gen/server
PATH="$PROJECT_DIR/.venv/bin:$PATH" bash web/gen.sh "$TMP_DIR/SPEED2.EXE"
check_stop

progress 60 'WASM oyun modülü yeniden derleniyor'
WASI="$WASI_DIR" J=2 PATH="$PROJECT_DIR/.venv/bin:$PATH" bash web/build.sh
[[ -s out/web/nfsu2.wasm && -s out/web/nfsu2.json ]] || fail 'WASM derlemesi beklenen nfsu2.wasm/nfsu2.json dosyalarını üretmedi.'
MODULE_REV="$(grep -o '"rev":"[^"]*"' out/web/nfsu2.json | head -n 1 | cut -d '"' -f4)"
[[ -n "$MODULE_REV" ]] || fail 'Yeni WASM metadata rev alanı okunamadı.'
say "WASM hazır: rev=$MODULE_REV size=$(stat -c '%s' out/web/nfsu2.wasm) bytes"

# Ensure the Compose file targets the expected Rust image and persistent
# volumes before building. No 'down -v' and no volume deletion is used.
progress 68 'Docker Compose ve kalıcı volume ayarları denetleniyor'
run_compose config --quiet
compose_config="$(run_compose config --format json)"
compose_image="$(jq -r '.services.server.image // empty' <<< "$compose_config")"
[[ "$compose_image" == "$LOCAL_IMAGE" ]] || fail "Compose image beklenen $LOCAL_IMAGE değil: ${compose_image:-<empty>}"
for spec in 'game-data:nfsu2-web-local_game-data' 'module-data:nfsu2-web-local_module-data' 'state:nfsu2-web-local_state'; do
  volume_key="${spec%%:*}"; expected_volume="${spec#*:}"
  actual_volume="$(jq -r --arg key "$volume_key" '.volumes[$key].name // $key' <<< "$compose_config")"
  [[ "$actual_volume" == "$expected_volume" ]] || fail "Compose volume adı beklenen $expected_volume değil: $actual_volume"
  jq -e --arg key "$volume_key" --arg expected "$expected_volume" '.services.server.volumes | any(.source == $key or .source == $expected)' <<< "$compose_config" >/dev/null || \
    fail "Compose server servisi kalıcı volume kaynağını bağlamıyor: $volume_key"
done
PORT="$(grep -E '^NFSU2_HTTP_PORT=' .env.local | tail -n 1 | sed -E 's/^[^=]+="?([0-9]+)"?.*/\1/')"
[[ "$PORT" =~ ^[0-9]+$ ]] || fail 'NFSU2_HTTP_PORT .env.local içinden okunamadı.'

# Snapshot the previous module from the actual Docker volume and the current
# image tag. The old container stays online while new npm/Rust dependencies
# download and the new Docker image is compiled.
progress 72 'Mevcut çalışan sürüm için geri dönüş kopyası hazırlanıyor'
mkdir -p "$TMP_DIR/old-module" "$ROLLBACK_DIR/module" "$MODULE_DIR"
docker run --rm --network none -v "$MODULE_VOLUME:/module:ro" alpine:3.22 \
  sh -ec 'test -s /module/nfsu2.wasm && test -s /module/nfsu2.json; tar -C /module -cf - nfsu2.wasm nfsu2.json' \
  > "$TMP_DIR/old-module.tar"
tar -xf "$TMP_DIR/old-module.tar" -C "$TMP_DIR/old-module"
cp "$TMP_DIR/old-module/nfsu2.wasm" "$ROLLBACK_DIR/module/nfsu2.wasm"
cp "$TMP_DIR/old-module/nfsu2.json" "$ROLLBACK_DIR/module/nfsu2.json"
docker image tag "$LOCAL_IMAGE" "$ROLLBACK_IMAGE"
ROLLBACK_READY=1

# Dockerfile page stage executes npm ci (package-lock.json); server stage
# installs the Rust toolchain/zig/cargo-zigbuild and compiles Cargo.lock with
# --locked. This is the automatic React + Rust dependency refresh.
progress 76 'React web paketi ve Rust sunucu bağımlılıkları Docker içinde indiriliyor'
say 'Docker image build: base images --pull, Node 24 npm ci + Rust cargo zigbuild --locked'
run_compose build --pull server
check_stop

# Do not interrupt the old running server until every new artifact is built.
progress 90 'Eski sunucu durduruluyor ve güncel WASM volume aktarılıyor'
SERVER_STOPPED=1
run_compose stop server

# Replace only the two module files. The server is stopped and mounts module
# read-only, so a short-lived helper container writes to the named volume.
tar -C out/web -cf - nfsu2.wasm nfsu2.json | docker run --rm --network none -i \
  -v "$MODULE_VOLUME:/module" alpine:3.22 sh -ec '
    stage="/module/.nfsu2-update-stage"
    rm -rf "$stage"; mkdir -p "$stage"
    tar -xf - -C "$stage"
    test -s "$stage/nfsu2.wasm" && test -s "$stage/nfsu2.json"
    cp "$stage/nfsu2.wasm" /module/nfsu2.wasm.new
    cp "$stage/nfsu2.json" /module/nfsu2.json.new
    mv -f /module/nfsu2.wasm.new /module/nfsu2.wasm
    mv -f /module/nfsu2.json.new /module/nfsu2.json
    rm -rf "$stage"
  '
MODULE_VOLUME_UPDATED=1

# Keep C:\nfsu2\module synchronized with the module volume for diagnostics and
# future repair/reinstall operations.
cp out/web/nfsu2.wasm "$MODULE_DIR/nfsu2.wasm.new"
cp out/web/nfsu2.json "$MODULE_DIR/nfsu2.json.new"
mv -f "$MODULE_DIR/nfsu2.wasm.new" "$MODULE_DIR/nfsu2.wasm"
mv -f "$MODULE_DIR/nfsu2.json.new" "$MODULE_DIR/nfsu2.json"
HOST_MODULE_UPDATED=1

progress 94 'Yeni Rust sunucu containerı başlatılıyor'
if ! run_compose up -d --no-build --force-recreate server; then
  run_compose ps -a || true
  run_compose logs --tail=160 server || true
  rollback_update 'Yeni container başlatılamadı'
  fail 'Yeni Docker container başlatılamadı; önceki sürüme geri dönüldü.'
fi

progress 96 'Docker sağlık kontrolü bekleniyor'
container_id="$(run_compose ps -q server | tail -n 1)"
[[ -n "$container_id" ]] || { rollback_update 'Container ID alınamadı'; fail 'Yeni container kimliği alınamadı; önceki sürüme geri dönüldü.'; }
ready=0
last_note=0
for attempt in $(seq 1 120); do
  check_stop
  inspect="$(docker inspect --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}|{{.RestartCount}}|{{.State.ExitCode}}' "$container_id" 2>/dev/null || true)"
  IFS='|' read -r container_state health_status restart_count exit_code <<< "$inspect"
  if [[ "$health_status" == healthy ]]; then ready=1; break; fi
  if [[ "$health_status" == no-healthcheck ]] && curl --noproxy '*' -fsS --max-time 2 "http://127.0.0.1:$PORT/game.json" -o /dev/null 2>/dev/null; then ready=1; break; fi
  elapsed=$((attempt * 2))
  if (( elapsed - last_note >= 20 )); then
    say "Yeni container hazır değil (${elapsed}s): state=${container_state:-unknown} health=${health_status:-unknown} restart=${restart_count:-0} exit=${exit_code:-unknown}"
    last_note=$elapsed
  fi
  if [[ "$health_status" == unhealthy || "$container_state" == dead ]] || \
     { [[ "$container_state" == exited || "$container_state" == restarting ]] && [[ "${restart_count:-0}" =~ ^[0-9]+$ ]] && (( restart_count >= 3 )); }; then
    break
  fi
  sleep 2
done
if (( ready != 1 )); then
  run_compose ps -a || true
  run_compose logs --tail=200 server || true
  rollback_update 'Yeni container sağlık kontrolünden geçemedi'
  fail 'Yeni server healthy olmadı; önceki Docker image ve WASM modülü geri yüklendi.'
fi

SERVER_STOPPED=0
MODULE_VOLUME_UPDATED=0
HOST_MODULE_UPDATED=0
progress 98 'Yeni Rust sunucu ve WASM modülü doğrulandı'

# Preserve the ordinary Ubuntu user's ownership of the checkout after this
# updater (which the Windows manager invokes as root) has refreshed build files.
if [[ "$PROJECT_UID" =~ ^[0-9]+$ && "$PROJECT_GID" =~ ^[0-9]+$ ]]; then
  chown -R "$PROJECT_UID:$PROJECT_GID" "$PROJECT_DIR" || say 'UYARI: Kaynak klasör sahipliği tamamen geri yüklenemedi.'
fi

cat > "$ROOT/NFSU2-UPDATE-REPORT.txt" <<REPORT
TRPOLARIS NFSU2 Web Update Report
Updated: $(date -Is)
GitHub: $REPO_URL
Branch: main
Source commit: $COMMIT
WASM module revision: $MODULE_REV
Docker image: $LOCAL_IMAGE
Rollback image: $ROLLBACK_IMAGE
Rollback module backup: $ROLLBACK_DIR/module
Compose project: nfsu2-web-local
Game volume: $GAME_VOLUME (preserved)
Module volume: $MODULE_VOLUME (updated)
State volume: nfsu2-web-local_state (preserved)
HTTP port: $PORT
Container: $container_id
Health: healthy
Build stages: Python requirements + WASI SDK + web/gen.sh + web/build.sh + Docker npm ci + Rust cargo zigbuild --locked
REPORT

rm -rf "$TMP_DIR"
TMP_DIR=""
progress 100 "Güncelleme tamamlandı · main=$COMMIT · WASM=$MODULE_REV · Docker healthy" measured
say "GÜNCELLEME BAŞARILI: main=$COMMIT wasm=$MODULE_REV image=$LOCAL_IMAGE container=$container_id"
say "Geri dönüş image etiketi korundu: $ROLLBACK_IMAGE"
echo 'UPDATE_STATUS=SUCCESS'
