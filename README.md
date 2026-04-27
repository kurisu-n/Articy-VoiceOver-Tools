# Articy VoiceOver Tools

An Articy:draft X macro plugin for managing voice-over asset naming, cleanup, and translation stripping across DialogueFragments. Internal plugin id: `Kurisu.VoiceOverTools`.

## Features

### Rename Voice-Overs (All / Selected)

Scans DialogueFragments and proposes per-asset renames so each VO file follows a `{hexId}_{culture}` convention. A preview window shows the plan:

- Each VO reference is categorized: **Already correct**, **Will rename**, **Display name update**, **Target file exists (skip)**, **Source file missing (skip)**
- Click the category badges above the list to filter by category
- Each row shows the file-name and display-name *before → after*
- Click any row and then **Show Fragment** or **Show VO Asset** to jump to it in Articy's main view (window stays open)
- Choose **Dry run** to preview only, or **Execute rename** to apply

### Audit Voice-Overs

Read-only diagnostic that scans every DialogueFragment and flags VO issues:

- **Missing**: a text property has voice-over enabled but no asset assigned for one or more languages
- **Corrupted**: the assigned asset is broken — invalid proxy, missing `AbsoluteFilePath`, file missing on disk, or 0-byte file
- **Overlapping**: a single audio asset is referenced by multiple distinct fragments (sometimes intentional, sometimes a bug — the audit just surfaces it)

Same window pattern as the others — category filter toggles, click-to-navigate (**Show Fragment** / **Show VO Asset**).

### Clean Up Orphaned Voice-Overs

Finds audio assets that no DialogueFragment references via its `VoiceOverReferences` and offers three actions:

- **Dry run** — list only, no changes
- **Delete from Articy only** (recommended) — removes the asset entries; Articy cleans up the on-disk files on session close, and the action stays reversible via Undo until then
- **Delete from Articy AND disk immediately** — permanent, guarded behind a confirmation dialog

### Remove Translations and Voice-Overs

Right-click a selection (a Flow, Dialogue, or DialogueFragment) and strip all non-primary-language translations and voice-over references. VO references are pointed at a silent-placeholder asset that's cleaned up at session close.

## Installation

1. Download the latest `.mdk` from the [`releases/`](./releases) folder.
2. Install it via Articy:draft X's **Package Manager** (the standard plugin install path for the app — see Articy's [Packaging a plugin](https://www.articy.com/adxdevkit/html/packaging_a_plugin.htm) docs).
3. Commands appear in the ribbon (global scope, e.g. "Rename All Voice-Overs", "Clean Up Orphaned Voice-Overs", "Audit Voice-Overs") and in right-click context menus for selections (e.g. "Rename Selected Voice-Overs", "Remove Translations and Voice-Overs").

## Build from source

Requirements:

- .NET 8 SDK
- Windows (WPF)
- Articy:draft X installed — the `Articy.MDK` NuGet package's reference assemblies ship alongside the app

```bash
dotnet build -c Release
```

Output lands in `bin/Release/`. To produce a distributable `.mdk`, either:

- Use Articy's built-in **DevKit Tools** plugin to package the build folder (canonical path), or
- Zip the contents of `bin/Release/` and rename the archive to `.mdk` (the `.mdk` format is a plain zip of the built plugin files)

## License

MIT — see [LICENSE](./LICENSE).
