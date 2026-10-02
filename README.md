# Simple-AI-Tag-Tool

**A dataset refinement tool for LoRA and other image-training captions.** Step through your images one at a time, see each caption next to its image, and see at a glance which tags are there that you want, which are missing, and which shouldn't be there at all. Fix them as you go and save.

Simple-AI-Tag-Tool started as a fork of [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager) by starik222, and it keeps everything that tool does. But it is built for a different job. BooruDatasetTagManager is centred on *creating* tags: generate them with an autotagger, tidy them up, done. Simple-AI-Tag-Tool is centred on *refining* them: going back over a dataset again and again until it really covers what you need it to cover.

## Why refinement needs its own tool

| | Tag creation (the original tool's focus) | Tag refinement (this tool's focus) |
|---|---|---|
| The question | "What tags does this image have?" | "Has this dataset been refined to cover the scenarios I care about?" |
| The workflow | Generate, make a basic edit, done | Review, check, correct, and repeat, many times |
| What you look at | The tag list | The *changes*: what is missing, what is wrong, what should not be there |
| How you check | Read every caption | Tell the tool what you expect and let it colour the answer on every image |
| What comes next | Training | Another refinement pass, by hand or with a local AI model |

When a caption is wrong, the cost shows up hours later in a training run. This tool's job is to make the problems visible *before* that, without opening each caption file and searching it by hand.

## The review loop

1. **Open a dataset folder.** Each image's caption is read from the `.txt` file with the same name.
2. **Say what you are checking for.** Type tags into the *Check for* box, for example `sherlock-holmes, deerstalker, -watermark`. A `-` means "this tag should not be here".
3. **Step through the images.** Space for the next image, Backspace for the previous one. On every image, each tag you are checking for turns **green** when it is as expected and **red** when it is missing or unwanted, in the check list and in the caption itself.
4. **Fix the caption in place.** The caption is a single editable line, exactly as the file holds it.
5. **Save, change what you are checking for, and go round again.**

## What it does today (2.12.0)

- **Review layout:** image preview on the left, the check list and caption in the middle, the dataset list on the right.
- **Captions as text:** each image's tags as one editable comma-separated line, with undo and redo. The original tag grid is one click away, and is used automatically when you select several images.
- **Rules:** "if this tag, then that tag", one row each: `sherlock-holmes` => `deerstalker, -modern clothes`, with `!` `&` `|` and `~"regex"` in the condition. Each image shows what each rule found; rules travel with the dataset in `satt-rules.json`. A character's standard tags are a rule, applied with one right-click.
- **Check for:** wanted and unwanted tags, coloured green or red on every image as it loads. Matching ignores upper/lower case, `_` versus space, and weights. The list is remembered between sessions.
- **IrfanView-style navigation:** Space / Backspace / arrow keys, and Home / End for the first and last image, whenever you are not typing. All the keys can be changed in Settings > Hotkeys.
- **Start and end of the folder:** loop round, stop, or (like IrfanView) be offered the current folder, its parent and its subfolders to continue in.
- **Only this folder:** loading can include or skip subfolders.
- **Folder bar:** above the dataset list, Root / Up / Subfolders (with image counts) and where you are relative to the folder you opened; the list's Path column shows each image's folder as `.` or `.\sub`.
- **Recent folders:** the last five folders are listed in the File menu, the last one reopens at startup, and clearing the list overwrites the stored file before deleting it.
- **Help menu:** the user guide, keyboard shortcuts, and links to this project and the original.
- **Zoom, pan and select in the preview, IrfanView-style:** wheel to zoom, drag a rectangle that stays and can be resized or moved, click inside it to zoom to it, right-drag to pan, double-click to fit; `+` / `-` / `0` / `1` on the keyboard. The title bar shows the zoom and the selection's position, size and ratio.
- **Refine, a local AI pass with a diff:** send the image, an instruction (a *skill* file you can edit) and the current caption to a vision model in LM Studio; the reply comes back beside the caption as chips, green added, red removed, orange changed; take it, keep yours, or pick chip by chip. The model's reasoning streams into a log, the folder's rules go along and are checked on the reply, and nothing touches the file until you save.
- **Image info pane:** under the preview, the file's facts (size, aspect, print size, colours, dates, attributes, position in the dataset) and what is embedded in it: EXIF, the prompt, and for ComfyUI images the prompt of every sampling stage, resolved by the same logic as `comfydbg prompt`, built in. Click any prompt to see it whole and copy it, or send it straight to the check list.

The [user guide](docs/simple-ai-tag-tool/guide.md) explains each of these, with the full shortcut list.

## Where it is going

- **Global conditional rules** (a second tab beside the folder's), and rules inherited from parent folders.
- **Refine on many images at once:** run a skill over the selection or the whole folder and review the proposals one by one.
- **Per-folder skills**, the way rules already belong to a folder.

## Getting started

**Requirements:** Windows, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and Git.

```
git clone https://github.com/djdarcy/Simple-AI-Tag-Tool.git
cd Simple-AI-Tag-Tool
git switch dazzle
build.cmd run "C:\path\to\your\dataset"
```

- `build.cmd` builds the program, `build.cmd run` also starts it, and `build.cmd run "<folder>"` opens that dataset. The program is `BooruDatasetTagManager\bin\Debug\net8.0-windows\Simple-AI-Tag-Tool.exe`.
- The first build also clones and builds [ScreenLister](https://github.com/starik222/ScreenLister) next to this repository. The original tool uses it to take frames from videos. Images work without its native part; video previews need it.
- To build by hand instead: clone ScreenLister next to this repository so the two folders sit side by side, build `ScreenLister/ScreenList/ScreenListerNET.csproj` in Release, then build `BooruDatasetTagManager.sln`.

## Everything from BooruDatasetTagManager is still here

All of the original tool's features keep working and are documented in [its README](docs/upstream/BooruDatasetTagManager-README.md):

- the AutoTagger (AiApiServer) for generating tags with local models;
- tag translation;
- tag lists for autocomplete;
- multi-image editing in the tag grid;
- weighted tags;
- colour schemes;
- interface translations;
- background replacement.

To go back to the original window layout, untick Settings > General > *Preview | Tags | Dataset layout* and restart.

## Branches

- `dazzle`: Simple-AI-Tag-Tool's development.
- `master`: an unchanged mirror of upstream BooruDatasetTagManager. Internal code names (namespace, project and solution files) are deliberately left as they were, so fixes from upstream can still be pulled in.

## Credits and license

Simple-AI-Tag-Tool is based on [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager) by starik222, used under the MIT License; the original copyright notice is kept in [LICENSE](LICENSE). Simple-AI-Tag-Tool's own additions are released under the same license.

Changes between versions are listed in the [CHANGELOG](CHANGELOG.md).
