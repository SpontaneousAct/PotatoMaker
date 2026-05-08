# AGENTS.md

This file applies to the entire repository.

## Purpose

PotatoMaker is a Windows-first video compression tool for making clips easier to share, especially within Discord-sized upload limits. The app trims videos, analyzes a sensible export strategy, and writes a smaller MP4, optionally split into multiple parts.

## Repository Layout

- `PotatoMaker.Core`: shared probe, crop detection, strategy analysis, and encoding pipeline logic
- `PotatoMaker.GUI`: Avalonia desktop app
- `PotatoMaker.Cli`: command-line front end
- `PotatoMaker.Tests`: xUnit test project for core and GUI-facing behavior
- `website`: Astro marketing/help site
- `scripts`: PowerShell packaging and diagnostics scripts
- `third_party`: external runtime dependencies such as FFmpeg payloads

Treat `.tmp`, `.codex-build`, `.codex-verify`, `artifacts`, `website/dist`, and `website/node_modules` as generated or scratch areas unless a task explicitly targets them.

## Tech Stack

- .NET SDK `10.0.103` via `global.json`
- Target framework: `net10.0`
- Nullable enabled across projects
- Central package management in `Directory.Packages.props`
- GUI: Avalonia + CommunityToolkit.Mvvm + LibVLCSharp + Velopack
- Core encoding/probing: `FFMpegCore`
- Website: Astro with Tailwind v4 via Vite plugin

## Core Architecture

Front ends are responsible for preflight work and then pass precomputed analysis into the pipeline.

1. Probe input with `VideoInfo.ProbeAsync(...)`
2. Analyze strategy with `StrategyAnalyzer.AnalyzeAsync(...)`
3. Encode with `ProcessingPipeline.RunAsync(...)`

Important: `ProcessingPipeline` executes encoding and output stages only. It should not be changed to repeat probe, crop detection, or planning that already happened upstream.

## Project Conventions

- Keep business logic in `PotatoMaker.Core`; avoid moving encoding or planning rules into CLI or GUI layers.
- Keep GUI code MVVM-oriented. Put state and commands in view models, not in Avalonia code-behind, unless a task is explicitly UI-plumbing only.
- Budget and threshold defaults belong in `EncodeSettings`, not as duplicated magic numbers elsewhere.
- Preserve the current encoder model:
  - default GPU path: `av1_nvenc`
  - CPU fallback / explicit CPU path: `libsvtav1`
- Preserve current output behavior unless a task says otherwise:
  - default suffix `_discord`
  - output format `.mp4`
  - large outputs may split into `_part1`, `_part2`, etc.

## Working Areas

### .NET app

- Solution file: `PotatoMaker.slnx`
- Preferred entry points:
  - GUI: `PotatoMaker.GUI`
  - CLI: `PotatoMaker.Cli`
- Settings persistence lives in the GUI layer via `IAppSettingsService` / `JsonAppSettingsService`, coordinated through `AppSettingsCoordinator`.
- Runtime dependencies such as `ffmpeg` and `ffprobe` must remain discoverable at runtime, either from PATH or expected bundled locations.

### Website

- Edit source under `website/src` and static assets under `website/public`.
- Do not hand-edit `website/dist` or `website/node_modules`.
- Keep the project-page aware `site` / `base` behavior in `website/astro.config.mjs` intact unless the task is specifically about deployment routing.

## Build, Test, and Run

Run commands from the repository root unless there is a good reason not to.

```powershell
dotnet build .\PotatoMaker.slnx
dotnet test .\PotatoMaker.Tests
dotnet run --project .\PotatoMaker.GUI
dotnet run --project .\PotatoMaker.Cli -- "C:\clips\example.mp4"
dotnet run --project .\PotatoMaker.Cli -- --cpu "C:\clips\example.mp4"
```

Website commands:

```powershell
cd .\website
npm run dev
npm run build
```

Packaging scripts:

```powershell
.\scripts\publish-portable.ps1
.\scripts\publish-velopack.ps1
```

## Change Expectations

- Add or update tests in `PotatoMaker.Tests` when changing compression logic, naming, planning, or user-visible GUI behavior.
- Prefer focused fixes that preserve current architecture over broad refactors.
- If modifying shared pipeline behavior, verify both CLI and GUI assumptions still hold.
- If modifying website content or styling, keep edits in source files and rebuild only when needed.

## Safety Notes For Agents

- Check `git status` before editing; this repository may contain in-progress user changes.
- Never overwrite or revert unrelated work in dirty files.
- Avoid editing generated output, vendored dependencies, or scratch directories unless the task explicitly requires it.
- Prefer PowerShell-friendly commands and paths in docs and scripts, since the repo already uses PowerShell for packaging and developer workflows.
