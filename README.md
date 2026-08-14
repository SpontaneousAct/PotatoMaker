# PotatoMaker

PotatoMaker turns large video recordings into clips that are small enough to share on Discord and similar services.

Choose a video, trim it down to the part you want, and press **Compress**. PotatoMaker works out the bitrate and resolution, removes black bars when it can, and exports a shareable MP4. If a longer clip cannot fit into one file without looking terrible, it can split the result into numbered parts instead.

Everything runs on your PC. Your videos are not uploaded anywhere.

[Visit the PotatoMaker website](https://spontaneousact.github.io/PotatoMaker/)

## Download

[Download the latest Windows installer](https://github.com/SpontaneousAct/PotatoMaker/releases/latest/download/PotatoMaker-win-x64-Setup.exe)

PotatoMaker is built for 64-bit Windows. The first time you open it, it shows the exact FFmpeg and VLC versions, providers, archive URLs, download sizes, SHA-256 verification details, and install locations before asking for permission to download anything. These tools are kept in your local app data and removed when you uninstall PotatoMaker.

### FFmpeg and VLC setup

PotatoMaker does not bundle or redistribute FFmpeg or native VLC. Automatic setup downloads a pinned FFmpeg ZIP directly from [BtbN FFmpeg Builds](https://github.com/BtbN/FFmpeg-Builds) and a pinned VLC ZIP directly from [VideoLAN](https://www.videolan.org/vlc/). It verifies both archives against the SHA-256 fingerprints built into that PotatoMaker release before extracting them.

The installed files use stable, manually understandable locations:

```text
%LOCALAPPDATA%\PotatoMaker\runtimes\
├─ ffmpeg\
│  ├─ ffmpeg.exe
│  └─ ffprobe.exe
└─ libvlc\
   ├─ libvlc.dll
   ├─ libvlccore.dll
   ├─ plugins\
   ├─ lua\
   └─ hrtfs\
```

The setup window also provides direct manual-download buttons, opens the install folder, and can check the files again after they are copied. For manual FFmpeg setup, copy `ffmpeg.exe` and `ffprobe.exe` from the archive's `bin` folder into `runtimes\ffmpeg`. For VLC, copy the contents of the archive's top-level VLC folder into `runtimes\libvlc` so `libvlc.dll` and the `plugins` folder sit directly inside it.

Interrupted downloads are retried and resumed when the server supports it. Installation failures are recorded in `%LOCALAPPDATA%\PotatoMaker\Logs\media-tools-setup.log` with the failed stage and technical exception.

## Using PotatoMaker

1. Drag a video into the window, or choose one with **Browse**.
2. Set the start and end points if you only want part of the recording.
3. Check the proposed crop, frame rate, and output plan.
4. Choose where to save the result, then press **Compress**.

In **Settings**, you can point PotatoMaker at the folder where your recordings are saved. Your latest videos will then appear in the **Recent videos** menu, ready to open without digging through folders.

You can also make several clips from the same recording: choose a section, add it to the queue, then adjust the start and end points and add the next one. When the queue is ready, PotatoMaker will work through the clips in order.

PotatoMaker accepts MP4, MKV, AVI, MOV, WebM, WMV, and FLV files. Exports are MP4 files and use the `_discord` suffix by default.

### Keyboard shortcuts

| Key | Action |
| --- | --- |
| `Space` | Play or pause |
| `Q` / `E` | Jump back or forward 10 seconds |
| `A` | Set the start point |
| `D` | Set the end point |

## A note about file size and speed

The default settings use a 20 MB per-file upload limit. You can change the limit under **Settings > Encoding Defaults**; PotatoMaker keeps safety headroom below the selected value because video compression is not perfectly predictable. Long videos may be divided into several parts.

PotatoMaker uses CPU encoding by default. If your computer supports NVIDIA AV1 encoding, you can enable it in **Settings** for faster exports, though GPU encoding is less predictable when aiming for an exact file size.

## Command line

There is also a small CLI for scripts and quick conversions. It currently runs from the source tree:

```powershell
dotnet run --project .\PotatoMaker.Cli -- "C:\clips\example.mp4"
```

The CLI uses NVIDIA AV1 encoding by default. Pass `--cpu` to use the CPU encoder:

```powershell
dotnet run --project .\PotatoMaker.Cli -- --cpu "C:\clips\example.mp4"
dotnet run --project .\PotatoMaker.Cli -- --target-size-mb 50 "C:\clips\example.mp4"
```

Unlike the desktop app, the CLI does not switch encoders automatically. It will ask you to use `--cpu` if NVIDIA AV1 encoding is unavailable.

## Building from source

You will need the .NET SDK version listed in [`global.json`](global.json). The desktop app offers to download FFmpeg and VLC when it first runs. To use local copies elsewhere instead, set `POTATOMAKER_FFMPEG_DIR` to the folder containing `ffmpeg.exe` and `ffprobe.exe`, and `POTATOMAKER_LIBVLC_DIR` to the folder containing `libvlc.dll`, `libvlccore.dll`, and `plugins`. The FFmpeg override may also point to a parent containing a `bin` folder. The CLI can find FFmpeg on `PATH`.

```powershell
dotnet build .\PotatoMaker.slnx
dotnet test .\PotatoMaker.Tests
dotnet run --project .\PotatoMaker.GUI
```

Packaging scripts are available in [`scripts`](scripts):

```powershell
.\scripts\publish-portable.ps1
.\scripts\publish-velopack.ps1
```

Maintainers can create a tested draft GitHub release with the manual **Release PotatoMaker** workflow. See [the release guide](docs/RELEASING.md) for the versioning, review, and publishing steps.

## Contributing

Bug reports and pull requests are welcome. If you change compression rules, output naming, or visible app behavior, please add or update the relevant tests in `PotatoMaker.Tests`.

## License

PotatoMaker is available under the [MIT License](LICENSE.txt). Releases also include FFMpegCore and LibVLCSharp; see the [third-party notices](third_party/notices/THIRD-PARTY-NOTICES.txt) for their licenses. FFmpeg and VLC are downloaded separately and are not part of the PotatoMaker package.
