#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 7 ]]; then
  echo "Usage: build-ffmpeg-runtime.sh <ffmpeg-source> <svt-source> <nv-codec-source> <zlib-source> <work-dir> <output-dir> <version-label>" >&2
  exit 2
fi

to_unix_path() {
  cygpath -u "$1"
}

ffmpeg_source="$(to_unix_path "$1")"
svt_source="$(to_unix_path "$2")"
nv_source="$(to_unix_path "$3")"
zlib_source="$(to_unix_path "$4")"
work_dir="$(to_unix_path "$5")"
output_dir="$(to_unix_path "$6")"
version_label="$7"
prefix="$work_dir/prefix"

mkdir -p "$work_dir" "$output_dir" "$prefix"

cmake -S "$svt_source" -B "$work_dir/svt-build" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_INSTALL_PREFIX="$prefix" \
  -DBUILD_SHARED_LIBS=OFF \
  -DBUILD_APPS=OFF \
  -DBUILD_TESTING=OFF \
  -DSVT_AV1_LTO=OFF
cmake --build "$work_dir/svt-build" --parallel
cmake --install "$work_dir/svt-build"

make -C "$nv_source" PREFIX="$prefix" install

cd "$zlib_source"
CHOST=x86_64-w64-mingw32 ./configure --static --prefix="$prefix"
make -j"$(nproc)"
make install

export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
cd "$ffmpeg_source"
./configure \
  --prefix="$work_dir/ffmpeg-install" \
  --extra-version="$version_label" \
  --pkg-config-flags=--static \
  --extra-cflags="-I$prefix/include" \
  --extra-ldflags="-L$prefix/lib -static" \
  --enable-static \
  --disable-shared \
  --disable-autodetect \
  --disable-debug \
  --disable-doc \
  --disable-ffplay \
  --enable-gpl \
  --disable-nonfree \
  --enable-libsvtav1 \
  --enable-ffnvcodec \
  --enable-nvenc \
  --enable-zlib

make -j"$(nproc)"
cp -f ffmpeg.exe ffprobe.exe "$output_dir/"

{
  echo "MSYS2 package inventory"
  echo "======================="
  pacman -Q
  echo
  echo "Compiler"
  echo "========"
  gcc --version
  echo
  echo "CMake"
  echo "====="
  cmake --version
  echo
  echo "Ninja"
  echo "====="
  ninja --version
} > "$output_dir/BUILD-ENVIRONMENT.txt"
