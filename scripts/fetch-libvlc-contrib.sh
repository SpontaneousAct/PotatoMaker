#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: fetch-libvlc-contrib.sh <vlc-source-dir> <host>" >&2
  exit 2
fi

vlc_source="$(cygpath -u "$1")"
host="$2"
build_dir="$vlc_source/contrib/potatomaker-$host"

mkdir -p "$build_dir"
cd "$build_dir"
../bootstrap --host="$host"
make fetch
