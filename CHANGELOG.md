# Changelog

## [1.0.6] - 2026-11-26
### Added
- Sidecar files: loose files listed in `<project root>/deploy-files.txt` (one path per line, relative
  to Assets) are copied into the build root next to the exe before zipping — e.g. `screensaver.mp4`,
  which the app loads from beside the exe at runtime. Replaces the old manual step of adding them to
  the Archive zip by hand.

## [1.0.5] - 2026-11-24
### Added
- Build & Publish "Replace current release" toggle: rebuilds and swaps the zip (and content
  backup) on the latest existing release instead of creating a new version — for test iterations.
  Deletes the same-named asset first, then re-uploads. No new tag/version is created.

## [1.0.4] - 2026-11-20
### Added
- Build & Publish now attaches a dated `content-<version>.json` backup (data + schema + settings +
  meta) to every release, so each release is an off-machine, per-repo restore point. Skipped
  automatically for projects with no text CMS (no `data.json`).

## [1.0.3] - 2026-11-19
### Added
- Publish confirmation now shows the client download link (`https://spotd.uk/get/<repo>`) with a
  "Copy link" button, ready to paste into the update email.

## [1.0.2] - 2026-11-19
### Fixed
- Add `.meta` files for `package.json`, `README.md` and `CHANGELOG.md` to silence Unity's
  "has no meta file ... in an immutable folder" warnings.

## [1.0.1] - 2026-11-19
### Fixed
- `BuildPublish`: fully-qualify `System.IO.Compression.CompressionLevel` to resolve an ambiguity
  with `UnityEngine.CompressionLevel` when compiled inside the `SpotCMS.Editor` assembly.

## [1.0.0] - 2026-11-19
### Added
- Initial package: SpotCMS Runtime (appAdmin, textNode, page, Settings, JSON, ContentBootstrap)
  and Editor tooling (TextNodeExporter, AppHandle, BuildPublish).
- Versioned content bootstrap with structural merge and optional full-replace (`contentReplace`).
- Build & Publish Release: build + zip + create GitHub release + upload, machine-wide write token.
