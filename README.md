# PotatoMaker

A CLI tool that compresses any video to fit within Discord's **10 MB** file-size limit using HEVC (H.265) encoding. Just point it at a video and get a Discord-ready `.mp4` back.

## Features

- **One-command usage** — `potatomaker video.mp4` produces `video_discord.mp4` next to the input
- **GPU-accelerated encoding** — uses NVIDIA NVENC (`hevc_nvenc`) by default, with automatic fallback to CPU two-pass (`libx265`)
- **Automatic crop detection** — detects and removes black bars (letterbox/pillarbox) to maximize picture quality
- **Smart resolution scaling** — dynamically chooses 1080p / 720p based on available bitrate budget
- **Auto-splitting** — if a single file can't meet the quality floor, it splits into up to 10 parts that each fit under the limit
- **Apple/browser compatibility** — all output is tagged `hvc1` with `faststart` for broad playback support

## Requirements

- **FFmpeg** — must be on your system `PATH` (not bundled).  
  Download from [https://ffmpeg.org/download.html](https://ffmpeg.org/download.html).
- **.NET 10 SDK** — required for building from source.  
  Download from [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
- **NVIDIA GPU + drivers** — required for NVENC hardware encoding; falls back to CPU (`libx265`) if unavailable.

### Supported NVIDIA GPUs for NVENC (HEVC)

NVENC HEVC encoding requires a Maxwell (2nd gen) or newer NVIDIA GPU. For the full compatibility matrix, see [NVIDIA's Video Encode and Decode GPU Support Matrix](https://developer.nvidia.com/video-encode-and-decode-gpu-support-matrix-new).

## Installation

### From source

```bash
# Clone the repo
git clone https://github.com/SpontaneousAct/PotatoMaker.git
cd PotatoMaker

# Publish self-contained single-file binary (win-x64)
dotnet publish PotatoMaker/PotatoMaker.csproj -c Release -r win-x64
```

The published binary will be in `PotatoMaker/bin/Release/net10.0/win-x64/publish/`.

### Add to Windows right-click "Open with"

1. Copy `potatomaker.exe` to a permanent location (e.g. `C:\Tools\potatomaker.exe`)
2. Right-click any video file → **Open with** → **Choose another app**
3. Scroll down and click **Choose an app on your PC**, then browse to `potatomaker.exe`

After selecting it once, PotatoMaker will appear in the "Open with" list for that file type going forward.

## CLI Usage

```
potatomaker [--cpu] <video_file>
```

### Examples

```bash
# Compress a video (GPU encoding, auto-detect best settings)
potatomaker "C:\clips\gameplay.mp4"

# Force CPU two-pass encoding (slower but no GPU required)
potatomaker --cpu "C:\clips\gameplay.mp4"
```

### Options

| Flag    | Description                                               |
|---------|-----------------------------------------------------------|
| `--cpu` | Use libx265 CPU two-pass encoder instead of NVENC (GPU)   |

### Output

- Single file: `{name}_discord.mp4`
- Split files: `{name}_discord_part1.mp4`, `{name}_discord_part2.mp4`, …

Output files are written next to the input file.

## How It Works

PotatoMaker runs a five-stage pipeline:

1. **Probe** — reads duration, resolution, and metadata via FFProbe
2. **Crop detection** — runs FFmpeg's `cropdetect` filter to find and remove symmetric black bars (letterbox/pillarbox)
3. **Encode planning** — calculates the video bitrate to fit within 9.0 MB (with 128 kbps reserved for audio). If the bitrate falls below the quality floor (500 kbps at 720p), it splits the video into multiple parts
4. **Resolution selection** — picks output height based on bitrate budget:
   - &ge; 1000 kbps &rarr; 1080p (capped, never upscaled)
   - &ge; 500 kbps &rarr; 720p
   - &lt; 500 kbps &rarr; split into parts to stay above the floor
5. **Encode** — encodes with HEVC; NVENC uses constrained VBR (`-rc vbr`), libx265 uses true two-pass with a temp stats file

### Encoding Details

| Encoder       | Mode                                                        |
|---------------|-------------------------------------------------------------|
| `hevc_nvenc`  | Single-pass constrained VBR (`-rc vbr -preset p5`)         |
| `libx265`     | Two-pass (stats file in `%TEMP%`, cleaned up automatically) |

Both paths output `-tag:v hvc1 -movflags +faststart` for maximum compatibility.

## Project Structure

```
PotatoMaker/
├── Program.cs            # Entry point, argument parsing
├── ProcessingPipeline.cs # Orchestrates probe → crop → plan → encode
├── EncodePlanner.cs      # Bitrate math, resolution selection, split planning
├── CropDetector.cs       # Black bar detection via ffmpeg cropdetect
├── VideoEncoder.cs       # NVENC and libx265 encoding with progress bars
├── EncodeJob.cs          # Data record passed between planner and encoder
└── ConsoleHelper.cs      # Colored console output utility
```

## License

[MIT](LICENSE.txt)