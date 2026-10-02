# Simple-AI-Tag-Tool user guide

Simple-AI-Tag-Tool is a caption review tool for LoRA and other image-training datasets. You open a folder of images whose captions sit beside them in `.txt` files, step through the images one at a time, and see at a glance which tags each caption has that you want, and which it has that you don't.

It is a fork of [BooruDatasetTagManager](https://github.com/starik222/BooruDatasetTagManager) by starik222 (MIT License). Everything that tool does is still here; this fork adds a review layout on top. For the original features (autotagger, translation, tag grid operations), see the [upstream README](https://github.com/starik222/BooruDatasetTagManager#readme).

## The window

| Pane | What it shows |
|---|---|
| **Left: preview** | The current image. The *All / Common tags* and *AutoTagger preview* tabs are still here, behind the preview. |
| **Middle, top: Check for** | Your list of tags to check every image against. |
| **Middle, bottom: tags** | The current image's caption as one editable, comma-separated line, exactly as the `.txt` file holds it. The original tag grid is on the *Grid* tab. |
| **Right: dataset** | The images in the folder. Click one, or step through them with the keyboard. |

To go back to the original layout, untick Settings > General > *Preview \| Tags \| Dataset layout* and restart.

## Opening a dataset

- **File > Open folder**, or start the program with a folder: `Simple-AI-Tag-Tool.exe "C:\path\to\dataset"`.
- Captions are read from the file with the same name as the image and the extension set in Settings > General (default `.txt`).
- Subfolders are included by default. To load only the chosen folder, untick Settings > General > *Include subfolders when loading a dataset*.
- The **folder bar** above the dataset list shows where you are relative to the folder you opened (`testdata\sub`), with **Root** (back to that folder), **Up** (its parent) and **Subfolders**, a list of the current folder's subfolders with their image counts; click one to load it. Opening a folder any other way starts a new root.
- In the dataset list, the **Path** column shows each image's folder relative to the loaded one: `.` for the folder itself, `.\sub` for a subfolder. The file name is in the Name column; hover the path cell for the full path.
- **File > Recent folders** lists the last 5 folders you opened, and the program reopens the most recent one when it starts (unless you give it a folder on the command line).

### Recent folders and privacy

The recent-folders list is kept in `recent-folders.json` next to the program, in a separate file from `settings.json`, and nowhere else in the program. Nothing is written to the registry.

- **File > Recent folders > Clear recent folders** overwrites that file with zeros, then with random bytes, and then deletes it.
- Settings > UI > *Remember recent folders*: untick it to stop keeping the list at all (this also clears the stored list). *Reopen the last folder at startup* controls the startup behaviour on its own.

This is best effort. Solid-state drives, NTFS journaling, backups and shadow copies can keep older copies of any file in places no program can reach. Separately, the Windows folder picker (File > Open folder) keeps its own per-program note of the last folder it showed, in the registry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU`; that belongs to Windows, not to this program.

## Checking tags

Type the tags you are reviewing for into **Check for**, separated by commas. Put `-` in front of a tag you do **not** want:

```
sherlock-holmes, deerstalker, 1man, -watermark, -blurry
```

On every image, both boxes are coloured straight away:

| You wrote | The image's caption... | Check for box | Tags box |
|---|---|---|---|
| `deerstalker` | has `deerstalker` | green | `deerstalker` green |
| `deerstalker` | lacks `deerstalker` | red | — |
| `-watermark` | lacks `watermark` | green | — |
| `-watermark` | has `watermark` | red | `watermark` red |

Matching ignores upper/lower case, treats `_` and space as the same (`long_hair` = `long hair`), and ignores weights (`(beard:1.2)` = `beard`). The Check for list is remembered between sessions.

## Rules: "if this tag, then that tag"

Above the Check for box is the **Rules** pane: one row per rule, with an **If** condition, a **Then** list of tags, and a read-only **Result**.

| If | Then | Meaning |
|---|---|---|
| `sherlock-holmes` | `deerstalker, pipe` | when the character is tagged, these should be too |
| `sherlock-holmes` | `-modern clothes` | ...and this should not be |
| `!sherlock-holmes` | `-deerstalker` | when the character is absent, the hat should be absent too |
| `(1girl \| 1boy) & !solo` | `duo` | a condition can combine tags |
| `~"\d+/\d+/\d{4}"` | `has date` | `~"..."` matches a regular expression against any tag |
| *(empty)* | `beard, -watermark` | no condition: always checked, the same as the Check for box |

The condition language is small: `!` (not), `&` (and), `|` (or), parentheses, double quotes around a tag that contains spaces or those characters, and `~"regex"`. The words `and`, `or`, `not` work too. A tilde is only a regex when the quote follows it directly; `~mytag` is just a tag.

On every image, the Result column says what the rule found: **ok**, **dormant** (the condition is false, so the rule says nothing here, shown grey), what is **missing** or **should not be here** (red), or **conflict** (orange) when one rule requires a tag another forbids. A row whose condition does not parse shows the error in red and keeps your text. The tags a rule wants or forbids are coloured in the caption exactly like the Check for entries. Right-click a row for **Add missing tags to caption**, which appends the rule's missing tags in one step; that is how a character's standard tag set is applied.

Rules belong to the dataset: they are saved as `satt-rules.json` in the folder, one `condition => tags` line per row, written when you move to another image, change folder or close. You can edit that file in Notepad; the grid shows whatever it holds. A folder with no rules has no file.

**Tag matching** (Settings > General) applies to the Check for box, the rules and the colouring: **Strict** means a term must equal a whole comma-separated tag; **Lazy** means it may appear as a word or phrase inside a tag, so `beard` matches `raymond-cole beard` and `hair` matches `long hair` but not `hairy`.

## AI Refine: a local AI pass

The top of the middle pane has three modes: **Review** (the rules and the Check for list), **AI Refine** and **AI Chat**. Refine sends the current image, an instruction and the current caption to a vision model running locally in [LM Studio](https://lmstudio.ai) and shows what came back as a diff against the caption, so you can see exactly what would be added, removed or changed before accepting any of it.

**Setup:** start LM Studio's server with a vision model loaded (a model whose card says it accepts images; the tool checks). The address, API key, model name and timeout are the ones under Settings > AutoTagger > OpenAI (default `http://127.0.0.1:1234/v1`). Leave the model name empty to use whatever is loaded. Settings > UI has a *Refine* group for the reply's token budget (thinking counts against it), the temperature, and how large the image is when sent (1024 px on the long side by default; larger is slower and rarely captions better). Load the model with a context window of 16k-32k tokens: a very large window makes every run slow (measured on a 27B model: 10 minutes for 165 tokens at 111k, 4 seconds per image at 32k).

**Running:** pick an **AI skill** (an instruction file from the `skills` folder beside the program, editable in the box; *Save as...* keeps your edits as a new skill; the last two entries of the list, *Load a file...* and *Open skills folder...*, bring in any text file as the instruction or open the folder so you can edit the files in your own editor), then **Play**. The pane sizes itself: before a run, the caption box below gets the room; after a run, the two chip columns grow to fit the proposal as room permits. The status line shows the model's progress; **Stop** cancels. The toggles: **Think** lets the model reason before answering (on by default, and worth keeping on when rules are involved); **Send rules** includes this folder's rules and the Check for list in the request; **Schema** asks for the reply as strict JSON, which keeps the model from adding chatter.

**Reading the result:** the current caption on the left, the proposal on the right, one chip per tag: green = added, red = removed, orange = changed (the same tag written differently, or a sentence with words changed; hover for the word diff), grey = unchanged. The proposal is also checked against the folder's rules, and any violation is named in the status line. Then:

- **Take proposal** replaces the caption with the right side; **Keep current** leaves it.
- Or click chips: on the right, a click drops that item (struck through); on the left, a click keeps a removed item (bold). **Apply chip choices** composes the result.

Accepting only writes to the caption box, as if you had typed it; nothing reaches the file until you save (Ctrl+S). The proposal is kept per image while the folder is open, so you can move on and come back. **Logs** opens a window with every run's request, the model's reasoning, the reply and any error, with Copy.

## AI Chat: an assistant that acts

The third mode of the middle pane. Where Refine proposes a caption for you to accept, Chat is a conversation with the model about the current image in which it can *do* things: set the caption, rename the file, move it to a folder. It sees the image, and it knows what Review and Refine produced.

**The pane:** the skill (the system instruction; two are written on first use, *Assistant* and *Name from template*), a **Result** panel showing the file name, folder and caption as they stand, the last change the model made with an **Undo last change** button, and a context meter; below, the transcript (you in blue, the model in green, tool actions in grey) and an input line. Enter sends; Shift+Enter is a new line. **New session** starts the conversation again with the instruction as it is now.

**What the model can change** -- its five tools, which it is told about: `get_image` (name, folder, caption, the latest Refine proposal, the folder's rules, the Check for list), `set_caption`, `rename_image`, `move_image` (inside the dataset folder only; the folder is created if needed), `list_images`. A caption change goes into the caption box like a typed edit and is saved with Ctrl+S; a rename or move happens on disk at once, the caption file moving with the image, and the dataset list, title bar and info pane follow. Every change is journaled; **Undo last change** reverses the most recent one, as many times as there are changes. Turn **Tools** off to make the model talk only; turn **Ask before file changes** on to be asked before each rename or move.

**Skills can use placeholders** filled in when a session starts: `{caption}`, `{refined}` (Refine's latest proposal), `{file}`, `{folder}`, `{rules}`, `{checks}`. That is how the three modes chain: review the tags, refine them from the image, then a skill such as *Name from template* (`<subject>__<scene>__<objects>`) builds the file name from both.

**The window:** the meter shows the session's tokens against the model's loaded context. Above three quarters, the oldest turns are dropped (the instruction is kept) and the transcript says so. The image is sent once per session and again when you move to another image.

## Editing tags

Click into the tags line and edit it like any text. Your edit is applied to the image when you:

- press **Enter** (Shift+Enter inserts a line break, for files with several captions on separate lines),
- click somewhere else,
- move to another image, or
- save.

Nothing is written to disk until you save (**Ctrl+S**, or File > Save all changes), as in the original program. When you select several images at once, the middle pane switches to the *Grid* tab, which edits the tags of all selected images together.

## Zoom, pan and select in the preview

The preview works like IrfanView's window:

| Mouse | Does |
|---|---|
| Wheel | Zoom in or out about the pointer |
| Left-drag on the image | Draw a selection rectangle; it stays until you clear it |
| Drag a handle on the rectangle | Resize it |
| Drag inside the rectangle | Move it |
| Click inside the rectangle (magnifier cursor) | Zoom so the selection fills the preview; the rectangle stays |
| Click outside the rectangle | Clear it |
| Right-drag (hand cursor) | Pan |
| Double-click | Fit the image to the preview again |

The title bar shows the zoom as IrfanView does: `img03.png - Simple-AI-Tag-Tool 2.8.0 (Zoom: 4876 x 6502, 635 %) (Selection: 10, 9; 55 x 41; 1.341)`, the displayed size, the zoom percentage, and the selection's position, size in image pixels and width-to-height ratio. The info pane's *Zoom* and *Selection* rows show the same live.

Keys (when you are not typing; all changeable in Settings > Hotkeys): **+** / **−** zoom, **0** fit, **1** 100%, **Enter** zoom to the selection, **Esc** clear it. Fitting never enlarges a small image past 100%. Moving to another image resets the zoom and clears the selection.

## The image info pane

Under the preview sits a pane with everything about the current image. Press **I** (when you are not typing), or use View > *Image info pane*, to show or hide it; its size and visibility are remembered.

**Preview Info** lists the file's facts: name and folder; format and compression (for example `PNG - Deflate`); size in pixels with the aspect ratio (`768 x 1024 (3:4)`); print size in inches and centimetres from the file's DPI, or at an assumed 96 dpi when the file has none; colour depth; the number of unique colours (counted in the background; shown as "counting..." until done); the image's position in the dataset; how long the image took to load; created, modified and accessed times; file size; and attributes such as read-only or hidden. Right-click a row to copy its value, or everything. *Open in default app* and *Show in folder* do what they say. **Double-click the File name row to rename the file** (or right-click > Rename file...): type the new name and press Enter; the caption file is renamed with it, the dataset list follows, and Chat's *Undo last change* can reverse it.

**Preview Extracted Info** shows what is embedded in the file:

- **EXIF** and any other metadata directories, grouped.
- **Prompt (embedded):** the single positive/negative prompt BooruDatasetTagManager's own reader finds (A1111, NovelAI, ComfyUI and others).
- **Workflow:** for an image made with ComfyUI, the positive and negative prompt text of **every sampling stage**, in execution order, with the text fields each encoder used (for example `clip_l` and `t5xxl` for Flux) and the node they came from. A stage that repeats an earlier one says so. A side that could not be traced is marked *unresolved* with the reason, never guessed. This is the same resolver as [`comfydbg prompt`](https://github.com/djdarcy/comfydbg), ported into the program, so no external tool runs. Below it, **Version fingerprint** lists the ComfyUI frontend and backend versions and the custom-node packages the workflow recorded.
- *Copy workflow JSON* and *Save workflow JSON...* hand you the raw embedded graph. *Compare versions with comfydbg* runs `comfydbg detect` on the file, if you have comfydbg installed, to compare the workflow's versions with what is installed in ComfyUI; the command can be set in Settings > UI.

Everything here is read in the background, so moving between images never waits for it. When an image carries a workflow, an embedded prompt, or descriptive metadata (EXIF, XMP, IPTC), the pane switches to this tab as the image loads, with the first positive prompt shown in the detail box; otherwise it shows *Preview Info*.

**WebP and video files:** decoding WebP needs `win-x64\libwebp.dll` next to the program, which BooruDatasetTagManager's release zip includes but a build from source does not; video previews need ScreenLister's native DLL. When an image cannot be decoded, the pane still shows the file's facts and says why the preview is missing. Workflows embedded in WebP files are read either way.

## Keyboard shortcuts

| Action | Default key | Works... |
|---|---|---|
| Next image | Space, or Right | when you are not typing in a text box |
| Previous image | Backspace, or Left | when you are not typing in a text box |
| First image / last image | Home / End | when you are not typing in a text box |
| Show / hide the image info pane | I | when you are not typing in a text box |
| Zoom in / out | + / − (also numpad + / −) | when you are not typing in a text box |
| Fit the image / show at 100% | 0 / 1 | when you are not typing in a text box |
| Zoom to the selection / clear it | Enter / Esc | when a selection exists and you are not typing |
| Next image | Alt+Right | anywhere, also while typing |
| Previous image | Alt+Left | anywhere, also while typing |
| Next image | PageDown | inside the tags or Check for box |
| Previous image | PageUp | inside the tags or Check for box |
| Leave the tags or Check for box | Esc | inside those boxes; Space then moves between images again |
| Apply the tags line | Enter | in the tags box |
| Undo / redo your typing | Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) | in the tags or Check for box |
| Undo / redo a change to the tag list | Ctrl+Z / Ctrl+Shift+Z | outside the text boxes |
| Save all changes | Ctrl+S | anywhere |

Clicking the preview image moves the keyboard focus to the dataset, so Space and the arrow keys change images straight away.

**Changing the keys:** all the navigation keys above are listed at the bottom of Settings > Hotkeys, next to the original program's shortcuts. Select a row and press the new key. The "works when..." rule belongs to the action, not the key: if you move *Next image* to `F`, `F` still types normally inside a text box.

## At the start or end of the folder

Going forward past the last image, or back past the first, does what Settings > UI > *Going past the first or last image* says:

- **Loop round to the other end** (default).
- **Stop at the first / last image.**
- **Ask for another folder (like IrfanView):** a list of the current folder (selected, so pressing the same key again simply loops), the parent folder `(..)` and each subfolder, with how many images each holds.

| Key in the folder list | Does |
|---|---|
| Space, Right, Backspace, Enter, or double-click | Use the highlighted folder |
| Up / Down | Highlight another folder |
| Left | List the folders one level up |
| Esc | Cancel and stay where you are |

So you can keep pressing Space (or Right) to go round the same folder again, or press Down to pick a subfolder and then Right to open it. Going backwards works the same way: Backspace at the first image asks, Backspace again wraps to the last image, and a folder chosen while going backwards opens on its last image.

## Links

- This project: https://github.com/djdarcy/Simple-AI-Tag-Tool
- The original BooruDatasetTagManager: https://github.com/starik222/BooruDatasetTagManager
