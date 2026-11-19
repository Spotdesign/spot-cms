# Spot CMS (`uk.co.spotdesign.cms`)

Text/content CMS, local settings, and build-and-publish tooling for Spot Design Unity exhibits —
packaged for UPM so it drops into any project from one Git URL.

- **Runtime** (`SpotCMS` assembly): `appAdmin`, `textNode`, `page`, `Settings`, a bundled
  dependency-free `JSON` reader, and `ContentBootstrap` (versioned seed/merge of content from
  `StreamingAssets` into `persistentDataPath`).
- **Editor** (`SpotCMS.Editor` assembly): `TextNodeExporter` (export text keys, bump versions,
  import bundles), `AppHandle` (`app.json`), and **Build & Publish Release**.

Requires TextMeshPro. On Unity 6 it's built into uGUI (always present); on 2021/2022 add the
TextMeshPro package.

## Install (per project)

Package Manager → **＋** → *Add package from git URL…*:

```
https://github.com/Spotdesign/spot-cms.git#1.0.0
```

Pin a tag (`#1.0.0`) so projects don't drift. To update a project, bump the tag. It installs into
`Packages/` (read-only), so it can't be edited per-project — the repo is the single source of truth.

Both assemblies are `autoReferenced`, so your existing loose scripts in `Assembly-CSharp` can use
`appAdmin`, `textNode`, `getText(...)` etc. with **no** assembly reference to add. If your own code
lives in its own asmdef, add `SpotCMS` to that asmdef's references.

## Releasing a new package version

1. Commit changes to this repo.
2. Bump `version` in `package.json` and add a `CHANGELOG.md` entry.
3. Tag it: `git tag 1.1.0 && git push --tags`.
4. Projects move up by changing their git URL suffix to `#1.1.0`.

## Build & Publish Release (exhibit releases, not this package)

`Tools > CMS > Build & Publish Release…` builds the exhibit, zips it (`Archive-win.zip` /
`Archive-mac.zip`), creates the GitHub release (tag auto-made from the branch) and uploads the asset.
Repo/owner come from the project's git `origin`. The write token is read once, machine-wide, never
stored in any project:

1. env var `GITHUB_RELEASE_TOKEN`
2. fallback file `~/.spot-release-token.txt` (`%USERPROFILE%` on Windows)

Use a fine-grained PAT with **Contents: write** on the Spotdesign repos. On macOS, Unity launched
from Finder won't see shell env vars, so the file is the reliable route.

## Migrating an existing project onto the package

New projects are clean — just add the package. For a project that already has the scripts loose in
`Assets/` (as `heritage-golf-clubs` and `heritage-designing-nelson` do):

> **`appAdmin`, `textNode` and `page` are MonoBehaviours on scene objects.** The package ships its own
> `.meta` GUIDs, which differ from the loose copies' GUIDs, so simply deleting the loose scripts and
> adding the package would leave those components as "missing script" in scenes.

Two safe options:
- **Leave existing projects as-is** (keep mirroring the loose copies) and use the package only for
  **new** projects. Lowest effort.
- **Migrate with a GUID remap**: before deleting the loose folder, note each MonoBehaviour's old GUID
  (from its `.cs.meta`) and the package's new GUID, then find-and-replace old→new across `.unity`
  and `.prefab` files. Do it on a branch and verify scenes before committing.

Pure-logic scripts (`Settings`, `JSON`, `ContentBootstrap`, all Editor tools) have no scene
references, so they swap over with no fixup.
