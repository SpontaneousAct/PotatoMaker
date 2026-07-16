# FFmpeg runtime policy

PotatoMaker release packages do not contain FFmpeg binaries. Do not add
`ffmpeg.exe` or `ffprobe.exe` under this directory.

The desktop app uses its pinned local-app-data runtime and offers a verified
download directly from BtbN's upstream GitHub release when that runtime is
missing or invalid. `POTATOMAKER_FFMPEG_DIR` is retained as an explicit
developer override. The CLI can still discover FFmpeg from `PATH`.

The pinned URL and SHA-256 live in
`PotatoMaker.Core/FfmpegRuntimePackage.cs`. Downloaded tools are stored under
`%LOCALAPPDATA%\PotatoMaker\runtimes\ffmpeg`; this repository and PotatoMaker's
release assets do not redistribute them.
