# Changelog

All notable changes to Simple-AI-Tag-Tool are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions continue from the BooruDatasetTagManager release this project is based on (2.6.3), so the version number shows the shared base. For changes in BooruDatasetTagManager itself, see its [releases](https://github.com/starik222/BooruDatasetTagManager/releases).

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
