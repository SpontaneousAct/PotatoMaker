# PotatoMaker

PotatoMaker is a small video compression tool for making clips easier to share.

Open a video, trim the part you want, let the app figure out a sensible export strategy, and save a smaller MP4 that is easier to post in chats, communities, and social apps.

## What It Does

- Loads common video files such as `.mp4`, `.mkv`, `.avi`, `.mov`, `.webm`, `.wmv`, and `.flv`
- Lets you trim a clip before exporting
- Detects crop automatically when it helps
- Chooses bitrate, resolution, and splitting strategy for you
- Exports one MP4 or multiple parts when a single file would be too large
- Supports both AV1 NVENC and SVT-AV1 CPU encoding
- Includes both a desktop GUI and a CLI

## Why You Might Want It

PotatoMaker is built for the "I just need this video to be smaller and still watchable" workflow.

Instead of manually juggling FFmpeg commands, guessing bitrates, or re-exporting the same clip a few times, you can:

1. Pick a file
2. Trim the section you want
3. Check the preview plan
4. Click `Compress`

## Screenshots

Add your images here when you are ready.

<!-- Replace this block with a real screenshot -->
<!-- Example: ![Main window](docs/media/main-window.png) -->

`[ Screenshot placeholder: main app window ]`

`[ Screenshot placeholder: strategy preview / output settings ]`

## Demo GIFs

This section is intentionally left open for short workflow demos.

<!-- Replace this block with a real GIF -->
<!-- Example: ![Trim and compress demo](docs/media/trim-and-compress.gif) -->

`[ GIF placeholder: drag a file in and trim it ]`

`[ GIF placeholder: compressing a clip ]`

## Main Workflow

1. Open a video with `Browse...` or drag and drop it into the app.
2. Preview the file and set trim start/end points.
3. Review the generated strategy preview.
4. Choose your output folder and naming options.
5. Compress immediately or add the job to the queue.

## Desktop App Highlights

- User-friendly Windows GUI built with Avalonia
- Built-in video preview
- Keyboard shortcuts for quick trimming
- Queue for lining up multiple compressions
- Recent videos panel
- Light/dark theme support
- Built-in update plumbing for packaged releases

### Keyboard Shortcuts

- `Space`: play or pause
- `Q`: jump back 10 seconds
- `E`: jump forward 10 seconds
- `A`: set trim start
- `D`: set trim end

## CLI Usage

The repo also includes a command-line app for quick or scripted use.

### Basic command

```powershell
dotnet run --project .\PotatoMaker.Cli -- "C:\clips\example.mp4"
```

### Force CPU encoding

```powershell
dotnet run --project .\PotatoMaker.Cli -- --cpu "C:\clips\example.mp4"
```

### CLI help summary

```text
potatomaker [--cpu] <video_file>
```

By default the CLI uses AV1 NVENC and exits with an error if it is unavailable. Use `--cpu` to select the SVT-AV1 CPU encoder instead.

## Requirements

### For end users

- Windows for the desktop app
- FFmpeg and FFprobe available either:
  - from a bundled `ffmpeg` folder in the app/package, or
  - from your system `PATH`

### For development

- .NET SDK `10.0.103`
- PowerShell for the packaging scripts

## Running The GUI

```powershell
dotnet run --project .\PotatoMaker.GUI
```

## Building The Solution

```powershell
dotnet build .\PotatoMaker.slnx
```

## Running Tests

```powershell
dotnet test .\PotatoMaker.Tests
```

## Packaging

### Portable build

```powershell
.\scripts\publish-portable.ps1
```

### Velopack package

```powershell
.\scripts\publish-velopack.ps1
```

Both scripts can bundle FFmpeg if you provide it or keep it in the expected `third_party\ffmpeg\<runtime>` location.

## Project Structure

```text
PotatoMaker.Core   Core video analysis, planning, and encoding pipeline
PotatoMaker.GUI    Desktop app
PotatoMaker.Cli    Command-line app
PotatoMaker.Tests  Unit tests
scripts            Publishing and diagnostics scripts
third_party        External runtime dependencies such as FFmpeg
```

## Notes

- The default output suffix is `_discord`
- Output files are written as `.mp4`
- Trimmed clips include time markers in the output filename
- If a clip would end up too large as a single file, PotatoMaker can split it into multiple parts

## Contributing

Issues and pull requests are welcome. If you are changing compression logic, output naming, or UI behavior, adding or updating tests in `PotatoMaker.Tests` will make the change much easier to review.

## License

This project is licensed under the MIT License. See [LICENSE.txt](LICENSE.txt).
