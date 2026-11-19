# Changelog

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
