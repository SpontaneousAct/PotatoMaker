# PotatoMaker

PotatoMaker is a Windows desktop app for turning videos into small, shareable MP4s without hand-tuning `ffmpeg` settings.

Drop in a clip, trim the section you want, and let the app decide the crop, resolution, bitrate, and whether the export should be split into multiple parts.

## What It Does

- Loads a video with drag and drop or a file picker
- Lets you preview the source before exporting
- Trims a clip visually with a timeline
- Detects crop automatically when it helps
- Uses AV1 NVENC when it is available through FFmpeg
- Falls back to CPU AV1 when NVENC is unavailable or an NVENC encode fails
- Splits long videos into multiple files when one file would be too large
- Saves output as `.mp4`
- Supports choosing an output folder and filename prefix/suffix

## Who It Is For

PotatoMaker is for people who want a quick "make this easier to share" workflow instead of a full video editor.

It is especially useful when you want to:

- shrink gameplay clips or recordings
- make videos easier to post in chat apps
- avoid remembering encoder flags
- trim and compress a clip in one pass

## Quick Start

1. Download the latest Windows build from [GitHub Releases](https://github.com/SpontaneousAct/PotatoMaker/releases).
2. Launch `PotatoMaker.GUI.exe`, or install the packaged release if you want update support and Explorer integration.
3. Drag a video into the window or click `Browse...`.
4. Preview the video and set the start/end of the clip you want.
5. Choose a different output folder if needed.
6. Click `Start Compression`.

## Keyboard Shortcuts

- `Space` plays or pauses preview playback
- `A` sets the trim start at the current position
- `D` sets the trim end at the current position

## Supported Input Formats

PotatoMaker currently accepts:

- `.mp4`
- `.mkv`
- `.avi`
- `.mov`
- `.webm`
- `.wmv`
- `.flv`

## How PotatoMaker Chooses Settings

PotatoMaker is designed to be automatic by default.

For each video, it:

1. probes the source file
2. analyzes the selected clip length
3. detects crop when useful
4. picks an export resolution and bitrate
5. decides whether the result should be one file or several parts

The goal is a small, share-friendly export with as little manual setup as possible.

## Output Behavior

- Output files are written as `.mp4`
- By default, exports use the suffix `_discord`
- If you do not choose a custom output folder, files are written next to the source video
- Installed Windows builds can add a `Compress with PotatoMaker` Explorer context menu entry

## Requirements

### For End Users

- Windows is the primary supported platform for the GUI app
- AV1 NVENC-capable NVIDIA hardware can make exports much faster
- If FFmpeg cannot use AV1 NVENC on the current machine, PotatoMaker uses CPU AV1 instead

### For Building From Source

- .NET SDK `10.0.103` or newer in the `10.0.x` line
- Windows for the desktop app workflow
- `ffmpeg` and `ffprobe` available on `PATH`, or bundled locally for packaging

## Build From Source

Clone the repository, then run:

```powershell
dotnet restore
dotnet build
dotnet test
```

To start the desktop app:

```powershell
dotnet run --project .\PotatoMaker.GUI
```

## Packaging

Portable publish:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

Velopack installer/release packaging:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -GitHubRepoUrl https://github.com/SpontaneousAct/PotatoMaker
```

If you are packaging releases yourself, make sure `ffmpeg.exe` and `ffprobe.exe` are available either on `PATH` or in `third_party\ffmpeg\win-x64`.

## Project Layout

- `PotatoMaker.GUI` - Avalonia desktop app
- `PotatoMaker.Core` - encoding, probing, crop detection, and planning logic
- `PotatoMaker.Cli` - command-line wrapper around the core pipeline
- `PotatoMaker.Tests` - automated tests

## Attribution

App icon attribution:

<a href="https://www.flaticon.com/free-icons/potato" title="potato icons">Potato icons created by Freepik - Flaticon</a>

## License

This project is licensed under the terms in [LICENSE.txt](./LICENSE.txt).
