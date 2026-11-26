#!/usr/bin/env bash
# Refresh the agent skill packs described by sources.json.
#
# Vendored packs (Duende, .NET) are downloaded from codeload.github.com at the
# recorded commit and copied into .opencode/skills/<pack>. The Aspire pack is a
# catalog URL in .opencode/opencode.json, so updating it re-pins that URL.
#
# Usage:
#   sync-agent-skills.sh [--check | --update] [--latest]
#                        [--pack <name>]... [--ref <pack>=<sha>]...
#
# See README.md in this directory for the provenance table and the update
# procedure.

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
MANIFEST="$SCRIPT_DIR/sources.json"
CONFIG="$REPO_ROOT/.opencode/opencode.json"

MODE="update"
LATEST=0
declare -a PACKS=()
declare -A REF_OVERRIDES=()

die() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

info() {
  printf '%s\n' "$*"
}

usage() {
  cat <<'EOF'
Usage: sync-agent-skills.sh [options]

Refresh the vendored agent skill packs and the pinned Aspire catalog.

Options:
  --check                  Compare installed content and the Aspire catalog URL
                           with sources.json; exit non-zero on drift.
  --update                 Fetch and replace the vendored packs, recompute the
                           content hashes, and re-pin the Aspire catalog URL.
                           This is the default.
  --latest                 Resolve each selected pack's upstream main branch
                           through api.github.com before updating.
  --pack <name>            Limit the operation to one pack; repeatable.
                           Names come from sources.json (duende, dotnet,
                           aspire).
  --ref <pack>=<sha>       Use an explicit 40-character commit SHA for a pack;
                           repeatable. The new SHA is written to sources.json.
  -h, --help               Show this help.

Examples:
  sync-agent-skills.sh --check
  sync-agent-skills.sh --pack duende
  sync-agent-skills.sh --pack dotnet --latest
  sync-agent-skills.sh --pack aspire --ref aspire=<sha>
EOF
}

parse_ref_override() {
  local value="$1" pack ref
  [[ "$value" == *=* ]] || die "--ref expects <pack>=<sha>, got '$value'"
  pack="${value%%=*}"
  ref="${value#*=}"
  [ -n "$pack" ] || die "--ref is missing a pack name"
  [[ "$ref" =~ ^[0-9a-f]{40}$ ]] \
    || die "ref for '$pack' must be a 40-character lowercase commit SHA"
  REF_OVERRIDES["$pack"]="$ref"
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --check)
      MODE="check"
      shift
      ;;
    --update)
      MODE="update"
      shift
      ;;
    --latest)
      LATEST=1
      shift
      ;;
    --pack)
      [ "$#" -ge 2 ] || die "--pack requires a pack name"
      PACKS+=("$2")
      shift 2
      ;;
    --pack=*)
      PACKS+=("${1#*=}")
      shift
      ;;
    --ref)
      [ "$#" -ge 2 ] || die "--ref requires <pack>=<sha>"
      parse_ref_override "$2"
      shift 2
      ;;
    --ref=*)
      parse_ref_override "${1#*=}"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    --)
      shift
      break
      ;;
    *)
      die "unknown argument: $1 (try --help)"
      ;;
  esac
done

[ -f "$MANIFEST" ] || die "manifest not found: $MANIFEST"

if [ "$MODE" = "check" ] && { [ "$LATEST" -eq 1 ] || [ "${#REF_OVERRIDES[@]}" -gt 0 ]; }; then
  die "--check cannot be combined with --latest or --ref"
fi

# Print the pack names in manifest order.
pack_names() {
  python3 - "$MANIFEST" <<'PY'
import json, sys

with open(sys.argv[1], encoding="utf-8") as handle:
    data = json.load(handle)

for name in data["packs"]:
    print(name)
PY
}

# Print one manifest field. Arrays are printed one item per line.
get_field() {
  local pack="$1" field="$2"
  python3 - "$MANIFEST" "$pack" "$field" <<'PY'
import json, sys

path, pack, field = sys.argv[1:4]
with open(path, encoding="utf-8") as handle:
    data = json.load(handle)

value = data["packs"][pack].get(field)
if isinstance(value, list):
    print("\n".join(value))
elif value is None:
    print("")
else:
    print(value)
PY
}

# Write one manifest field, preserving the manifest's two-space JSON layout.
set_field() {
  local pack="$1" field="$2" value="$3"
  python3 - "$MANIFEST" "$pack" "$field" "$value" <<'PY'
import json, sys

path, pack, field, value = sys.argv[1:5]
with open(path, encoding="utf-8") as handle:
    data = json.load(handle)

data["packs"][pack][field] = value
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(data, handle, indent=2, ensure_ascii=False)
    handle.write("\n")
PY
}

# SHA-256 over the sorted relative path and content of every file in a pack.
sha_of_dir() {
  python3 - "$1" <<'PY'
import hashlib, os, sys

root = sys.argv[1]
digest = hashlib.sha256()

for directory, dirnames, filenames in os.walk(root):
    dirnames.sort()
    for name in sorted(filenames):
        path = os.path.join(directory, name)
        relative = os.path.relpath(path, root).replace(os.sep, "/")
        digest.update(relative.encode("utf-8"))
        digest.update(b"\0")
        if os.path.islink(path):
            digest.update(b"link:")
            digest.update(os.readlink(path).encode("utf-8"))
        else:
            with open(path, "rb") as handle:
                for chunk in iter(lambda: handle.read(1 << 20), b""):
                    digest.update(chunk)
        digest.update(b"\0")

print(digest.hexdigest())
PY
}

catalog_url() {
  local repo="$1" ref="$2" catalog="${3:-opencode/v2}"
  printf '%s' "https://raw.githubusercontent.com/$repo/$ref/${catalog%/}/"
}

resolve_latest() {
  local repo="$1" response
  response="$(curl -fsSL --retry 3 --retry-delay 2 \
    -H 'Accept: application/vnd.github+json' \
    "https://api.github.com/repos/$repo/commits/main")" \
    || die "failed to resolve the latest commit for $repo (api.github.com)"
  python3 -c 'import json, sys; print(json.load(sys.stdin)["sha"])' <<<"$response"
}

# Validate the requested pack names against the manifest.
available="$(pack_names)"
declare -a SELECTED=()
if [ "${#PACKS[@]}" -eq 0 ]; then
  while IFS= read -r name; do
    [ -n "$name" ] && SELECTED+=("$name")
  done <<<"$available"
else
  for requested in "${PACKS[@]}"; do
    [ -n "$requested" ] || die "--pack requires a non-empty name"
    grep -Fxq "$requested" <<<"$available" \
      || die "unknown pack '$requested'; available: $(tr '\n' ' ' <<<"$available")"
    duplicate=0
    for existing in "${SELECTED[@]:-}"; do
      [ "$existing" = "$requested" ] && duplicate=1
    done
    [ "$duplicate" -eq 0 ] && SELECTED+=("$requested")
  done
fi

[ "${#SELECTED[@]}" -gt 0 ] || die "no packs selected"

if [ "${#REF_OVERRIDES[@]}" -gt 0 ]; then
  for pack in "${!REF_OVERRIDES[@]}"; do
    grep -Fxq "$pack" <<<"$available" || die "unknown pack '$pack' in --ref"
    found=0
    for selected in "${SELECTED[@]}"; do
      [ "$selected" = "$pack" ] && found=1
    done
    if [ "$found" -eq 0 ]; then
      if [ "${#PACKS[@]}" -gt 0 ]; then
        die "--ref names '$pack', which is not selected by --pack"
      fi
      SELECTED+=("$pack")
    fi
  done
fi

check_catalog_url() {
  local expected="$1"
  python3 - "$CONFIG" "$expected" <<'PY'
import json, sys

path, expected = sys.argv[1], sys.argv[2]

try:
    with open(path, encoding="utf-8") as handle:
        config = json.load(handle)
except FileNotFoundError:
    print(f"DRIFT catalog: {path} not found")
    sys.exit(1)
except json.JSONDecodeError as error:
    print(f"DRIFT catalog: invalid JSON in {path}: {error}")
    sys.exit(1)

skills = config.get("skills")
if not isinstance(skills, list):
    print('DRIFT catalog: OpenCode config has no "skills" array')
    sys.exit(1)

aspire = [
    entry for entry in skills
    if isinstance(entry, str) and "microsoft/aspire-skills/" in entry
]

if not aspire:
    print("DRIFT catalog: Aspire catalog URL is missing from .opencode/opencode.json")
    sys.exit(1)
if len(aspire) > 1:
    print("DRIFT catalog: multiple Aspire catalog entries: " + ", ".join(aspire))
    sys.exit(1)
if aspire[0] != expected:
    print(f"DRIFT catalog: expected {expected}, found {aspire[0]}")
    sys.exit(1)

print(f"ok catalog: {expected}")
PY
}

# Replace the Aspire catalog URL in opencode.json without reformatting the rest
# of the file.
rewrite_catalog_url() {
  local url="$1"
  python3 - "$CONFIG" "$url" <<'PY'
import json, re, sys

path, url = sys.argv[1], sys.argv[2]

try:
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
except FileNotFoundError:
    sys.exit(f"{path} not found")

try:
    config = json.loads(text)
except json.JSONDecodeError as error:
    sys.exit(f"{path}: invalid JSON: {error}")

skills = config.get("skills")
if skills is not None and not isinstance(skills, list):
    sys.exit(f'{path}: "skills" must be an array')

aspire_re = re.compile(
    r"https://raw\.githubusercontent\.com/microsoft/aspire-skills/"
    r"[0-9a-f]{40}/opencode/v[0-9]+/?"
)
matches = [entry for entry in (skills or []) if isinstance(entry, str) and "microsoft/aspire-skills/" in entry]

if len(matches) > 1:
    sys.exit(f"{path}: multiple Aspire catalog entries; remove the extras first")

if matches:
    text = aspire_re.sub(lambda _match: url, text, count=1)
elif skills is not None:
    match = re.search(r'"skills"\s*:\s*\[', text)
    if not match:
        sys.exit(f'{path}: could not locate the "skills" array')
    start = match.end()
    cursor = start
    while cursor < len(text) and text[cursor] in " \t\r\n":
        cursor += 1
    if cursor < len(text) and text[cursor] == "]":
        text = text[:start] + json.dumps(url) + text[cursor:]
    else:
        whitespace = text[start:cursor]
        if "\n" in whitespace:
            indent = whitespace.rsplit("\n", 1)[1]
            text = text[:start] + "\n" + indent + json.dumps(url) + "," + text[start:]
        else:
            text = text[:cursor] + json.dumps(url) + ", " + text[cursor:]
else:
    match = re.match(r"\s*\{", text)
    if not match:
        sys.exit(f"{path}: could not locate the root object")
    insert_at = match.end()
    remainder = text[insert_at:]
    if remainder.lstrip().startswith("}"):
        text = text[:insert_at] + '\n  "skills": [' + json.dumps(url) + "]\n" + remainder.lstrip()
    else:
        text = text[:insert_at] + '\n  "skills": [' + json.dumps(url) + "]," + remainder

try:
    rewritten = json.loads(text)
except json.JSONDecodeError as error:
    sys.exit(f"{path}: rewrite produced invalid JSON: {error}")

if url not in rewritten.get("skills", []):
    sys.exit(f"{path}: rewrite did not add the Aspire catalog URL")

with open(path, "w", encoding="utf-8", newline="\n") as handle:
    handle.write(text)
PY
}

if [ "$MODE" = "check" ]; then
  status=0

  for pack in "${SELECTED[@]}"; do
    mode="$(get_field "$pack" mode)"
    if [ "$mode" = "vendor" ]; then
      expected="$(get_field "$pack" hash)"
      if [ ! -d "$SCRIPT_DIR/$pack" ]; then
        info "DRIFT $pack: installed directory is missing"
        status=1
        continue
      fi
      if [ -z "$expected" ]; then
        info "DRIFT $pack: sources.json has no content hash (run --update)"
        status=1
        continue
      fi
      actual="sha256:$(sha_of_dir "$SCRIPT_DIR/$pack")"
      if [ "$expected" != "$actual" ]; then
        info "DRIFT $pack: content hash mismatch (expected $expected, found $actual)"
        status=1
        continue
      fi
      info "ok $pack: $actual"
    elif [ "$mode" = "catalog" ]; then
      ref="$(get_field "$pack" ref)"
      catalog="$(get_field "$pack" catalog)"
      recorded_url="$(get_field "$pack" url)"
      derived_url="$(catalog_url "$(get_field "$pack" repo)" "$ref" "$catalog")"
      if [ "$recorded_url" != "$derived_url" ]; then
        info "DRIFT $pack: sources.json URL does not match its ref ($recorded_url != $derived_url)"
        status=1
        continue
      fi
      check_catalog_url "$recorded_url" || status=1
    else
      info "DRIFT $pack: unknown mode '$mode'"
      status=1
    fi
  done

  if [ "$status" -ne 0 ]; then
    exit 1
  fi
  exit 0
fi

# Update mode. Stage every vendor pack before replacing any installed copy so a
# failed download leaves the working tree untouched.
#
# The work directory lives beside the packs so the old and new trees are on the
# same filesystem and can be swapped with rename(2). A running OpenCode may be
# scanning a pack at the same time, so deleting the old tree in place can fail
# with "Directory not empty"; renaming it out of the way first avoids that race.
WORK="$(mktemp -d "$SCRIPT_DIR/.sync.XXXXXX")" \
  || die "could not create a work directory under $SCRIPT_DIR"
declare -a BACKUPS=()

remove_tree() {
  local target="$1" attempt
  [ -e "$target" ] || return 0
  for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if rm -rf "$target" 2>/dev/null; then
      return 0
    fi
    sleep 0.2
  done
  rm -rf "$target" 2>/dev/null || info "warning: could not remove temporary tree $target"
}

cleanup() {
  local backup
  for backup in "${BACKUPS[@]:-}"; do
    [ -n "$backup" ] && remove_tree "$backup"
  done
  remove_tree "$WORK"
}
trap cleanup EXIT

declare -A REFS=()
declare -A HASHES=()

for pack in "${SELECTED[@]}"; do
  mode="$(get_field "$pack" mode)"
  repo="$(get_field "$pack" repo)"
  if [ "${REF_OVERRIDES[$pack]+set}" = "set" ]; then
    ref="${REF_OVERRIDES[$pack]}"
  elif [ "$LATEST" -eq 1 ]; then
    info "Resolving latest $repo"
    ref="$(resolve_latest "$repo")"
  else
    ref="$(get_field "$pack" ref)"
  fi
  [ -n "$ref" ] || die "$pack: no ref recorded"
  REFS["$pack"]="$ref"

  if [ "$mode" = "vendor" ]; then
    license="$(get_field "$pack" license)"
    archive="$WORK/$pack.tar.gz"
    unpack="$WORK/unpack-$pack"
    stage="$WORK/stage-$pack"
    mkdir -p "$unpack"
    info "Fetching $repo@$ref"
    curl -fsSL --retry 3 --retry-delay 2 -o "$archive" \
      "https://codeload.github.com/$repo/tar.gz/$ref" \
      || die "failed to download $repo@$ref"
    tar -xzf "$archive" -C "$unpack" || die "failed to extract $repo@$ref"
    src="$(find "$unpack" -mindepth 1 -maxdepth 1 -type d | head -n 1)"
    [ -n "$src" ] || die "no source directory in the $repo@$ref archive"

    mkdir -p "$stage"
    while IFS= read -r include; do
      [ -n "$include" ] || continue
      [ -d "$src/$include" ] || die "$pack: upstream include path not found: $include"
      cp -a "$src/$include/." "$stage/"
    done < <(get_field "$pack" include)

    if [ -n "$license" ]; then
      [ -f "$src/$license" ] || die "$pack: upstream license not found: $license"
      cp -a "$src/$license" "$stage/LICENSE"
    fi

    [ -n "$(find "$stage" -type f -name SKILL.md -print -quit)" ] \
      || die "$pack: staged content contains no SKILL.md"

    HASHES["$pack"]="sha256:$(sha_of_dir "$stage")"
  elif [ "$mode" != "catalog" ]; then
    die "$pack: unknown mode '$mode'"
  fi
done

for pack in "${SELECTED[@]}"; do
  mode="$(get_field "$pack" mode)"
  ref="${REFS[$pack]}"
  old_ref="$(get_field "$pack" ref)"

  if [ "$mode" = "vendor" ]; then
    destination="$SCRIPT_DIR/$pack"
    backup="$WORK/old-$pack"
    if [ -e "$destination" ]; then
      mv "$destination" "$backup" \
        || die "$pack: could not move the existing pack aside"
      BACKUPS+=("$backup")
    fi
    if ! mv "$WORK/stage-$pack" "$destination"; then
      if [ -d "$backup" ]; then
        mv "$backup" "$destination" 2>/dev/null || true
      fi
      die "$pack: could not install the staged pack"
    fi
    set_field "$pack" ref "$ref"
    set_field "$pack" hash "${HASHES[$pack]}"
    if [ "$old_ref" != "$ref" ]; then
      info "Updated $pack: $old_ref -> $ref (${HASHES[$pack]})"
    else
      info "Updated $pack at $ref (${HASHES[$pack]})"
    fi
  else
    catalog="$(get_field "$pack" catalog)"
    url="$(catalog_url "$(get_field "$pack" repo)" "$ref" "$catalog")"
    set_field "$pack" ref "$ref"
    set_field "$pack" url "$url"
    rewrite_catalog_url "$url"
    if [ "$old_ref" != "$ref" ]; then
      info "Updated $pack: $old_ref -> $ref ($url)"
    else
      info "Updated $pack catalog URL ($url)"
    fi
  fi
done

if git -C "$REPO_ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  info ""
  git -C "$REPO_ROOT" --no-pager diff --stat -- .opencode/skills .opencode/opencode.json || true
fi
