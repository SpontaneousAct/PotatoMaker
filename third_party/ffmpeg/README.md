Place FFmpeg binaries here for portable packaging.

Expected default layout for `scripts/publish-portable.ps1`:

```text
third_party/ffmpeg/
`- win-x64/
   |- ffmpeg.exe
   |- ffprobe.exe
   `- runtime-manifest.json
```

The script also supports a custom location via `-FfmpegDir`.

Build the approved GPL runtime and its corresponding-source bundle with
`scripts/build-ffmpeg-runtime.ps1`. The source inputs and their hashes are pinned
in `third_party/ffmpeg/manifests/source-win-x64.json`; the build writes a runtime
manifest beside the binaries. Pass a custom output with `-FfmpegManifestPath`.
Packaging fails when supplied binaries do not match that generated manifest.

Never approve a build containing `--enable-nonfree`; keep `--enable-gpl` so
PotatoMaker's required cropdetect filter remains present.
