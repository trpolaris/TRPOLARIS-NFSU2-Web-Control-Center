#!/usr/bin/env bash
# File-based resolver: do not pass this multi-line program through bash -c/Windows argv.
# Runs read-only as root, using the real normal user's HOME from the installer report.
set -Eeuo pipefail
shopt -s nullglob

uid_now="$(id -u 2>/dev/null || printf 'unknown')"
home_now="${NFSU2_DEFAULT_HOME:-${HOME:-}}"
if [[ -z "$home_now" || "$home_now" == "/root" ]]; then
  home_now="$(getent passwd 1000 2>/dev/null | cut -d: -f6 || true)"
fi
expected="${NFSU2_EXPECTED_PROJECT_DIR:-}"
printf 'NFSU2_LOOKUP_START uid=%s home=%s expected=%s\n' "$uid_now" "${home_now:-<empty>}" "${expected:-<none>}"

candidates=()
add_candidate() {
  local value="${1:-}"
  [[ -n "$value" ]] || return 0
  candidates+=("$value")
}

normalize_remote() {
  local value="${1:-}"
  value="${value,,}"
  value="${value#https://github.com/}"
  value="${value#http://github.com/}"
  value="${value#ssh://git@github.com/}"
  value="${value#git://github.com/}"
  value="${value#git@github.com:}"
  value="${value#github.com:}"
  value="${value%/}"
  value="${value%.git}"
  value="${value%/}"
  printf '%s' "$value"
}

read_origin() {
  local dir="$1" value="" git_path="$1/.git" config_path="" git_dir=""
  if command -v git >/dev/null 2>&1; then
    value="$(git -c safe.directory="$dir" -C "$dir" config --get remote.origin.url 2>/dev/null || true)"
  fi
  if [[ -n "$value" ]]; then printf '%s' "$value"; return 0; fi

  if [[ -d "$git_path" ]]; then
    config_path="$git_path/config"
  elif [[ -f "$git_path" ]]; then
    git_dir="$(sed -n 's/^gitdir:[[:space:]]*//p' "$git_path" 2>/dev/null | head -n 1 || true)"
    [[ -n "$git_dir" ]] || return 0
    [[ "$git_dir" == /* ]] || git_dir="$dir/$git_dir"
    config_path="$git_dir/config"
  else
    return 0
  fi
  [[ -f "$config_path" ]] || return 0

  if command -v python3 >/dev/null 2>&1; then
    value="$(python3 - "$config_path" <<'PYGITCFG' 2>/dev/null || true
import configparser, sys
p = configparser.ConfigParser(interpolation=None)
try:
    p.read(sys.argv[1], encoding='utf-8')
    print(p.get('remote "origin"', 'url', fallback=''))
except Exception:
    pass
PYGITCFG
)"
  fi
  printf '%s' "$value"
}

validate_candidate() {
  local raw="$1" d="" origin="" normalized="" missing="" owner_uid="" owner="" owner_home=""
  [[ -n "$raw" ]] || return 1
  d="$(realpath -e -- "$raw" 2>/dev/null || printf '%s' "$raw")"
  if [[ ! -d "$d" ]]; then
    printf 'NFSU2_CANDIDATE_REJECT path=%s reason=directory-missing\n' "$d"
    return 1
  fi
  if [[ ! -e "$d/.git" ]]; then
    printf 'NFSU2_CANDIDATE_REJECT path=%s reason=.git-missing\n' "$d"
    return 1
  fi
  origin="$(read_origin "$d")"
  normalized="$(normalize_remote "$origin")"
  [[ -f "$d/compose.local.yml" ]] || missing+=" compose.local.yml"
  [[ -f "$d/.env.local" ]] || missing+=" .env.local"
  [[ -f "$d/web/server-rs/Dockerfile" ]] || missing+=" web/server-rs/Dockerfile"
  [[ -f "$d/web/server-rs/src/main.rs" ]] || missing+=" web/server-rs/src/main.rs"
  [[ -f "$d/web/server-rs/Cargo.lock" ]] || missing+=" web/server-rs/Cargo.lock"
  [[ -f "$d/web/package.json" && -f "$d/web/package-lock.json" ]] || missing+=" web/package.json/package-lock.json"
  [[ -f "$d/web/gen.sh" && -f "$d/web/build.sh" && -f "$d/requirements.txt" ]] || missing+=" WASM/Python build files"

  if [[ "$normalized" != "trpolaris/nfsu2-web" ]]; then
    printf 'NFSU2_CANDIDATE_REJECT path=%s reason=remote-mismatch remote=%s normalized=%s missing=%s\n' \
      "$d" "${origin:-<missing>}" "${normalized:-<missing>}" "${missing:-none}"
    return 1
  fi
  if [[ -n "$missing" ]]; then
    printf 'NFSU2_CANDIDATE_REJECT path=%s reason=required-files-missing files=%s remote=%s\n' "$d" "$missing" "$origin"
    return 1
  fi

  owner_uid="$(stat -c '%u' -- "$d" 2>/dev/null || true)"
  owner="$(getent passwd "$owner_uid" 2>/dev/null | cut -d: -f1 || true)"
  [[ -n "$owner" ]] || owner="$(stat -c '%U' -- "$d" 2>/dev/null || id -un)"
  owner_home="$(getent passwd "$owner_uid" 2>/dev/null | cut -d: -f6 || true)"
  # The installer may run the final chmod/chown step as root. When the verified
  # checkout sits inside the real default user's HOME, keep that user as the
  # project owner instead of incorrectly persisting /root as its home.
  if [[ -n "$home_now" && "$d" == "$home_now/"* ]]; then
    owner="${home_now##*/}"
    owner_uid="$(getent passwd "$owner" 2>/dev/null | cut -d: -f3 || printf '%s' "$owner_uid")"
    owner_home="$home_now"
  fi
  if [[ -z "$owner_home" ]]; then
    case "$d" in
      /home/*) local relative="${d#/home/}"; owner="${relative%%/*}"; owner_home="/home/$owner" ;;
      /root/*|/root) owner=root; owner_home=/root ;;
      *) owner_home="${home_now:-${d%/*}}" ;;
    esac
  fi

  local project_b64 home_b64 owner_b64 uid_b64
  project_b64="$(printf '%s' "$d" | base64 -w0)"
  home_b64="$(printf '%s' "$owner_home" | base64 -w0)"
  owner_b64="$(printf '%s' "$owner" | base64 -w0)"
  uid_b64="$(printf '%s' "${owner_uid:-1000}" | base64 -w0)"
  printf 'NFSU2_PROJECT_B64=%s.%s.%s.%s\n' "$project_b64" "$home_b64" "$owner_b64" "$uid_b64"
  printf 'NFSU2_PROJECT_VALIDATED path=%s remote=%s owner=%s uid=%s\n' "$d" "$origin" "$owner" "${owner_uid:-unknown}"
  return 0
}

# Read both the compact path manifest and the human-readable installer report.
manifest="${NFSU2_PROJECT_METADATA_FILE:-${NFSU2_CONTROL_ROOT:-/mnt/c/nfsu2}/NFSU2-PROJECT-PATH.txt}"
for report in "$manifest" "${NFSU2_SETUP_REPORT_FILE:-${NFSU2_CONTROL_ROOT:-/mnt/c/nfsu2}/NFSU2-SETUP-REPORT.txt}"; do
  [[ -f "$report" ]] || continue
  report_project="$(sed -n 's/^Source folder (WSL):[[:space:]]*//p' "$report" | head -n 1 || true)"
  add_candidate "$report_project"
done
add_candidate "$expected"
add_candidate "${NFSU2_PROJECT_DIR:-}"
[[ -n "$home_now" ]] && add_candidate "$home_now/nfsu2-web-trpolaris"
[[ -n "${HOME:-}" ]] && add_candidate "$HOME/nfsu2-web-trpolaris"
for item in /home/*/nfsu2-web-trpolaris /root/nfsu2-web-trpolaris /root/projects/nfsu2-web-trpolaris \
            /root/workspace/nfsu2-web-trpolaris /opt/nfsu2-web-trpolaris /srv/nfsu2-web-trpolaris; do
  add_candidate "$item"
done

declare -A seen=()
for raw_dir in "${candidates[@]}"; do
  d="$(realpath -e -- "$raw_dir" 2>/dev/null || printf '%s' "$raw_dir")"
  [[ -n "$d" && -z "${seen[$d]+x}" ]] || continue
  seen[$d]=1
  if validate_candidate "$d"; then exit 0; fi
done

# Docker labels are read-only fallbacks; they are never required for a successful direct-path lookup.
if command -v docker >/dev/null 2>&1; then
  ids="$(timeout 8s docker ps -aq --filter label=com.docker.compose.project=nfsu2-web-local \
      --filter label=com.docker.compose.service=server 2>/dev/null || true)"
  while IFS= read -r cid; do
    [[ -n "$cid" ]] || continue
    image="$(timeout 4s docker inspect --format '{{.Config.Image}}' "$cid" 2>/dev/null || true)"
    [[ "$image" == "nfsu2-web-rs:local" ]] || continue
    project_label="$(timeout 4s docker inspect --format '{{index .Config.Labels "com.trpolaris.nfsu2.project-path"}}' "$cid" 2>/dev/null || true)"
    compose_dir="$(timeout 4s docker inspect --format '{{index .Config.Labels "com.docker.compose.project.working_dir"}}' "$cid" 2>/dev/null || true)"
    config_files="$(timeout 4s docker inspect --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}' "$cid" 2>/dev/null || true)"
    add_candidate "$project_label"
    add_candidate "$compose_dir"
    if [[ -n "$config_files" && "$config_files" != '<no value>' ]]; then add_candidate "$(dirname "${config_files%%,*}")"; fi
    printf 'NFSU2_DOCKER_LABEL container=%s image=%s project_path=%s compose_dir=%s\n' \
      "$cid" "$image" "${project_label:-<empty>}" "${compose_dir:-<empty>}"
  done <<< "$ids"
fi

for raw_dir in "${candidates[@]}"; do
  d="$(realpath -e -- "$raw_dir" 2>/dev/null || printf '%s' "$raw_dir")"
  [[ -n "$d" && -z "${seen[$d]+x}" ]] || continue
  seen[$d]=1
  if validate_candidate "$d"; then exit 0; fi
done

# Last resort is restricted to Linux source directories; never scan the game folder.
for base in /home /root /opt /srv "${NFSU2_CONTROL_ROOT:-/mnt/c/nfsu2}/source"; do
  [[ -d "$base" ]] || continue
  while IFS= read -r -d '' gitdir; do
    if [[ "${gitdir##*/}" == ".git" ]]; then add_candidate "${gitdir%/.git}"; fi
  done < <(find "$base" -xdev -maxdepth 7 \
      \( -type d -name .git -print0 -prune \) -o \
      \( -type d \( -name .cache -o -name .cargo -o -name .rustup -o -name .npm -o -name .local \
          -o -name .config -o -name node_modules -o -name .venv -o -name venv -o -name target \
          -o -name out -o -name gen -o -name dist -o -name build -o -name coverage \) -prune \) -o \
      \( -type f -name .git -print0 \) 2>/dev/null)
done

for raw_dir in "${candidates[@]}"; do
  d="$(realpath -e -- "$raw_dir" 2>/dev/null || printf '%s' "$raw_dir")"
  [[ -n "$d" && -z "${seen[$d]+x}" ]] || continue
  seen[$d]=1
  if validate_candidate "$d"; then exit 0; fi
done

printf 'NFSU2_PROJECT_LOOKUP_FAILED: Ubuntu ran the file-based resolver, but no valid checkout was found. Checked setup report, HOME, expected path, Docker Compose labels and bounded Linux source scans.\n'
exit 17
