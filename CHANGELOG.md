# Changelog

All notable changes to Simple-AI-Tag-Tool are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions continue from the BooruDatasetTagManager release this project is based on (2.6.3), so the version number shows the shared base. For changes in BooruDatasetTagManager itself, see its [releases](https://github.com/starik222/BooruDatasetTagManager/releases).

## [2.13.1] - 2026-10-02

### Added

- Settings > UI scrolls when its rows run past the dialog's height, instead of needing the dialog resized.
- A tooltip on the Refine or Chat strip goes away when its item is clicked or a dropdown opens, instead of sitting over the list.
- A chevron at the right of the info pane's tab strip hides the pane; a slim "Image info" bar under the preview brings it back (the I key and View > Image info pane still do both).
- AI Refine's **Current caption** chips follow the caption box as you type (after each comma or space, or a short pause), and a **Refresh** button beside Play/Stop redraws them on demand.
- The Refine strip's tooltips go away as soon as the cursor leaves the item (they stayed their full 20 s), and stay at most 15 s.
- The middle pane's modes are labelled **Review**, **AI Refine** and **AI Chat**, and the instruction dropdown **AI skill**, so the AI sections read as such.
- The info pane's **File name** row can be edited in place: double-click it (or right-click > Rename file...), type the new name, Enter. The caption file is renamed with it, the dataset list follows, and the rename is journaled, so Chat's *Undo last change* reverses it.

## [2.13.0] - 2026-10-02

### Added

- **Chat: an assistant that acts.** A third mode of the middle pane. A conversation with the model about the current image in which it can set the caption, rename the file or move it, through five tools it is told about (`get_image`, `set_caption`, `rename_image`, `move_image`, `list_images`). A caption change goes into the caption box like a typed edit; a rename or move happens on disk at once with the caption file following, and the dataset list, title bar and info pane update. Every change is journaled with **Undo last change**. The Result panel shows the file, folder and caption as they stand and the last change; the transcript shows you, the model and its tool actions in three colours; a context meter shows the session's tokens against the model's loaded window, and the oldest turns are dropped above three quarters of it. The image is sent once per session and again when the current image changes. Toggles: Think, Tools (off = talk only), Ask before file changes.
- **Chat skills** in `skills\chat` with placeholders `{caption}`, `{refined}`, `{file}`, `{folder}`, `{rules}`, `{checks}`, filled in when a session starts; two defaults: *Assistant* and *Name from template* (`<subject>__<scene>__<objects>` from the caption and Refine's proposal).
- Multi-turn, tool-calling support in the LM Studio client (streamed tool-call fragments assembled, `role: tool` results, usage from `stream_options`).

### Changed

- The Refine strip's tooltips are wrapped blocks shown for 20 seconds instead of one long line that vanished.
- The transcript renders the model's `**bold**` and `` `code` `` instead of showing the marks.

## [2.12.0] - 2026-10-02

### Added

- **Refine: a local AI pass with a diff.** The middle pane's top half now has two modes, **Review** (rules and the Check for list, as before) and **Refine**. Refine sends the image, an instruction and the current caption to a vision model in LM Studio (or any OpenAI-compatible server; the address, key, model and timeout are the existing Settings > AutoTagger > OpenAI values) and shows the reply beside the current caption as coloured chips: green added, red removed, orange changed, grey unchanged; a sentence item carries a word-level diff in its tooltip. *Take proposal*, *Keep current*, or click chips to drop or keep individual items and *Apply chip choices*. Accepting writes to the caption box only; Ctrl+S saves as always.
- **Skills:** the instruction comes from a dropdown of files in a `skills` folder beside the program (two are written on first use: *Describe the subject* and *Inventory the objects*); the text is editable in place and *Save as...* adds a new skill. The list ends with *Load a file...* (any text file as the instruction) and *Open skills folder...* (edit the files in your own editor).
- **Think / Send rules / Schema** toggles: let the model reason first (on by default; off sends `reasoning_effort: none`), include the folder's rules and the Check for list in the request (on by default), and ask for the reply as strict JSON (on; falls back to free text if the server refuses). The model's reasoning streams into the status line and the **Logs** window, which keeps every run's request, reasoning, reply and errors.
- A proposal is checked by the same rules engine as the caption before it is shown; rule violations appear in the status line.
- The middle splitter fits itself when the Refine pane renders: before a run the chip area is just tall enough for the current caption and the caption box below gets the room to type; after a run the chip columns grow to fit the proposal as room permits, the caption box keeping the height its lines need. Dragging it by hand holds until the next image.
- Settings > UI gains a **Refine** group: max reply tokens (4096), temperature (0.3) and the image's long side when sent (1024 px). The server address, key, model and timeout are the AutoTagger tab's OpenAI values. A circled **?** on the Refine strip opens the guide's Refine section; every toggle explains itself on hover.
- **Play / Stop** (a green triangle and a red square): Stop cancels the request, and LM Studio stops generating when the connection closes. Before a run the server is probed: a model that is not loaded is refused rather than loaded from disk, and a context window above 32k tokens is flagged (measured: 0.27 tokens/s at 111k vs 4 s per image at 32k on the same model).

## [2.11.1] - 2026-10-02

### Changed

- The info pane opens on **Preview Extracted Info** for an image that carries a ComfyUI workflow, an embedded prompt, or descriptive metadata (EXIF, XMP, IPTC), with the first positive prompt already shown in the detail box; an image with none of these opens on Preview Info. (Structural chunks every file has, such as PNG-IHDR or an ICC profile, do not count.)

### Fixed

- "Prompt (embedded)" no longer shows the image's own caption file: BooruDatasetTagManager's metadata reader falls back to the sidecar `.txt` when an image has no embedded parameters, so every captioned image appeared to carry a prompt. That text is the caption, and is now left out.

## [2.11.0] - 2026-10-02

### Added

- **Explorer mode for the Dataset pane:** a folder bar shows the loaded folder relative to the one you opened, with Root, Up and a Subfolders list (each with its image count); clicking one loads that folder. The Browse-folders dialog remains the keyboard route.

### Changed

- The dataset list's path column is headed **Path** and shows the image's folder relative to the loaded one (`.` or `.\sub`) instead of the full path; the full path is in the cell's tooltip. The Name and Path columns use a slightly smaller font so the list needs less width.

### Fixed

- Loading a folder whose subtree contains a directory the program cannot enter (for example the `Application Data` junction in a user profile) no longer crashes with "Access to the path ... is denied"; the inaccessible directory is skipped. A failure while navigating from the folder bar now shows a message instead of the crash dialog.

## [2.10.0] - 2026-10-02

### Added

- **Rules pane** above the Check for box: one row per rule, `If` condition and `Then` tags, with a live Result (ok / missing / should not be here / dormant / conflict / error). Conditions use `!` `&` `|`, parentheses, quotes and `~"regex"`. Rules are saved per dataset folder in `satt-rules.json`, one `condition => tags` line per row, verbatim, so the file can be hand-edited. Right-click a rule to add its missing tags to the caption.
- **Tag matching setting** (Settings > General): Strict (a term equals a whole tag, the default and the previous behaviour) or Lazy (a term may be a word or phrase inside a tag).
- The Browse folders dialog can be resized; its size is remembered.
- The Extracted tab opens on the first stage's POSITIVE prompt.

### Changed

- **Show in folder** opens the folder with the shell's folder handler (a replacement lister such as Directory Opus, when installed); "Open in Explorer with the file selected" is on the button's right-click.
- Clicking in the info pane's tree or list hands the keyboard back to the dataset list, so the arrow keys keep navigating images.

## [2.9.0] - 2026-10-02

### Added

- **Zoom, pan and selection in the preview, IrfanView-style.** Wheel zooms about the pointer; left-drag draws a selection rectangle that stays, with handles to resize it and dragging to move it; a click inside it (magnifier cursor) zooms to it; right-drag pans with a hand cursor; double-click fits. Keys **+** / **−**, **0** fit, **1** 100%, **Enter** zoom to selection, **Esc** clear it, all remappable. The title bar shows the displayed size, zoom percentage and the selection's position, size and ratio as IrfanView does; the info pane gains live *Zoom* and *Selection* rows. Fit never enlarges a small image past 100%.

## [2.8.0] - 2026-10-01

### Added

- **Image info pane** under the preview (View > Image info pane, or the I key), with two tabs. *Preview Info*: file name and folder, format and compression, pixel size and aspect ratio, print size from DPI, colour depth, unique colours, position in the dataset, load time, created/modified/accessed times, file size and attributes; right-click to copy; buttons to open the file in its default app or show it in its folder. *Preview Extracted Info*: EXIF and other metadata, the embedded prompt, and for ComfyUI images the positive and negative prompts of every sampling stage plus the workflow's version fingerprint, with buttons to copy or save the raw workflow JSON and to run `comfydbg detect` for an installed-version comparison. Slow facts are read in the background and never hold up moving to the next image.
- **Full prompt view and "Collapse repeats":** clicking a node in the Extracted tab shows its whole text in a read-only, selectable box, with "Send to Check for box" on its right-click menu. Collapse repeats (on by default) folds stages and sides that repeat an earlier one and merges fields holding the same text, like `comfydbg prompt --prune`.
- **Native ComfyUI prompt resolver:** the per-stage prompt extraction from `comfydbg prompt`, ported into the program and checked against the Python original on 476 real outputs with no differences. It also reads workflows that BooruDatasetTagManager's own reader could not: every WebP output, and PNGs with damaged trailing chunks.

### Changed

- **Settings no longer claim a restart for every change.** The restart notice appears only when the language, the layout or the preview size changed; changing *Include subfolders* reloads the open dataset on the spot.
- The README and guide examples use `sherlock-holmes` as the sample character.

### Fixed

- When an image cannot be decoded (a WebP without `libwebp.dll`, a video without ScreenLister), the info pane now describes that file instead of silently keeping the previous image's facts.
- Print size no longer uses the screen's DPI for a file that has none; it trusts only a resolution stored in the file, and otherwise says "assumed 96 dpi".

## [2.7.0] - 2026-10-01

The first Simple-AI-Tag-Tool release: a review layout for refining existing captions, on top of BooruDatasetTagManager 2.6.3.

### Added

- **Review layout:** image preview on the left, caption and check list in the middle, dataset on the right. The original layout is still available (Settings > General).
- **Caption as an editable text line:** each image's tags shown as one comma-separated line, exactly as the caption file holds it. Enter, moving to another image, saving or clicking away applies the edit as a single undo step. Ctrl+Z / Ctrl+Y undo and redo typing inside the box. The original tag grid stays on a "Grid" tab and is used automatically when several images are selected.
- **Check for box:** a list of wanted tags and `-unwanted` tags, coloured on every image as it loads, in the list and in the caption: green when as expected, red when missing or unwanted. Matching ignores upper/lower case, `_` versus space, and weights. The list is remembered between sessions.
- **Keyboard navigation, IrfanView-style:** Space / Right and Backspace / Left move between images, and Home / End jump to the first and last, whenever you are not typing. Alt+arrows and PageUp / PageDown work from inside the text boxes, and Esc leaves them. Clicking the preview returns the keyboard to navigation.
- **Navigation keys in Settings > Hotkeys:** every navigation key can be changed, and each keeps its rule (when not typing, anywhere, or inside a text box).
- **Start and end of the folder:** going past the first or last image can loop round (the default), stop, or open a "Browse folders" list of the current folder, its parent and its subfolders, with image counts. In that list, the same key you were pressing continues, and Left goes up a level. A key held down is not taken as an answer, and does not keep stepping once the next folder opens, so a newly opened folder always starts at its first image (or its last, when going backwards).
- **Option to skip subfolders** when loading a dataset (Settings > General).
- **Recent folders:** File > Recent folders lists the last five folders, and the most recent reopens at startup. Clearing the list overwrites the stored file with zeros and random data before deleting it. Both can be turned off (Settings > UI).
- **Open a folder from the command line:** `Simple-AI-Tag-Tool.exe "<folder>"`.
- **Help menu:** user guide, keyboard shortcuts, About, and links to this project and to BooruDatasetTagManager.
- **User guide:** `docs/simple-ai-tag-tool/guide.md`.
- **`build.cmd`:** builds the program, optionally starts it on a folder, and fetches the ScreenLister dependency on first use.

### Changed

- **Renamed to Simple-AI-Tag-Tool:** the window title, the program file (`Simple-AI-Tag-Tool.exe`) and the product name. Internal code names are unchanged.
- **README rewritten** around refining datasets. BooruDatasetTagManager's original README is kept unchanged in `docs/upstream/`.
- **The View menu's show/hide pane items** now work in both the new and the original layout.
