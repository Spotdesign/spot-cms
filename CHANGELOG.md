# Changelog

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
