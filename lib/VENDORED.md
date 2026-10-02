# lib\ -- vendored libraries

Libraries written for Simple-AI-Tag-Tool with no reference to the application, so that a second C# tool can lift them by copying the folder, and so that they can later move to their own repositories in the DazzleLib collection (`C:\proj\dazzlelib`) as the C# siblings of the Python stack.

| Folder | Namespace | Mirrors (Python) | What it owns |
|---|---|---|---|
| `Dazzle.Layers\` | `Dazzle.Layers` | the overlay semantics measured in the 2025-11 WinFsp + rclone union experiment; `dazzle-preservelib`'s read/write split (`find_available_manifests` / `next_manifest_path`) and its three locations (sidecar, adjacent `.preserve\`, global) | `Layer` / `LayerStack` (ordered layers, four policies: first wins, union, merge with precedence, key merge); `DataLayout` (portable beside the exe or a home folder as the base, the other base second, a Documents folder third; first-run setup; the one-time settings copy); `ItemStore` (a per-item file kept as a sidecar, in an adjacent dot-folder, or in the store; find in all, write to one, move with the item) |
| `Dazzle.Layers\Links\` | `Dazzle.Links` | `directory_manager.py` in Windows-No-Internet-Secured-BUGFIX (`create_junction_pair`) | `Junctions.EnsurePair`: one junction each way, idempotent, recorded for removal, created with `mklink /J`, never throwing |

Rules of the folder:

- Nothing here references `BooruDatasetTagManager`, WinForms, or `Program.Settings`; a consumer passes its configuration in. A console check that references this library alone, and nothing else, is the proof; it is kept beside the repository rather than in it (`tools\datastore-check` next to the clone, 45 checks as of 2.14.0).
- Mechanics (walk, create, copy, link) and policy (which layer is base, how entries combine) are separate types, the L1 / L3 line of the Python stack drawn inside one folder.
- Design: `2026-10-02__17-58-41__dev-workflow-process__satt-layering-principle-and-rules-at-four-levels.md` and the beta plan's storage addenda.
