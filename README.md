# PotatoMaker

PotatoMaker compresses videos to fit Discord-sized uploads using AV1 encoding.
It includes both a command-line app and a desktop GUI, backed by a shared core pipeline.

## Features

- One-command CLI usage: `potatomaker video.mp4` outputs `video_discord.mp4`
- Desktop GUI built with Avalonia
- GPU-accelerated encoding with NVIDIA NVENC (`av1_nvenc`) when available
- Automatic fallback to CPU two-pass encoding (`libsvtav1`) when NVENC is unavailable
- Automatic crop detection to remove black bars
- Smart resolution scaling based on bitrate budget
- Automatic splitting into multiple parts for long videos
- Streaming-friendly output (`-movflags +faststart`)

## Requirements

- FFmpeg on system `PATH` (not bundled): https://ffmpeg.org/download.html
- .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
- Optional NVIDIA GPU for AV1 NVENC (Ada Lovelace / RTX 40-series or newer)

NVIDIA compatibility matrix:
https://developer.nvidia.com/video-encode-and-decode-gpu-support-matrix-new

## Solution Layout

```text
PotatoMaker/
|- PotatoMaker.Core/   # Shared probe/crop/plan/encode pipeline
|- PotatoMaker.Cli/    # CLI front-end (assembly name: potatomaker)
|- PotatoMaker.GUI/    # Avalonia desktop front-end
|- PotatoMaker.slnx
`- README.md
```

## Build

```bash
dotnet build PotatoMaker.slnx
```

## Run

### CLI

```bash
dotnet run --project PotatoMaker.Cli -- "C:\clips\gameplay.mp4"
dotnet run --project PotatoMaker.Cli -- --cpu "C:\clips\gameplay.mp4"
```

CLI syntax:

```text
potatomaker [--cpu] <video_file>
```

`--cpu` forces libsvtav1 two-pass instead of NVENC.

### GUI

```bash
dotnet run --project PotatoMaker.GUI
```

In the GUI, pick a file, review the probe summary, optionally enable CPU mode, and start encoding.

## Publish

### CLI (single-file, self-contained win-x64)

```bash
dotnet publish PotatoMaker.Cli/PotatoMaker.Cli.csproj -c Release -r win-x64
```

Output path:

```text
PotatoMaker.Cli/bin/Release/net10.0/win-x64/publish/
```

### GUI

```bash
dotnet publish PotatoMaker.GUI/PotatoMaker.GUI.csproj -c Release -r win-x64
```

## Output Naming

- Single output: `{name}_discord.mp4`
- Split output: `{name}_discord_part1.mp4`, `{name}_discord_part2.mp4`, ...

Outputs are written next to the input file.

## Pipeline Overview

1. Probe input media (duration, resolution, metadata)
2. Detect crop (optional) using FFmpeg `cropdetect`
3. Plan bitrate/resolution/splitting against target size budget
4. Encode with AV1:
   - `av1_nvenc` single-pass constrained VBR, or
   - `libsvtav1` two-pass (with temp passlog files)
5. Write MP4 with `+faststart`

Default planning values (from `EncodeSettings`):

- Hard size limit: 9.5 MB
- Effective budget: 9.0 MB
- Audio reserve: 128 kbps
- 1080p threshold: 1000 kbps
- 720p threshold: 500 kbps
- Max split parts: 10

## License

[MIT](LICENSE.txt)
