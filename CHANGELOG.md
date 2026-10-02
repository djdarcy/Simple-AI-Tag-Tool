# Changelog

All notable changes to Simple-AI-Tag-Tool are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions continue from the BooruDatasetTagManager release this project is based on (2.6.3), so the version number shows the shared base. For changes in BooruDatasetTagManager itself, see its [releases](https://github.com/starik222/BooruDatasetTagManager/releases).

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
