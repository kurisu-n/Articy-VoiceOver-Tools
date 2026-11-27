# Articy VoiceOver Tools

An Articy:draft X macro plugin for managing voice-over asset naming, cleanup, and translation stripping across DialogueFragments. Internal plugin id: `Kurisu.VoiceOverTools`.

## Features

### Export / Import Voice-Overs

Takes the voice-over files under one object out of the project and brings edited ones back. Open it from the ribbon, or right-click an object to pre-pick it; **Pick...** opens articy's own object picker. Both modes share the object, a folder, the languages and a *main language*.

- **Export** copies every voice-over under the object to the folder, after a preview:
  - the main language's files lose their `_language` suffix (`0x…1B4D_el(1C49).mp3` → `0x…1B4D(1C49).mp3`); names without a suffix are left alone
  - keep or strip the `(XXXX)` tag articy appends to stored files
  - optional subfolders: one top folder per language, 0-6 hierarchy levels below the object, and a speaker folder before or after the hierarchy (`(no speaker)` when unset)
  - existing files are skipped or overwritten; a voice-over shared by several fragments is written once, and two different files landing on one name are reported and neither is written
- **Import** searches a folder and its subfolders and replaces the voice-overs whose stored names match: exactly when the file keeps its `(XXXX)` tag, ignoring the tag when it does not, and with or without the main language's suffix. A file matching several voice-overs, several files matching one, and a different file format are listed as **conflicts**: each offers a choice and stays skipped until you pick. Files identical to the stored one are skipped, and a voice-over shared by several fragments is flagged because replacing it changes all of them. Replaced assets keep their display name, description and external id.

### Match Language Voice-Overs

Binds a new language's audio, already imported into articy, to the right fragments by name. Pick where the voice-overs are (right-click a dialogue to pre-pick it), the asset folder holding the new audio (right-click it to pre-pick it; subfolders optional) and the target language, then preview.

- A slot answers to the names of the voice-overs it already has in other languages, with the `(XXXX)` tag and a trailing `_language` ignored. So `name_el`, `name_en` and `name` all match, `_de` counts for `de-DE`, and suffixes that are not project languages (`_SC`, `_final`) stay part of the name.
- Among assets sharing a name, one named for the target language wins, then one without a language suffix. One named for another language is never used.
- Two equally good assets, or a slot that already has a different target voice-over, are **conflicts**: they stay as they are until you pick in the row. A slot already bound to the matching asset shows as unchanged. Assets that match nothing are listed with the reason.
- Every binding is read back after it is set.

### Tests

`Tests/` is an articy-free console test suite for the naming and matching rules (`dotnet run -c Release` inside it). It includes synthetic projects harder than real ones and was checked against deliberately broken versions of the rules.

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

### Find Duplicate Translations

Scans every DialogueFragment's localized text properties and groups together fragments whose **non-reference-language** text is byte-identical. Useful for spotting copy-pasted translations or accidentally-shared lines that should differ.

- One group per `(language, shared-text)` pair. Each member row shows its own reference-language text alongside the shared offending text, so it's easy to see when the duplicate is suspicious (different reference texts → probable bug) versus expected (all members say the same thing in the reference language too).
- **Reference language** picker — defaults to the project's primary language. Changing it re-runs the scan with the new language excluded from the offending side.
- **Comparison rules** (re-runs the scan): case-insensitive, collapse whitespace, include groups where every member shares the same reference text (off by default — those are usually justified duplicates).
- **View filters** (no rescan): offending language, character (speaker), navigator path.
- Per-row buttons: **Clear** wipes just this fragment's translation in the offending language; **Clear from others** wipes everyone *except* this row.
- Footer **Clear all filtered offending texts** — bulk-clears every visible row's translation, with a confirmation showing the breakdown by language. Reference-language text is never touched.

### Find Multi-bound Voice-Overs

Inverts the audit's *Overlapping* view into a per-asset listing: one entry per audio asset that's referenced by more than one DialogueFragment, with a sub-row for each binding.

- Per-asset header shows display name, file name, on-disk size, full path, plus **▶ Play** (uses the system audio decoder) and **Show VO Asset** (jump to the asset in Articy).
- Per-binding sub-rows show speaker, language, property, fragment ID, path, line text, plus **Clear** (unbind just this slot) and **Clear from others** (keep this binding, unbind all others on this asset).
- Filters: language, character, navigator path. Optional toggle to also flag assets bound to the same fragment via multiple property/language slots (off by default — only inter-fragment overlaps are surfaced).
- Clearing uses the API-supported null assignment (`text.VoiceOverReferences[culture] = null`), confirmed against the live `LocalizationHelper.SetAudioSample` chain.

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
3. Commands appear under **Firecrest Roost: Voice-Over Tools** in the ribbon (global scope, e.g. "Rename All Voice-Overs", "Clean Up Orphaned Voice-Overs", "Audit Voice-Overs", "Find Duplicate Translations", "Find Multi-bound Voice-Overs") and under the same submenu in right-click context menus for selections (e.g. "Rename Selected Voice-Overs", "Remove Translations and Voice-Overs").

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
