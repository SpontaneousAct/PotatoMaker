# VLC runtime policy

PotatoMaker release packages contain the managed LibVLCSharp integration but do
not contain native VLC, LibVLC, or codec-plugin binaries. Do not add those files
under this directory.

The desktop app uses a pinned VLC runtime under
`%LOCALAPPDATA%\PotatoMaker\runtimes\libvlc`. When it is missing or invalid,
PotatoMaker asks the user before downloading the official archive directly from
VideoLAN and verifying its SHA-256. `POTATOMAKER_LIBVLC_DIR` is retained only as
an explicit developer override.

The pinned URL and checksum live in
`PotatoMaker.GUI/Services/LibVlcRuntimePackage.cs`. This repository and
PotatoMaker's release assets do not redistribute the native runtime.
