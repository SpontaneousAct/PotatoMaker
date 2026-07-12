# Releasing PotatoMaker

PotatoMaker releases are built by a manually triggered GitHub Actions workflow. The workflow tests the selected commit, builds the Windows installer and Velopack update assets, and creates a **draft** GitHub release. It never publishes the release automatically.

## One-time GitHub setup

No repository secrets or personal access tokens are required. The workflow uses GitHub's short-lived `GITHUB_TOKEN` with `contents: write` permission.

In the repository, open **Settings → Actions → General → Workflow permissions** and confirm that workflows are allowed to use `GITHUB_TOKEN`. A repository-level read-only default is fine because the release workflow explicitly requests `contents: write`.

The workflow must be present on the default `master` branch before GitHub displays its **Run workflow** button.

## Create a release

1. Decide the next semantic version, for example `1.9.0`.
2. Change `VersionPrefix` in `Directory.Build.props` to that version.
3. Commit the version change and all intended release changes, then push or merge them to `master`.
4. Open the repository on GitHub and select **Actions → Release PotatoMaker → Run workflow**.
5. Leave the branch set to `master`.
6. Enter the same version from `Directory.Build.props`, without a leading `v`—for example, `1.9.0`.
7. Select **Mark the draft as a prerelease** only for beta or other non-stable builds.
8. Select **Run workflow** and wait for it to finish.
9. Open the draft release from the workflow summary or from the repository's **Releases** page.
10. Review the generated notes and confirm that the installer, portable ZIP, full Velopack package, update metadata, and `SHA256SUMS.txt` are attached.
11. Optionally install and smoke-test `PotatoMaker-win-x64-Setup.exe` from the draft.
12. Select **Publish release** when it is ready for users.

Publishing makes the release visible to users, updates the `/releases/latest/` installer link used by the website, and makes the release available to PotatoMaker's updater.

For a prerelease version such as `1.9.0-beta.1`, keep `VersionPrefix` as `1.9.0`, add `VersionSuffix` with the value `beta.1`, enter `1.9.0-beta.1` in the workflow, and select the prerelease checkbox.

## Safety checks

The workflow stops instead of releasing when:

- a branch other than `master` is selected;
- the entered version does not match `Directory.Build.props`;
- the version is not a supported semantic version;
- its Git tag or GitHub release already exists;
- tests fail;
- required installer or updater assets are missing; or
- the draft points at a commit other than the commit that was built.

If a run fails after creating a draft, delete that abandoned draft and its tag before retrying the same version. Otherwise, bump the version and run a new release.

## FFmpeg handling

The release workflow builds `ffmpeg.exe` and `ffprobe.exe` from pinned source
using `scripts/build-ffmpeg-runtime.ps1`. The recipe produces a GPL executable
build with FFmpeg's cropdetect filter, SVT-AV1, NVIDIA encoding headers, and
zlib. It explicitly disables nonfree components. PotatoMaker invokes FFmpeg as
a separate process and does not link its libraries into the MIT-licensed app.

The source inputs, immutable revisions, and SHA-256 hashes are reviewed in
`third_party/ffmpeg/manifests/source-win-x64.json`. The build generates a
runtime manifest containing the executable hashes and observed configuration;
packaging validates against that manifest and stops on a mismatch.

Changing the bundled FFmpeg build is a separate maintenance operation:

1. Update the pinned source revision and archive hash in
   `third_party/ffmpeg/manifests/source-win-x64.json`.
2. Review the license and configure changes, keeping `--enable-gpl` and
   `--disable-nonfree` mandatory.
3. Run `scripts/build-ffmpeg-runtime.ps1`, then exercise probing, thumbnailing,
   CPU AV1 encoding, and (where hardware is available) NVENC.
4. Produce and test a release locally before publishing it.

Every package includes `THIRD-PARTY-NOTICES.txt`, canonical license texts, and
`ffmpeg/FFMPEG-SOURCE.txt`. Releases also attach the complete FFmpeg and LibVLC
corresponding-source zip files generated during the same run. The LibVLC Dolby Surround and headphone mixer
plugins are intentionally excluded because those two files are GPL-only even
though the containing NuGet package is labelled LGPL.

## Local draft upload fallback

If GitHub Actions is unavailable, the existing script can still create a draft from a Windows development machine that has the required FFmpeg files:

```powershell
$env:POTATOMAKER_GITHUB_TOKEN = "<token with repository Contents write access>"

.\scripts\publish-velopack.ps1 `
  -GitHubRepoUrl "https://github.com/SpontaneousAct/PotatoMaker" `
  -UploadToGitHub:$true `
  -PublishRelease:$false
```

Do not use `-PublishRelease:$true` unless an immediate public release is specifically intended.
