#!/usr/bin/env bash
set -euo pipefail

# Simple regression harness to compare current Stunstick decompile output
# against existing reference outputs under ~/Documents/decompiled models/.
# Requires: dotnet SDK, diff(1).

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
REF_ROOT="${HOME}/Documents/decompiled models"
CLI=(dotnet run --project "${ROOT}/src/Stunstick.Cli" --)

usage() {
  cat <<'EOF'
Usage: decompile_regress.sh [--reference-root directory] [--subset pattern] [--keep-temp]

  --reference-root directory   Compare against this reference corpus.
  --subset pattern             Only run models whose path contains pattern (case-insensitive).
  --keep-temp                  Retain temp outputs and print their location.

Default reference corpus: ~/Documents/decompiled models/
Outputs will be compared to any folder containing manifest.json (Crowbar-style layout).
EOF
}

subset=""
keep_temp=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --reference-root|--subset)
      if [[ $# -lt 2 || -z "$2" ]]; then
        echo "Missing value for $1" >&2
        exit 1
      fi
      if [[ "$1" == "--reference-root" ]]; then
        REF_ROOT="$2"
      else
        subset="$2"
      fi
      shift 2 ;;
    --keep-temp) keep_temp=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ ! -d "$REF_ROOT" ]]; then
  echo "Reference root not found: $REF_ROOT" >&2
  exit 1
fi

refs=()
while IFS= read -r -d '' manifest; do
  model_dir="$(dirname "$manifest")"
  if [[ -n "$subset" ]]; then
    shopt -s nocasematch
    [[ "$model_dir" == *"$subset"* ]] || continue
    shopt -u nocasematch
  fi
  refs+=("$model_dir")
done < <(find "$REF_ROOT" -name manifest.json -print0)

if [[ ${#refs[@]} -eq 0 ]]; then
  echo "No reference models found (manifest.json) under $REF_ROOT" >&2
  exit 1
fi

pass=0
fail=0
TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/stunstick-decompile-regress.XXXXXX")"
if [[ $keep_temp -eq 1 ]]; then
  echo "Temporary outputs: $TMP_ROOT"
else
  trap 'rm -rf "$TMP_ROOT"' EXIT
fi

for ref in "${refs[@]}"; do
  mdl="$(python3 - "$ref/manifest.json" <<'PY'
import json,sys
try:
    with open(sys.argv[1], 'r', encoding='utf-8') as f:
        data=json.load(f)
    mdl=data.get('SourceMdlPath') or ''
    if mdl:
        print(mdl)
except Exception:
    pass
PY
)"
  if [[ -z "$mdl" || ! -f "$mdl" || ! -r "$mdl" ]]; then
    echo "[FAIL] Missing or unreadable SourceMdlPath in $ref/manifest.json"
    fail=$((fail+1))
    continue
  fi

  name="$(basename "$ref")"
  out="$(mktemp -d "$TMP_ROOT/case.XXXXXX")"

  model_base="$(basename "$mdl")"
  model_stem="${model_base%.mdl}"

  shopt -s nullglob
  want_qc=0; [[ -f "$ref/model.qc" ]] && want_qc=1
  want_lowercase_qc=0; if [[ -f "$ref/model.qc" ]] && grep -q '^\$modelname' "$ref/model.qc"; then want_lowercase_qc=1; fi
  want_ref_smds=0; ref_smds=( "$ref"/ref_bodypart*_lod0.smd "$ref"/*_ref_bodypart*_lod0.smd ); (( ${#ref_smds[@]} > 0 )) && want_ref_smds=1
  want_lods=0; lod_smds=( "$ref"/ref_bodypart*_lod[1-9]*.smd "$ref"/*_ref_bodypart*_lod[1-9]*.smd ); (( ${#lod_smds[@]} > 0 )) && want_lods=1
  want_physics=0; [[ -f "$ref/physics.smd" ]] && want_physics=1
  want_anims=0; anim_smds=( "$ref"/*_anim*.smd ); (( ${#anim_smds[@]} > 0 )) && want_anims=1
  want_vrd=0; vrd_files=( "$ref"/*.vrd ); (( ${#vrd_files[@]} > 0 )) && want_vrd=1
  want_vta=0; vta_files=( "$ref"/*.vta ); (( ${#vta_files[@]} > 0 )) && want_vta=1
  want_definebones=0; [[ -f "$ref/model_definebones.qci" ]] && want_definebones=1
  want_bmps=0; bmp_files=( "$ref"/*.bmp ); (( ${#bmp_files[@]} > 0 )) && want_bmps=1
  want_prefix=0; pref_ref=( "$ref"/"${model_stem}"_ref_bodypart*_lod0.smd ); (( ${#pref_ref[@]} > 0 )) && want_prefix=1
  shopt -u nullglob

  args=(decompile --mdl "$mdl" --flat-output --log)
  [[ $want_qc -eq 0 ]] && args+=(--no-qc)
  [[ $want_ref_smds -eq 0 ]] && args+=(--no-ref-smds)
  [[ $want_lods -eq 0 ]] && args+=(--no-lods)
  [[ $want_physics -eq 0 ]] && args+=(--no-physics)
  [[ $want_anims -eq 0 ]] && args+=(--no-anims)
  [[ $want_vrd -eq 0 ]] && args+=(--no-vrd)
  [[ $want_vta -eq 1 ]] && args+=(--vta)
  [[ $want_definebones -eq 0 ]] && args+=(--no-qc-group-qci --no-qc-definebones)
  [[ $want_bmps -eq 0 ]] && args+=(--no-texture-bmps)
  [[ $want_prefix -eq 1 ]] && args+=(--prefix-mesh-with-model-name)
  [[ $want_lowercase_qc -eq 1 ]] && args+=(--lowercase-qc)

  if [[ $want_physics -eq 1 && -f "$ref/physics.smd" ]]; then
    # Match Crowbar's indentation style for physics triangles.
    phy_first_line="$(awk 'found && NF { print; exit } /^triangles/{ found=1 }' "$ref/physics.smd")"
    if [[ "$phy_first_line" == "phy" ]]; then
      args+=(--no-physics-indent)
    fi
  fi

  echo "[RUN ] $name ($out)"
  if ! "${CLI[@]}" "${args[@]}" --out "$out" >"$out/decompile-command.log" 2>&1; then
    echo "[FAIL] decompile error for $name"
    cat "$out/decompile-command.log"
    fail=$((fail+1))
    continue
  fi

  if diff -qr -x manifest.json -x original -x "*.log" "$ref" "$out" >/dev/null; then
    echo "[PASS] $name"
    pass=$((pass+1))
  else
    # Retry with the other UV convention used by the reference corpus.
    retry="$(mktemp -d "$TMP_ROOT/flipped-uv.XXXXXX")"
    if "${CLI[@]}" "${args[@]}" --out "$retry" --non-valve-uv >"$retry/decompile-command.log" 2>&1 &&
      diff -qr -x manifest.json -x original -x "*.log" "$ref" "$retry" >/dev/null; then
      echo "[PASS] $name (uv flipped)"
      pass=$((pass+1))
      continue
    fi

    echo "[DIFF] $name"
    fail=$((fail+1))
    # Allow the loop to continue even when differences are found.
    diff -qr -x manifest.json -x original -x "*.log" "$ref" "$out" | sed 's/^/  /' || true
    echo "  (retry with --non-valve-uv did not match)"
  fi
done

echo "Done. pass=$pass fail=$fail"

exit $((fail>0))
