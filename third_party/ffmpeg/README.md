Place FFmpeg binaries here for portable packaging.

Expected default layout for `scripts/publish-portable.ps1`:

```text
third_party/ffmpeg/
`- win-x64/
   |- ffmpeg.exe
   |- ffprobe.exe
   `- LICENSE*.txt (recommended)
```

The script also supports a custom location via `-FfmpegDir`.
