# Articy Voice-Over Tools

An articy:draft X plugin that gives every voice-over file in a project a consistent name, carries the files out to the actors and back, and binds translated recordings to the right lines. Its naming matches what the Pixel Crushers Dialogue System for Unity looks for, so the same names work from the articy project to the game. Internal plugin id: `Kurisu.VoiceOverTools`.

## The workflow it is built for

The plugin exists so that a line's voice-over file can be found from the line itself, in articy and in Unity alike. articy keeps a recording under whatever name it was imported with, so the only thing tying a file to its line is the binding inside the articy project. That binding does not reach Unity: when the Dialogue System imports the project, it brings the lines across but nothing about which audio belongs to which. So the link has to live in the file's name.

The plugin names each file after the dialogue fragment that speaks it: the fragment's articy id in hex, then the language, as in `0x0100000300001B4D_en`. The Dialogue System's import carries the same id with every line, so it can be set up to look a line's audio up by that name.

One detail stands in the way. articy appends a short tag of its own to every file it stores, so on disk the file is `0x0100000300001B4D_en(1C49).wav`. The Dialogue System cannot know that tag, because its import holds no trace of it, and it would not find a file that still carries it. So the tag stays while the files live in articy, which requires it and would add it back anyway, and the plugin's tools match names with or without it. [Export](#export) can take it off the copies it writes, ready for Unity.

Much of this could be done by forking the Dialogue System's articy importer to carry the bindings across. The approach here is the opposite: change each tool as little as possible and work with the way it is designed. On the articy side, that is a plugin that names the files. On the Unity side, it is two small scripts that use hooks the Dialogue System already offers, a custom entrytag and a custom sequencer command.

A project goes through these steps, each covered below:

1. **Name the recordings.** [Rename Voice-Overs](#rename-voice-overs) renames every bound file to its fragment's id and language.
2. **Send the lines out and bring them back.** [Export / Import Voice-Overs](#export--import-voice-overs) copies the files under a dialogue to a folder, organised for the people recording, and puts edited or re-recorded files back in place by name. On export, the tag can be stripped and the main language can drop its language suffix, so the files are named the way Unity will look for them.
3. **Add a language.** Once a new language's audio is imported into articy, [Match Language Voice-Overs](#match-language-voice-overs) binds each file to its line by name.
4. **Play them in Unity.** The two scripts in [`Unity/`](./Unity) make the Dialogue System name each line after its articy id, and fall back to the main-language file when a translation has not been recorded yet. See [The Unity side](#the-unity-side).

The other commands keep a project tidy along the way: an audit of missing and broken voice-overs, a search for duplicate translations and for audio bound to several lines, a cleanup of audio nothing uses, and a command that strips translations from a selection.

## Installation

Download the latest `.mdk` from [`releases/`](./releases) and install it through articy:draft X's **Package Manager**, the usual way a plugin is installed (articy's [Packaging a plugin](https://www.articy.com/adxdevkit/html/packaging_a_plugin.htm) page covers it). The commands appear in the ribbon under **Firecrest Roost: Voice-Over Tools**, and the ones that act on a selection appear under the same name in the right-click menu.

Each command is reached from one or both menus:

| Command | Where it is |
| --- | --- |
| Export / Import Voice-Overs... | Ribbon, and right-click on any object to pick it |
| Match Language Voice-Overs... | Ribbon, and right-click on a dialogue or an asset folder to pick it |
| Rename All Voice-Overs | Ribbon |
| Rename Selected Voice-Overs | Right-click |
| Audit Voice-Overs | Ribbon |
| Find Duplicate Translations | Ribbon |
| Find Multi-bound Voice-Overs | Ribbon |
| Clean Up Orphaned Voice-Overs | Ribbon |
| Remove Translations and Voice-Overs | Right-click on a flow, dialogue or dialogue fragment |

## Rename Voice-Overs

Rename gives each voice-over file its fragment's name, `0x<id>_<language>`, and shows the whole plan before it changes anything. **Rename All Voice-Overs** covers every dialogue fragment in the project, and **Rename Selected Voice-Overs** covers what is selected. Alongside the file, the asset's display name is rewritten to say where the line is and what it says: its path in the project, its number in its dialogue, the start of its text and the file name, so the asset browser shows what each file holds.

Each voice-over in the plan falls into one of five categories:

| Category | What happens to it |
| --- | --- |
| Already correct | Nothing, the file and display name already match |
| Will rename | The file is renamed and the display name rewritten |
| Display name update | Only the display name changes, the file already has the right name |
| Target file exists | Skipped, a file of that name is already there |
| Source file missing | Skipped, the stored file is gone from disk |

The badges above the list filter it by category, and each row shows the file name and the display name before and after. Selecting a row and pressing **Show Fragment** or **Show VO Asset** jumps to it in articy's main view while the window stays open. **Dry run** only previews, **Execute rename** applies. When a renamed asset is bound to more than one fragment, the plan warns first: one file can carry only one fragment's name.

## Export / Import Voice-Overs

Export / Import takes the voice-over files under one object out of the project, for recording or editing, and puts the edited files back. Open it from the ribbon, or right-click an object to have it picked already. **Pick...** opens articy's own object picker. Both directions share the object, a folder, the languages to include and a **main language**.

### Export

Export copies every voice-over under the object to the folder, after a preview. Its options shape the file names and the folders:

| Option | What it does |
| --- | --- |
| Main language | The language whose files lose their `_language` suffix, so `0x…1B4D_el(1C49).wav` becomes `0x…1B4D(1C49).wav`. A name without a suffix is left as it is, and no main language leaves every name as stored |
| Strip the (XXXX) tag | Whether the tag articy appends to stored files is taken out of the name |
| One top folder per language | Whether each language gets a top folder of its own |
| Hierarchy levels | How many levels of the project's hierarchy below the object become folders, from 0 to 6 |
| Speaker folder | Where the speaker's name becomes a folder: None, Before the hierarchy or After the hierarchy. A line without a speaker goes to `(no speaker)` |
| Existing files | What happens to a file already in the folder: Skip or Overwrite |

A voice-over that several fragments share is written once. When two different files would land on the same name, both are reported and neither is written.

### Import

Import searches a folder and its subfolders and replaces each voice-over whose stored name matches a file. A file that keeps its `(XXXX)` tag must match exactly, and one without it matches ignoring the tag, with or without the main language's suffix. A replaced asset keeps its display name, description and external id.

The import decides nothing on its own when a match is unclear. These cases are listed as **conflicts**, each with a choice, and each stays skipped until you pick:

- one file matching several voice-overs
- several files matching one voice-over
- a file in a different format from the stored one

A file identical to the stored one is skipped. A voice-over shared by several fragments is flagged, because replacing it changes the audio of all of them.

## Match Language Voice-Overs

Match Language binds a new language's audio, already imported into articy, to the fragments it belongs to, by name. Pick where the voice-overs are (right-click a dialogue to pick it), the asset folder holding the new audio (right-click it to pick it, with its subfolders optional) and the target language, then preview.

A fragment's slot answers to the names of the voice-overs it already has in its other languages, with the `(XXXX)` tag and a trailing `_language` ignored. So `name_el`, `name_en` and `name` all match the same slot, `_de` counts for `de-DE`, and a suffix that is not one of the project's languages (`_SC`, `_final`) stays part of the name. When several assets share a name, one named for the target language wins, then one with no language suffix. An asset named for another language is never used.

Each row of the preview says why it matched and shows the file name next to the name articy displays, so the matches can be checked by eye. A row is in one of four states:

| State | What it means |
| --- | --- |
| Ready | A clean match, bound when its row is ticked |
| Unchanged | The slot is already bound to the matching asset |
| Conflict | Two assets match equally well, or the slot already holds a different asset for the target language. It stays as it is until you pick an asset in the row |
| No match | An asset in the folder that no slot takes, with the reason |

A row also notes when one asset goes to several slots, which is normal for a line used in several places. **Bind ticked** applies the ticked matches and the conflicts you picked, and every binding is read back from the project after it is set.

## Audit Voice-Overs

The audit changes nothing. It scans every dialogue fragment and lists three kinds of problem:

| Problem | What it means |
| --- | --- |
| Missing | A text has voice-over turned on but no asset for one or more languages |
| Corrupted | The asset is broken: an invalid reference, no file path, a file gone from disk, or an empty file |
| Overlapping | One audio asset is bound to several fragments. Sometimes that is intended, so the audit only shows it |

The window works like Rename's: badges filter by problem, and **Show Fragment** and **Show VO Asset** jump to the row's object.

## Find Duplicate Translations

This search finds fragments whose translation in some language is exactly the same text, which is how a copy-pasted or accidentally shared translation shows up. It compares every language except a **reference language**, the project's main language unless you pick another, and groups fragments by language and shared text. Each row shows its own reference-language text beside the shared one, so a group whose reference texts differ, the likely mistake, stands out from a group that says the same thing in every language.

The comparison can ignore case and collapse whitespace, and can include groups whose members all share their reference text too. Those are usually fine, so they are left out by default. Changing any of these runs the search again. The view filters by language, by speaker and by navigator path without searching again.

Three actions clear text, and none of them touches the reference language:

| Action | What it clears |
| --- | --- |
| Clear | This row's translation in the duplicated language |
| Clear from others | The same translation in every other row of the group |
| Clear all filtered offending texts | Every visible row's duplicated translation, after a confirmation that breaks the count down by language |

## Find Multi-bound Voice-Overs

This search lists each audio asset bound to more than one fragment, with one row under it for each binding. It is the audit's Overlapping list turned round, so that one asset's bindings sit together.

Each asset shows its display name, file name, size on disk and full path, with **▶ Play**, through the system's audio player, and **Show VO Asset**. Each binding shows its speaker, language, property, fragment id, path and line text, with two actions:

| Action | What it does |
| --- | --- |
| Clear | Unbinds this slot only |
| Clear from others | Keeps this binding and unbinds every other one on the asset |

The list filters by language, speaker and navigator path. An option also lists assets bound to the same fragment in several slots, which is off by default, so only assets shared between fragments show.

## Clean Up Orphaned Voice-Overs

Cleanup finds audio assets that no dialogue fragment uses as a voice-over, and offers three ways to deal with them:

| Choice | What it does |
| --- | --- |
| Dry run | Lists them and changes nothing |
| Delete from articy only | Removes the assets from the project. articy deletes their files when the session closes, and Undo brings them back until then. The recommended choice |
| Delete from articy and disk immediately | Removes the assets and their files at once, after a confirmation. It cannot be undone |

## Remove Translations and Voice-Overs

Right-click a flow, a dialogue or a dialogue fragment to strip every translation except the main language's, with its voice-overs, from the selection. The voice-over slots are pointed at a silent placeholder asset, which articy removes when the session closes.

## The Unity side

Two scripts in [`Unity/`](./Unity) finish the workflow in the Pixel Crushers Dialogue System for Unity, which imports articy projects. Copy them into your project's `Assets` folder, which needs the Dialogue System installed.

The Dialogue System finds a line's audio through its **entrytag**, a name it builds for each line. In a sequence, `entrytag` stands for that name, and `entrytaglocal` for the same name with the current language appended (`_en`), except in the default language, where it is the plain entrytag. The scripts make the entrytag the line's articy id, which is the name the plugin gives its file.

### CustomEntrytag

`CustomEntrytag` names each line's entrytag after one of its fields. The Dialogue System's articy importer writes every line's articy id into a field called **Articy Id**, which is the default.

To set it up:

1. Add the component to any active object in your scenes, the Dialogue Manager's own object for instance.
2. On the Dialogue Manager, set **Display Settings > Camera & Cutscene Settings > Entrytag Format** to **Custom**.

The inspector says when no Dialogue Manager is open or its format is not Custom, and shows whether the component is hooked in, with a **Hook Now** button. It hooks in edit mode as well, so the Dialogue Editor's voice-over exports use the same names.

### AudioWaitCustom

`AudioWaitCustom` is the Dialogue System's `AudioWait` with one addition: when a clip named for the current language does not exist, it plays the same name without the language suffix. A line with no recording in the current language then plays in the main language, provided the main-language files carry no suffix, which is what the main-language option of [Export](#export) gives.

Use it in a sequence, or as the Dialogue Manager's **Default Sequence**:

```text
AudioWaitCustom(entrytaglocal)
```

With Addressables, the check for the language's clip loads it once and releases it again, so it holds no reference of its own.

## Build from source

Building needs Windows, the .NET 8 SDK and articy:draft X. The project references the `Articy.MDK` NuGet package, version 2.4.1, which matches articy 4.4.1.

```bash
dotnet build -c Release
```

The build lands in `bin/Release/`. A `.mdk` is a plain zip of that folder's files, so either package the folder with articy's **DevKit Tools** plugin, or zip its contents and rename the archive to `.mdk`.

### Tests

[`Tests/`](./Tests) is a console suite for the naming and matching rules that runs without articy: run `dotnet run -c Release` inside it. Alongside real-world names it covers synthetic projects harder than real ones, and it was checked against deliberately broken versions of the rules to show that it fails when it should.

The plugin also carries an in-articy self-test of Export / Import, for maintainers. It replaces one voice-over and restores it, so it is meant for a copy of a project, and it is hidden unless the environment variable `FIRECREST_ARTICY_DEV` is set to `1` or `true` for the user running articy.

## License

MIT, see [LICENSE](./LICENSE).
