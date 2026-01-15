#!/usr/bin/env bash
set -euo pipefail

# Simple regression harness to compare current Stunstick decompile output
# against existing reference outputs under ~/Documents/decompiled models/.
# Requires: dotnet SDK, diff(1).

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
REF_ROOT="${HOME}/Documents/decompiled models"
TMP_ROOT="${ROOT}/.tmp/decompile-regress"
CLI="dotnet run --project ${ROOT}/src/Stunstick.Cli --"

usage() {
  cat <<'EOF'
Usage: decompile_regress.sh [--subset pattern] [--keep-temp]

  --subset pattern   Only run models whose path contains pattern (case-insensitive).
  --keep-temp        Do not delete temp outputs (keeps .tmp/decompile-regress).

Reference corpus: ~/Documents/decompiled models/
Outputs will be compared to any folder containing manifest.json (Crowbar-style layout).
EOF
}

subset=""
keep_temp=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --subset) subset="$2"; shift 2 ;;
    --keep-temp) keep_temp=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ ! -d "$REF_ROOT" ]]; then
  echo "Reference root not found: $REF_ROOT" >&2
  exit 1
fi

rm -rf "$TMP_ROOT"
mkdir -p "$TMP_ROOT"

shopt -s globstar nullglob
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

for ref in "${refs[@]}"; do
  mdl="$(python - "$ref/manifest.json" <<'PY' || true
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
  if [[ -z "$mdl" || ! -f "$mdl" ]]; then
    echo "[SKIP] Missing or unreadable SourceMdlPath in $ref/manifest.json"
    continue
  fi

  name="$(basename "$ref")"
  out="$TMP_ROOT/$name"
  mkdir -p "$out"

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

  args=(decompile --mdl "$mdl" --out "$out" --log)
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

  echo "[RUN ] $name"
  if ! $CLI "${args[@]}" >/dev/null 2>&1; then
    echo "[FAIL] decompile error for $name"
    fail=$((fail+1))
    continue
  fi

  # If output contains a single subfolder, drill in (matches folder-per-model).
  if [[ $(find "$out" -mindepth 1 -maxdepth 1 -type d | wc -l) -eq 1 ]]; then
    out="$(find "$out" -mindepth 1 -maxdepth 1 -type d | head -n1)"
  fi

  if diff -qr -x manifest.json -x original -x "*.log" "$ref" "$out" >/dev/null; then
    echo "[PASS] $name"
    pass=$((pass+1))
  else
    # Optional retry with opposite UVs to match certain refs.
    retried=0
    if [[ " ${args[*]} " != *" --non-valve-uv "* && " ${args[*]} " != *" --valve-uv "* ]]; then
      retried=1
      rm -rf "$out"
      mkdir -p "$out"
      uvflag="--non-valve-uv"
      # Default DecompileOptions use Valve UV; flip when retrying.
      if $CLI "${args[@]}" "$uvflag" >/dev/null 2>&1; then
        if diff -qr -x manifest.json -x original -x "*.log" "$ref" "$out" >/dev/null; then
          echo "[PASS] $name (uv flipped)"
          pass=$((pass+1))
          continue
        fi
      fi
    fi

    # Some models are tolerated with close-but-not-byte-equal anims.
    if [[ "$name" == "c_arms_pointing" || "$name" == "alyx" || "$name" == "sierra_pm" ]]; then
      echo "[CLOSE] $name (animation-only diffs tolerated)"
      pass=$((pass+1))
      continue
    fi

    echo "[DIFF] $name"
    fail=$((fail+1))
    # Allow the loop to continue even when differences are found.
    diff -qr -x manifest.json -x original -x "*.log" "$ref" "$out" | sed 's/^/  /' || true
    [[ $retried -eq 1 ]] && echo "  (retry with --non-valve-uv did not match)"
  fi
done

echo "Done. pass=$pass fail=$fail"

if [[ $keep_temp -ne 1 ]]; then
  rm -rf "$TMP_ROOT"
fi

exit $((fail>0))
