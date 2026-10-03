# Changelog

All notable changes to Simple-AI-Tag-Tool are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions continue from the BooruDatasetTagManager release this project is based on (2.6.3), so the version number shows the shared base. For changes in BooruDatasetTagManager itself, see its [releases](https://github.com/starik222/BooruDatasetTagManager/releases).

## [Unreleased]

## [2.14.3] - 2026-10-02

### Added

- AI Refine has a fourth way to accept a proposal: **Keep current + add new** keeps every tag of the current caption, in its order, and appends only the items the proposal adds. Items both sides share are not repeated. An item the proposal rewrote keeps the current wording. A chip clicked to drop stays out.
- AI Chat's transcript can be selected and copied, with Ctrl+C or a right-click menu (Copy, Copy all, Select all). It used to hand the focus straight back to the input box. Typing a character while the transcript has the focus carries on in the input box.
- AI Chat's input box grows as you type, wrapped lines included, from two lines up to six. Past six it scrolls with the cursor, and it shrinks back when the message is sent.
- AI Chat's transcript leaves a blank line above each "You:" and "AI:" turn, and above "AI Refine proposed:", and shows the label in bold, so it is easier to see who said what. Tool and status lines stay attached to the turn they belong to. A saved conversation reopens with the same spacing. The "(thought N chars)" note now follows the reply it belongs to rather than coming before it.

### Changed

- Settings > AI, "Where your data lives": each folder is shown on its own line, in the order it is read, in a box you can select and copy from, with its own Open button. A long path no longer runs past the dialog's edge. The skills folder is shown the same way.
- Settings: the AiApiServer tab now sits just before the AI tab, since the AI tab's server is configured there.
- Settings: every string the fork added to the Settings dialog (the AI tab, and the fork's options on the General and UI tabs) now comes from the language files, like the rest of the program. New keys are in English in `en-US.txt`, and the program copies them into the other language files until they are translated.

### Fixed

- Typing a capital letter in a text box no longer runs a shortcut. The original program's Shift+letter shortcuts (Shift+W add tag to selected, Shift+R remove, Shift+F and Shift+G the tag filter) fired while typing in the AI Chat input, the AI instructions or any other box. While the cursor is in something you type in, a plain or Shift-only key now types. Ctrl and Alt shortcuts and the function keys work as before.

## [2.14.2] - 2026-10-02

### Added

- **Each image has its own AI Chat conversation.** Moving to another image puts the current one away and brings that image's back, so several can be going at once. **New session** starts over for the current image only.
- **Kept on disk.** Each image's chat and its latest AI Refine run are kept as two files (`<image>.chat.json` and `<image>.refine.json`), wherever Settings > AI keeps per-image files, and come back when the folder is opened again. A rename or move takes them with the image. A setting keeps them in memory only.
- **Chat starts from Refine.** On an image with an AI Refine run, a new chat begins with that run (its instruction, request and proposal), so the model can discuss or build on it. A setting turns this off.
- **LM Studio export and import.** Both files are in LM Studio's own conversation format, with the tool's details under one extra `satt` key. **Export** on the Chat strip writes this image's chat, or its Refine run, into LM Studio's conversations folder under `simple-ai-tag-tool\<dataset folder>\`. **Import** lists LM Studio's recent conversations, plus *From a file...*. The chosen one becomes this image's chat, and the next message continues it.

### Fixed

- WebP images can be sent to the model. They were sent raw because this build cannot decode WebP with System.Drawing, and LM Studio's server refused them ("'url' field must be a base64 encoded image"). They are now decoded with ImageSharp and sent as JPEG, like other images.
- AI Chat's description of its `get_image` tool no longer mentions the folder's rules and the Check for list, which it stopped returning in 2.14.0.

## [2.14.1] - 2026-10-02

### Added

- **Settings > AI**, one page for how AI Refine and AI Chat behave:
  - **Server:** names the server and the model in use, with a **Test** that says the model, its context length and whether it sees images.
  - **Requests:** token budget, temperature and image size, moved from the UI tab, plus the Think and JSON-reply defaults.
  - **Defaults:** the skill each mode starts with, Chat's tools and ask-first defaults, and the share of context at which Chat drops its oldest turns.
  - **Skills:** your skills folder, and whether the shipped skills are listed.
  - **Data:** where data lives, and where per-image files go.
  - **Context:** what context goes with each request.
- The **OpenAI settings** block on Settings > AiApiServer gains a **Model** list (**Load list** marks the loaded one) and a **Test** button. The server stays configured in this one place.
- **Context** dropdowns on the AI Refine and AI Chat strips send the folder's rules or the Check for list for this session. Settings > AI can make either go every time; all are off by default. Each piece goes with an editable framing sentence that tells the model what it is.
- Under each AI instruction, a line says what will be sent besides the image, the skill and the caption. A piece the skill places itself with `{rules}` or `{checks}` is not added twice.
- **Portable** can be switched on Settings > AI, and takes effect when you save. The settings in use are written to the new place, a settings file already there is kept as a dated copy, and the recent folders follow.
- Changing where per-image files go moves the open dataset's files after asking, keeping their timestamps and overwriting nothing.
- Settings reopens on the tab used last.
- `SATT_HOME` and `SATT_DOCUMENTS` choose other folders for the data and Documents layers.

### Changed

- With the shipped skills hidden, their defaults are no longer written to the program folder.
- AI Chat's oldest-turn trimming follows the setting instead of a fixed 75 %.

## [2.14.0] - 2026-10-02

### Added

- Your own data has a home of its own. An installed copy keeps settings, recent folders, saved skills, conversations and logs in `%USERPROFILE%\.satt`. On the first run it copies `settings.json` and `recent-folders.json` from the program folder, leaving the originals in place. It also creates `Documents\Simple-AI-Tag-Tool` for skills you keep yourself, with a link from each folder to the other.
- A `portable` file beside the program keeps everything in the program folder instead, as before, and creates nothing in your profile. If `.satt` also holds settings, the status bar and the AI log say so once.
- AI skill lists are read from your data folder, then from `Documents\Simple-AI-Tag-Tool`, then from the skills shipped beside the program. A skill of yours with the same name as a shipped one replaces it, and deleting yours brings the shipped one back. **Save as...** writes to your data folder. The status line says which folder a skill came from.
- A build guide, `docs/simple-ai-tag-tool/building.md`. It covers the requirements, `build.cmd`, the Visual Studio and command-line routes, a single-file release build, what the program creates when it runs, the LM Studio setup, the AutoTagger service, and troubleshooting.

### Changed

- The layering, data-folder, per-image-file and folder-link code lives in a separate library inside the repository, `lib\Dazzle.Layers`, which has no dependency on the application, so another tool can reuse it.
- `build.cmd` shows errors only again in terminals where .NET uses its newer terminal logger, which ignored the errors-only option and printed about forty of the original project's long-standing warnings.

### Removed

- The **Send rules** toggle on the AI Refine strip, since the rules and the Check for list are no longer sent.

### Fixed

- AI Refine no longer sends the folder's rules or the Review mode's Check for list with every request. The model read the Check for list as tags to add, so a list left over from another dataset put unrelated tags into proposals. AI Chat's `get_image` tool leaves them out too. A Chat skill can still include them on purpose with `{rules}` and `{checks}`. Choosing what is sent automatically will move to the Settings > AI page.
- The fixed text that AI Refine adds to every skill now only sets the reply's format: one comma-separated line, no labels. It used to say "keep tags that are still true, drop tags that are false, add what is missing", which turned any skill into a general captioning pass.
- **Save as...** in AI Refine and AI Chat keeps the skill you just saved selected, with its text in the box. Before, the list jumped back to the previously selected skill and loaded that skill's text over yours.
- Switching to AI Refine or AI Chat no longer reloads the selected skill, so an unsaved edit to the instruction survives switching modes.

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
