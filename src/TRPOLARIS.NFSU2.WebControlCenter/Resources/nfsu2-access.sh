#!/usr/bin/env bash
set -Eeuo pipefail
MODE="${1:?mode missing}"
HTTP_PORT="${2:?http port missing}"
LAN_IP="${3:-}"
LAN_PORT="${4:-8443}"
ROOT='/mnt/c/nfsu2'
PROJECT="${NFSU2_PROJECT_DIR:-}"
if [[ -z "$PROJECT" ]]; then
  BASE_HOME="${NFSU2_INSTALL_HOME:-$(getent passwd "$(id -un)" | cut -d: -f6)}"
  PROJECT="$BASE_HOME/nfsu2-web-trpolaris"
fi
if [[ -z "${HOME:-}" ]]; then export HOME=/root; fi
COMPOSE="$PROJECT/compose.local.yml"
ENV_FILE="$PROJECT/.env.local"
LOG="$ROOT/logs/nfsu2-access-mode.log"
STOP_REQUEST_FILE="${NFSU2_STOP_REQUEST_FILE:-$ROOT/logs/.nfsu2-safe-stop.request}"
mkdir -p "$ROOT/logs"
exec > >(tee -a "$LOG") 2>&1
say() { printf '[%s] %s\n' "$(date '+%F %T')" "$*"; }
cleanup_owned_tunnel_on_stop() {
  local pid_file="$ROOT/.nfsu2-cloudflared.pid" pid='' command_line=''
  [[ -f "$pid_file" ]] || return 0
  pid="$(cat "$pid_file" 2>/dev/null || true)"
  if [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null; then
    command_line="$(ps -p "$pid" -o args= 2>/dev/null || true)"
    if [[ "$command_line" == *"$HOME/.local/bin/cloudflared"* ]]; then
      say "Stopping this operation's Quick Tunnel before safe exit (PID $pid)."
      kill "$pid" 2>/dev/null || true
      for _ in $(seq 1 8); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
      kill -9 "$pid" 2>/dev/null || true
    fi
  fi
  rm -f "$pid_file"
}
assert_not_stopped() {
  if [[ -f "$STOP_REQUEST_FILE" ]]; then
    cleanup_owned_tunnel_on_stop
    say 'STOPPED: Safe-stop request received at a checkpoint; no next operation was started.'
    echo 'ACCESS_STATUS=STOPPED'
    exit 75
  fi
}
progress() { local quality="${3:-estimated}"; printf '[NFSU2-PROGRESS]|%s|%s|%s\n' "$1" "$quality" "$2"; }
die() { say "ERROR: $*"; echo "ACCESS_STATUS=ERROR"; exit 1; }

download_with_progress() {
  local label="$1" progress_start="$2" progress_end="$3" url="$4" destination="$5"
  local temp_file="${destination}.download" total_bytes="" received=0 mapped=0 last_mapped=-1 pid rc=0
  assert_not_stopped
  total_bytes="$(curl -fsSI -L --max-time 25 "$url" 2>/dev/null | awk 'tolower($1)=="content-length:" {gsub("\\r","",$2); size=$2} END {print size}' || true)"
  [[ "$total_bytes" =~ ^[0-9]+$ ]] || total_bytes=0
  rm -f "$temp_file"
  progress "$progress_start" "$label başlatıldı · gerçek dosya yüzdesi bekleniyor" "indeterminate"
  curl -fL --retry 3 --retry-delay 2 "$url" -o "$temp_file" &
  pid=$!
  while kill -0 "$pid" 2>/dev/null; do
    received="$(stat -c '%s' "$temp_file" 2>/dev/null || echo 0)"
    if (( total_bytes > 0 )); then
      local transfer_percent=$((received * 100 / total_bytes))
      (( transfer_percent > 100 )) && transfer_percent=100
      mapped=$((progress_start + transfer_percent * (progress_end - progress_start) / 100))
      (( mapped >= progress_end )) && mapped=$((progress_end - 1))
      if (( mapped > last_mapped )); then
        progress "$mapped" "$label · gerçek aktarım: $transfer_percent% ($(numfmt --to=iec "$received" 2>/dev/null || echo "$received B") / $(numfmt --to=iec "$total_bytes" 2>/dev/null || echo "$total_bytes B"))" "measured"
        last_mapped=$mapped
      fi
    else
      if (( last_mapped < 0 )); then
        progress "$progress_start" "$label indiriliyor · sunucu dosya boyutu bildirmedi; yüzde uydurulmayacak" "indeterminate"
        last_mapped=0
      fi
    fi
    sleep 1
  done
  wait "$pid" || rc=$?
  if (( rc != 0 )); then rm -f "$temp_file"; return "$rc"; fi
  mv "$temp_file" "$destination"
  progress "$progress_end" "$label indirme adımı tamamlandı" "estimated"
  assert_not_stopped
}

[[ "$MODE" == local || "$MODE" == lan || "$MODE" == public ]] || die "Unknown access mode: $MODE"
[[ "$HTTP_PORT" =~ ^[0-9]+$ ]] && (( HTTP_PORT >= 1024 && HTTP_PORT <= 65535 )) || die "Invalid HTTP port: $HTTP_PORT"
[[ -d "$PROJECT/.git" && -f "$COMPOSE" && -f "$ENV_FILE" ]] || die "NFSU2 project/Compose files not found in $PROJECT. Complete the install first."
ORIGIN="$(git -c safe.directory="$PROJECT" -C "$PROJECT" remote get-url origin 2>/dev/null || true)"
case "$ORIGIN" in
  git@github.com:trpolaris/NFSU2-Web.git|git@github.com:trpolaris/NFSU2-Web) ORIGIN='https://github.com/trpolaris/NFSU2-Web' ;;
esac
[[ "$ORIGIN" == 'https://github.com/trpolaris/NFSU2-Web.git' || "$ORIGIN" == 'https://github.com/trpolaris/NFSU2-Web' ]] || die "Project repository did not match the expected TRPOLARIS repository. No Compose changes made."
[[ "$LAN_PORT" =~ ^[0-9]+$ ]] && (( LAN_PORT >= 1024 && LAN_PORT <= 65535 )) || die "Invalid LAN HTTPS port: $LAN_PORT"
if [[ "$MODE" == lan ]]; then
  [[ "$LAN_IP" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || die "Enter a valid IPv4 address for LAN access."
  python3 - "$LAN_IP" <<'PYIP' || die "LAN address is not a valid private IPv4 address."
import ipaddress,sys
ip=ipaddress.ip_address(sys.argv[1])
if ip.version != 4 or not ip.is_private or ip.is_loopback or ip.is_link_local: raise SystemExit(1)
PYIP
fi

assert_not_stopped
say "Applying access profile: $MODE"
progress 8 "Erişim profili hazırlanıyor"
configure_nfsu2_docker_config() {
  local source_config="${DOCKER_CONFIG:-/root/.docker}"
  local safe_config="/root/.config/nfsu2-control-center-docker"
  [[ -d "$source_config" ]] || source_config="/root/.docker"
  mkdir -p -- /root/.config/nfsu2-control-center-docker
  python3 - "$source_config/config.json" "$safe_config/config.json" <<'PYDOCKERCFG'
import json, pathlib, sys
source, target = map(pathlib.Path, sys.argv[1:3])
data = {}
try:
    if source.is_file():
        data = json.loads(source.read_text(encoding="utf-8"))
except Exception:
    data = {}
# WSL cannot execute Windows-only Docker Desktop credential helper binaries.
# Preserve contexts/auth metadata but remove helper settings from this app-only copy.
data.pop("credsStore", None)
data.pop("credHelpers", None)
target.write_text(json.dumps(data, ensure_ascii=False) + "\n", encoding="utf-8")
PYDOCKERCFG
  if [[ -d "$source_config/contexts" && ! -d "$safe_config/contexts" ]]; then cp -a "$source_config/contexts" "$safe_config/contexts"; fi
  export NFSU2_DOCKER_CONFIG="$safe_config" DOCKER_CONFIG="$safe_config"
  say "WSL Docker için uygulamaya özel güvenli yapılandırma hazırlandı; orijinal kullanıcı Docker ayarları değişmedi."
}
configure_nfsu2_docker_config
root_run() {
  if [[ "$(id -u)" == 0 ]]; then env DOCKER_CONFIG="$NFSU2_DOCKER_CONFIG" "$@"; else sudo env DOCKER_CONFIG="$NFSU2_DOCKER_CONFIG" "$@"; fi
}
if [[ "$(id -u)" != 0 ]]; then sudo -v || die "sudo authentication failed."; fi
stop_owned_tunnel() {
  local pf="$ROOT/.nfsu2-cloudflared.pid" p=''
  if [[ -f "$pf" ]]; then
    p="$(cat "$pf" 2>/dev/null || true)"
    if [[ "$p" =~ ^[0-9]+$ ]] && kill -0 "$p" 2>/dev/null; then
      say "Stopping the NFSU2 Quick Tunnel process ($p)."
      kill "$p" 2>/dev/null || true
      for _ in $(seq 1 10); do kill -0 "$p" 2>/dev/null || break; sleep 1; done
      kill -9 "$p" 2>/dev/null || true
    fi
    rm -f "$pf"
  fi
}
compose() { root_run docker compose --env-file "$ENV_FILE" -f "$COMPOSE" "$@"; }

# Stage Windows-mounted WSL sources into Docker-managed named volumes. Docker Desktop can
# expose a WSL /mnt/c bind source as an empty directory on some engine/context combinations.
# Streaming tar over docker run -i avoids that bind-mount translation for the game and WASM.
source_signature() {
  local source="$1" kind="$2"
  if [[ "$kind" == game ]]; then
    [[ -s "$source/SPEED2.EXE" ]] || die "SPEED2.EXE is missing from the selected game folder: $source"
  else
    [[ -s "$source/nfsu2.wasm" && -s "$source/nfsu2.json" ]] || die "WASM module files are missing from: $source"
  fi
  find "$source" -type f -printf '%P\0%s\0%T@\0' | LC_ALL=C sort -z | sha256sum | awk '{print $1}'
}
seed_docker_volume() {
  local volume="$1" source="$2" kind="$3" signature current
  [[ -d "$source" ]] || die "Source folder does not exist: $source"
  signature="$(source_signature "$source" "$kind")"
  root_run docker volume create "$volume" >/dev/null
  current="$(root_run docker run --rm --network none -v "$volume:/data:ro" alpine:3.22 sh -c 'cat /data/.nfsu2-source-signature 2>/dev/null || true' 2>/dev/null || true)"
  if [[ "$current" == "$signature" ]]; then
    say "Docker volume already contains the current $kind data: $volume"
    return 0
  fi
  say "Copying $kind data into Docker-managed persistent volume: $volume"
  progress 34 "$kind verileri Docker kalıcı alanına aktarılıyor" "estimated"
  # Remove only this dedicated content volume's old files, never the server state volume.
  tar -C "$source" -cpf - . | root_run docker run --rm --network none -i -v "$volume:/data" alpine:3.22 sh -ec \
    'find /data -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; tar -xpf - -C /data; chmod -R a+rX /data'
  printf '%s\n' "$signature" | root_run docker run --rm --network none -i -v "$volume:/data" alpine:3.22 sh -ec 'cat > /data/.nfsu2-source-signature.tmp; mv /data/.nfsu2-source-signature.tmp /data/.nfsu2-source-signature'
  if [[ "$kind" == game ]]; then
    root_run docker run --rm --network none -v "$volume:/data:ro" alpine:3.22 sh -ec 'test -s /data/SPEED2.EXE' || die "Game volume verification failed: $volume:/SPEED2.EXE"
  else
    root_run docker run --rm --network none -v "$volume:/data:ro" alpine:3.22 sh -ec 'test -s /data/nfsu2.wasm && test -s /data/nfsu2.json' || die "Module volume verification failed: $volume"
  fi
  say "$kind volume populated and verified."
}

prepare_runtime_data_volumes() {
  local data_source module_source
  data_source="$(awk -F= '$1=="NFSU2_DATA" {sub(/^\"/,"",$2); sub(/\"$/,"",$2); gsub(/\\\"/,"\"",$2); print $2; exit}' "$ENV_FILE")"
  module_source="$(awk -F= '$1=="NFSU2_MODULE" {sub(/^\"/,"",$2); sub(/\"$/,"",$2); gsub(/\\\"/,"\"",$2); print $2; exit}' "$ENV_FILE")"
  [[ -n "$data_source" && -n "$module_source" ]] || die "NFSU2_DATA/NFSU2_MODULE source paths are empty in $ENV_FILE"
  seed_docker_volume nfsu2-web-local_game-data "$data_source" game
  seed_docker_volume nfsu2-web-local_module-data "$module_source" module
}

seed_tls_volume() {
  local volume=nfsu2-web-local_certs
  [[ -s "$PROJECT/certs/nfsu2.crt" && -s "$PROJECT/certs/nfsu2.key" ]] || die "TLS certificate/key files are missing under $PROJECT/certs"
  root_run docker volume create "$volume" >/dev/null
  say "Streaming the LAN TLS certificate into Docker-managed volume: $volume"
  tar -C "$PROJECT/certs" -cpf - nfsu2.crt nfsu2.key | root_run docker run --rm --network none -i -v "$volume:/certs" alpine:3.22 sh -ec \
    'tar -xpf - -C /certs; chown root:root /certs/nfsu2.crt; chown 1000:1000 /certs/nfsu2.key; chmod 755 /certs; chmod 644 /certs/nfsu2.crt; chmod 640 /certs/nfsu2.key'
  root_run docker run --rm --network none --user 1000:1000 -v "$volume:/certs:ro" alpine:3.22 sh -ec \
    'test -s /certs/nfsu2.crt && test -r /certs/nfsu2.key' || die "TLS named volume is not readable by server UID 1000: $volume"
  say "TLS volume verified as UID 1000."
}

# Profile changes are transactional: if a new profile cannot start and answer health checks,
# restore the exact previous Compose/env pair and bring its service back without deleting volumes.
ROLLBACK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/nfsu2-access-rollback.XXXXXX")"
cp -p "$COMPOSE" "$ROLLBACK_DIR/compose.local.yml"
cp -p "$ENV_FILE" "$ROLLBACK_DIR/.env.local"
OLD_ACCESS_MODE="$(cat "$ROOT/nfsu2-access-mode.txt" 2>/dev/null || printf 'local')"
REQUESTED_MODE="$MODE"
STACK_REPLACEMENT_STARTED=0
PROFILE_COMMITTED=0
rollback_on_exit() {
  local rc=$?
  trap - EXIT
  if [[ "$PROFILE_COMMITTED" != 1 ]]; then
    say "The requested access profile did not complete; restoring the previous Compose and environment files."
    cp -p "$ROLLBACK_DIR/.env.local" "$ENV_FILE" 2>/dev/null || true
    if [[ "$OLD_ACCESS_MODE" == "local" && "$REQUESTED_MODE" == "lan" ]] && declare -F write_compose >/dev/null 2>&1; then
      say "The failed LAN profile is being rolled back to the known-good local profile."
      MODE=local
      write_compose
      MODE="$REQUESTED_MODE"
    else
      cp -p "$ROLLBACK_DIR/compose.local.yml" "$COMPOSE" 2>/dev/null || true
    fi
    if [[ "$STACK_REPLACEMENT_STARTED" == 1 ]]; then
      say "Restoring previous access profile: ${OLD_ACCESS_MODE:-local}."
      if root_run docker compose --env-file "$ENV_FILE" -f "$COMPOSE" up -d --force-recreate; then
        old_http_port="$(awk -F= '$1=="NFSU2_HTTP_PORT" {gsub(/["\r]/,"",$2); print $2; exit}' "$ENV_FILE")"
        [[ "$old_http_port" =~ ^[0-9]+$ ]] || old_http_port=9093
        old_lan_port="$(awk -F= '$1=="NFSU2_LAN_HTTPS_PORT" {gsub(/["\r]/,"",$2); print $2; exit}' "$ENV_FILE")"
        [[ "$old_lan_port" =~ ^[0-9]+$ ]] || old_lan_port=8443
        restored=0
        for _ in $(seq 1 20); do
          if [[ "$OLD_ACCESS_MODE" == lan ]]; then
            curl --noproxy '*' -kfsS --max-time 2 "https://127.0.0.1:$old_lan_port/game.json" -o /dev/null 2>/dev/null && { restored=1; break; }
          else
            curl --noproxy '*' -fsS --max-time 2 "http://127.0.0.1:$old_http_port/game.json" -o /dev/null 2>/dev/null && { restored=1; break; }
          fi
          sleep 1
        done
        if [[ "$restored" == 1 ]]; then
          say "Previous profile restored and its health endpoint answered."
        else
          say "WARNING: previous profile Compose was restarted, but its HTTP health endpoint did not answer; inspect docker compose logs."
          root_run docker compose --env-file "$ENV_FILE" -f "$COMPOSE" ps -a || true
          root_run docker compose --env-file "$ENV_FILE" -f "$COMPOSE" logs --tail=100 server || true
        fi
      else
        say "ERROR: previous profile could not be restarted automatically. Compose/env files were restored; named volumes were not removed."
      fi
    else
      say "Previous running container was not stopped; only the saved Compose/env files were restored."
    fi
  fi
  rm -rf "$ROLLBACK_DIR"
  exit "$rc"
}
trap rollback_on_exit EXIT

write_project_env_metadata() {
  local temp_file="${ENV_FILE}.nfsu2-tmp"
  awk '!/^NFSU2_PROJECT_DIR=/' "$ENV_FILE" > "$temp_file"
  printf 'NFSU2_PROJECT_DIR="%s"\n' "$PROJECT" >> "$temp_file"
  mv "$temp_file" "$ENV_FILE"
}
write_project_env_metadata

# Save a one-time backup; state volume is never removed by this profile switch.
if [[ ! -f "$COMPOSE.nfsu2-manager-backup.yml" ]]; then cp -p "$COMPOSE" "$COMPOSE.nfsu2-manager-backup.yml"; fi

write_compose() {
  case "$MODE" in
    local)
      cat > "$COMPOSE" <<'YAML'
name: nfsu2-web-local
services:
  server:
    build:
      context: .
      dockerfile: web/server-rs/Dockerfile
    image: nfsu2-web-rs:local
    restart: unless-stopped
    working_dir: /app
    labels:
      com.trpolaris.nfsu2.project-path: "${NFSU2_PROJECT_DIR:?NFSU2_PROJECT_DIR missing}"
      com.trpolaris.nfsu2.repository: "https://github.com/trpolaris/NFSU2-Web.git"
    network_mode: host
    # The HTTP listener intentionally binds to 127.0.0.1 inside the host network namespace.
    # Do not use ports: or --bind here; --bind only controls the TLS listener.
    command: ["--data", "/game", "--http-port", "${NFSU2_HTTP_PORT:-9093}"]
    environment:
      NFSU2_HTTP_PORT: "${NFSU2_HTTP_PORT:-9093}"
      NFSU2_DATA: /game
      NFSU2_WASM: /module/nfsu2.wasm
      NFSU2_REPO: /app
      NFSU2_STATE: /state
    volumes:
      - game-data:/game:ro
      - module-data:/module:ro
      - state:/state
    read_only: true
    cap_drop: [ALL]
    security_opt:
      - no-new-privileges:true
    mem_limit: 4g
    memswap_limit: 4g
    ulimits:
      nofile: 65536
    logging:
      driver: local
volumes:
  game-data:
    name: nfsu2-web-local_game-data
  module-data:
    name: nfsu2-web-local_module-data
  state:
    name: nfsu2-web-local_state
YAML
      ;;
    lan)
      cat > "$COMPOSE" <<'YAML'
name: nfsu2-web-local
services:
  server:
    build:
      context: .
      dockerfile: web/server-rs/Dockerfile
    image: nfsu2-web-rs:local
    restart: unless-stopped
    working_dir: /app
    labels:
      com.trpolaris.nfsu2.project-path: "${NFSU2_PROJECT_DIR:?NFSU2_PROJECT_DIR missing}"
      com.trpolaris.nfsu2.repository: "https://github.com/trpolaris/NFSU2-Web.git"
    ports:
      - "0.0.0.0:${NFSU2_LAN_HTTPS_PORT:-8443}:8443"
    command: ["--data", "/game", "--http-port", "${NFSU2_HTTP_PORT:-9093}", "--port", "8443", "--tls-cert", "/certs/nfsu2.crt", "--tls-key", "/certs/nfsu2.key", "--bind", "0.0.0.0"]
    environment:
      NFSU2_HTTP_PORT: "${NFSU2_HTTP_PORT:-9093}"
      NFSU2_DATA: /game
      NFSU2_WASM: /module/nfsu2.wasm
      NFSU2_REPO: /app
      NFSU2_STATE: /state
      NFSU2_PORT: "8443"
      NFSU2_TLS_CERT: "/certs/nfsu2.crt"
      NFSU2_TLS_KEY: "/certs/nfsu2.key"
      NFSU2_BIND: "0.0.0.0"
    volumes:
      - game-data:/game:ro
      - module-data:/module:ro
      - certs:/certs:ro
      - state:/state
    read_only: true
    cap_drop: [ALL]
    security_opt:
      - no-new-privileges:true
    mem_limit: 4g
    memswap_limit: 4g
    ulimits:
      nofile: 65536
    logging:
      driver: local
volumes:
  game-data:
    name: nfsu2-web-local_game-data
  module-data:
    name: nfsu2-web-local_module-data
  certs:
    name: nfsu2-web-local_certs
  state:
    name: nfsu2-web-local_state
YAML
      ;;
    public)
      cat > "$COMPOSE" <<'YAML'
name: nfsu2-web-local
services:
  server:
    build:
      context: .
      dockerfile: web/server-rs/Dockerfile
    image: nfsu2-web-rs:local
    restart: unless-stopped
    working_dir: /app
    labels:
      com.trpolaris.nfsu2.project-path: "${NFSU2_PROJECT_DIR:?NFSU2_PROJECT_DIR missing}"
      com.trpolaris.nfsu2.repository: "https://github.com/trpolaris/NFSU2-Web.git"
    network_mode: host
    command: ["--data", "/game", "--behind-proxy", "--http-port", "${NFSU2_HTTP_PORT:-9093}", "--host", "${NFSU2_HOST:-*.trycloudflare.com}"]
    environment:
      NFSU2_HTTP_PORT: "${NFSU2_HTTP_PORT:-9093}"
      NFSU2_DATA: /game
      NFSU2_WASM: /module/nfsu2.wasm
      NFSU2_REPO: /app
      NFSU2_STATE: /state
      NFSU2_HOST: "${NFSU2_HOST:-*.trycloudflare.com}"
    volumes:
      - game-data:/game:ro
      - module-data:/module:ro
      - state:/state
    read_only: true
    cap_drop: [ALL]
    security_opt:
      - no-new-privileges:true
    mem_limit: 4g
    memswap_limit: 4g
    ulimits:
      nofile: 65536
    logging:
      driver: local
volumes:
  game-data:
    name: nfsu2-web-local_game-data
  module-data:
    name: nfsu2-web-local_module-data
  state:
    name: nfsu2-web-local_state
YAML
      ;;
  esac
}

assert_not_stopped
stop_owned_tunnel
URL=''
if [[ "$MODE" == lan ]]; then
  command -v openssl >/dev/null 2>&1 || { say 'Installing OpenSSL for LAN certificate generation.'; root_run apt-get -o Dpkg::Use-Pty=0 install -y openssl; }
  mkdir -p "$PROJECT/certs"
  openssl req -x509 -nodes -days 825 -newkey rsa:2048 \
    -keyout "$PROJECT/certs/nfsu2.key" -out "$PROJECT/certs/nfsu2.crt" \
    -subj "/CN=$LAN_IP" -addext "subjectAltName=IP:$LAN_IP" \
    -addext 'basicConstraints=CA:FALSE' -addext 'extendedKeyUsage=serverAuth' >/dev/null 2>&1
  # Keep the source private on the WSL filesystem. A root Alpine one-shot copies it
  # into a Docker named volume and assigns the key to the upstream image's UID 1000.
  # The server never bind-mounts a host private key, avoiding Desktop/WSL UID mapping.
  chown root:root "$PROJECT/certs/nfsu2.crt" "$PROJECT/certs/nfsu2.key"
  chmod 755 "$PROJECT/certs"
  chmod 644 "$PROJECT/certs/nfsu2.crt"
  chmod 600 "$PROJECT/certs/nfsu2.key"
  # Keep the private certificate out of Git even if this checkout's ignore rules are older.
  exclude_file="$PROJECT/.git/info/exclude"
  if [[ -f "$exclude_file" ]] && ! grep -qxF '/certs/' "$exclude_file"; then printf '
/certs/
' >> "$exclude_file"; fi
  seed_tls_volume
  sed -i '/^NFSU2_LAN_HTTPS_PORT=/d' "$ENV_FILE"
  printf 'NFSU2_LAN_HTTPS_PORT=%s\n' "$LAN_PORT" >> "$ENV_FILE"
fi

prepare_runtime_data_volumes

if [[ "$MODE" == public ]]; then
  # First ensure the local origin is running before opening the outbound tunnel.
  MODE=local; write_compose
  compose config >/dev/null || die 'Local Compose validation failed.'
  assert_not_stopped
  # Atomic recovery transaction: once down begins, always try to bring the origin back up.
  compose down --remove-orphans || true
  compose up -d --force-recreate || die 'Could not start local NFSU2 origin.'
  MODE=public
  assert_not_stopped
  CF_BIN="$HOME/.local/bin/cloudflared"
  if [[ ! -x "$CF_BIN" ]]; then
    mkdir -p "$HOME/.local/bin"
    download_with_progress "cloudflared indirme" 20 62 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64' "$CF_BIN" || die 'Could not download cloudflared. Check internet access and retry.'
    chmod 755 "$CF_BIN"
  fi
  assert_not_stopped
  rm -f "$ROOT/logs/cloudflared.stdout.log" "$ROOT/logs/cloudflared.stderr.log" "$ROOT/NFSU2-PUBLIC-URL.txt"
  nohup "$CF_BIN" tunnel --no-autoupdate --url "http://127.0.0.1:$HTTP_PORT" > "$ROOT/logs/cloudflared.stdout.log" 2> "$ROOT/logs/cloudflared.stderr.log" < /dev/null &
  CF_PID=$!
  printf '%s\n' "$CF_PID" > "$ROOT/.nfsu2-cloudflared.pid"
  for _ in $(seq 1 60); do
    URL="$(grep -hEo 'https://[a-zA-Z0-9-]+\.trycloudflare\.com' "$ROOT/logs/cloudflared.stdout.log" "$ROOT/logs/cloudflared.stderr.log" 2>/dev/null | head -n 1 || true)"
    if [[ "$URL" == https://*.trycloudflare.com ]]; then break; fi
    if ! kill -0 "$CF_PID" 2>/dev/null; then
      cat "$ROOT/logs/cloudflared.stderr.log" 2>/dev/null || true
      die 'cloudflared exited before creating its public URL.'
    fi
    sleep 1
  done
  [[ "$URL" == https://*.trycloudflare.com ]] || die 'Timed out waiting for the Cloudflare Quick Tunnel URL; check cloudflared logs.'
  NFSU2_HOST="${URL#https://}"
  export NFSU2_HOST
fi

write_compose
progress 70 'Docker Compose yapılandırması doğrulanıyor'
compose config >/dev/null || die 'Compose validation failed; no container restart was attempted.'
assert_not_stopped
say 'Checking the exact game and WASM files visible inside the Docker runtime before touching the current server.'
compose run --rm --no-deps --entrypoint /bin/sh server -ec \
  'test -s /game/SPEED2.EXE || { echo "SPEED2.EXE is not present in Docker game volume /game" >&2; exit 41; }; \
   test -s /module/nfsu2.wasm || { echo "nfsu2.wasm is not present in Docker module volume /module" >&2; exit 42; }; \
   test -s /module/nfsu2.json || { echo "nfsu2.json is not present in Docker module volume /module" >&2; exit 43; }; \
   echo "Preflight OK: game and module volumes are readable as UID 1000."' \
  || die 'Runtime data preflight failed. Existing container was not stopped; inspect the SPEED2/module diagnostic above.'
assert_not_stopped
progress 80 'Mevcut NFSU2 konteyneri kontrollü biçimde durduruluyor'
# This down/up pair is a non-interruptible safety transaction. Honor stop only after health validation.
STACK_REPLACEMENT_STARTED=1
compose down --remove-orphans || die 'Could not stop existing NFSU2 compose project cleanly.'
progress 88 'Yeni erişim profiliyle NFSU2 başlatılıyor'
compose up -d --force-recreate || die 'Failed to start NFSU2 with the selected access profile.'

if [[ "$MODE" == lan ]]; then
  # Fail early with a precise permission diagnostic instead of probing a TLS socket that cannot start.
  if ! compose exec -T server sh -c 'test -s /certs/nfsu2.crt && test -r /certs/nfsu2.key && test -s /game/SPEED2.EXE'; then
    compose ps -a || true
    compose logs --tail=120 server || true
    die 'LAN TLS certificate/private key is not readable inside the container as UID 1000. Certificate ownership repair failed.'
  fi
  READY=0
  for _ in $(seq 1 30); do
    if curl --noproxy '*' -kfsS --max-time 2 "https://127.0.0.1:$LAN_PORT/game.json" -o /tmp/nfsu2-lan-game-json.json; then READY=1; break; fi
    sleep 1
  done
  [[ "$READY" == 1 ]] || { compose ps || true; compose logs --tail=100 server || true; die 'LAN HTTPS server did not answer /game.json on the selected port.'; }
  URL="https://$LAN_IP:$LAN_PORT/"
  say 'LAN mode uses a self-signed TLS certificate. Other devices may need to trust/import the certificate before the browser treats the site as secure.'
else
  READY=0
  for _ in $(seq 1 30); do
    if curl --noproxy '*' -fsS --max-time 2 "http://127.0.0.1:$HTTP_PORT/game.json" -o /tmp/nfsu2-access-game-json.json; then READY=1; break; fi
    sleep 1
  done
  [[ "$READY" == 1 ]] || { compose ps || true; compose logs --tail=100 server || true; die 'NFSU2 HTTP origin did not answer /game.json after the profile switch.'; }
  if [[ "$MODE" == local ]]; then
    URL="http://localhost:$HTTP_PORT/"
  else
    URL="$(grep -hEo 'https://[a-zA-Z0-9-]+\.trycloudflare\.com' "$ROOT/logs/cloudflared.stdout.log" "$ROOT/logs/cloudflared.stderr.log" 2>/dev/null | head -n 1 || true)"
    [[ "$URL" == https://*.trycloudflare.com ]] || die 'Public URL could not be recovered after Compose restart.'
  fi
fi

assert_not_stopped
progress 97 'HTTP sağlık kontrolü başarılı; erişim kaydı yazılıyor'
printf '%s\n' "$MODE" > "$ROOT/nfsu2-access-mode.txt"
printf '%s\n' "$URL" > "$ROOT/nfsu2-access-url.txt"
if [[ "$MODE" == lan ]]; then printf '%s\n' "$LAN_PORT" > "$ROOT/NFSU2-LAN-PORT.txt"; else rm -f "$ROOT/NFSU2-LAN-PORT.txt"; fi
if [[ "$MODE" == public ]]; then printf '%s\n' "$URL" > "$ROOT/NFSU2-PUBLIC-URL.txt"; else rm -f "$ROOT/NFSU2-PUBLIC-URL.txt"; fi
PROFILE_COMMITTED=1
say "Access profile is active: $MODE"
echo "ACCESS_MODE=$MODE"
echo "ACCESS_URL=$URL"
echo 'ACCESS_STATUS=OK'
progress 100 'Erişim profili tamamlandı'
