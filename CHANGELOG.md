# Changelog

## 1.3.0 — validation update (2026-11-04)

### Fixed

- **Simplify tolerance preset selection:** WPF's editable ComboBox can raise `SelectionChanged` before updating its `Text`. The handler now defers preview recalculation until the selected tolerance is available. This fixes the prior one-selection-behind behavior (for example, choosing 5 m could previously calculate a 1 m preview).
- **Safe Simplify Apply:** Apply refuses to commit a preview if the current tolerance, smoothing option, selected-only scope, target-line selection, or geometry revision differs from those used for the preview. Instead, it requests recalculation.
- The smoothing-only warning also updates after the selected tolerance has been resolved.

### Verified with supplied KMZ files (2026-11-04)

The original `winchester contour(1).kmz` and three edited KMZ outputs were compared:

| File | LineStrings | Line vertices |
| --- | ---: | ---: |
| Original | 152 | 132,621 |
| `winchester contour_edited_1.kmz` (1 m) | 176 | 12,921 |
| `winchester contour_edited_5.kmz` (5 m) | 176 | 5,371 |
| `winchester contour_edited_10.kmz` (10 m) | 176 | 3,583 |

- Increasing the chosen tolerance consistently reduces the number of retained vertices, confirming the expected distinction between the three outputs.
- The earlier geometry comparison of retained, matched contours reported maximum deviations from the original of approximately **0.9997 m**, **4.9979 m**, and **9.9948 m**, respectively, consistent with the requested tolerances.
- **Comparison caveat:** the original has 152 placemarks/152 LineStrings; each edited output has 132 placemarks/176 LineStrings. These outputs also contain line splitting/removal, so overall vertex reduction is **not** a controlled all-lines-only Simplify benchmark; deviation measurements apply to matched retained geometry, not deleted portions.
- Decision: keep the fix in `main`; no rollback indicated by these supplied outputs.

### Other 1.3.0 changes

- Step 2 defaults **Only split the selected lines** to checked, with an all-eligible-lines fallback when nothing is selected. The choice is remembered for the current document.
- Step 3 **Select side** replaces the old Clip workflow, with optional crossing repairs and chronological Undo. Open-divider display extensions stop at the map extent; classification retains the full prepared divider.
- **Simplify / smooth** continues to default to **All lines**, matching version 1.2.
- Windows x64 installer and portable-package builds use the 1.3.0 version and preserve the previous installer upgrade identity.
- Current source is offered under **PolyForm Noncommercial License 1.0.0**; earlier MIT grants for already-distributed copies remain unaffected.

### Verification and release state

- The Simplify fix passed the Windows CI build (0 errors, 0 warnings), **16/16 existing regression checks**, and installer packaging checks before merge ([PR #5](https://github.com/arifshaon/kmlbatcheditor/pull/5)).
- The three KMZ files provide real-output confirmation beyond the existing synthetic regression tests. They are user-provided test inputs, **not committed repository assets**.
- Version 1.3.0 is in `main`; this entry does not represent a separately published GitHub Release or tag.

## Earlier development notes (previously marked Unreleased)

### Workflow redesign

- Rebuilt the main window around four explicit steps: **Which placemarks → What to change → Preview → Apply**.
- Removed non-functional tree checkboxes; a tree row click is the tree-item selection.
- Replaced **Calculate Selection** with a live matched-placemark count.
- Added tree-item, placemark-name, icon-image, and icon-variant mode buttons.
- Grouped style properties under **Lines**, **Icons**, and **Labels**, and added real **line opacity** editing.
- Style preview now refreshes automatically and includes a line colour/width/opacity sample.
- Moved Style Diagnostics into a collapsed **Inspect selected item** section.
- Rebuilt Line Editor around **Remove unwanted lines → Split → Remove pieces**.
- Added one **Apply split** action for imported and drawn split paths; Enter uses the same action.
- Split selection scope was made explicit during development; version 1.3.0 now defaults **Only split the selected lines** to checked, with an all-lines fallback when nothing is selected.
- Added on-map zoom controls, a legend, clearer **Cancel — discard** / **Done — keep changes** actions, and automatic advance to cleanup after a successful split.


### Line Editor

- Removed the redundant geometry parse on Line Editor open and the full geometry `Reload()` after applying split lines.
- Split rendering into retained base, selection/hover, and split/edit visual layers; hover/drawing no longer repaint the base geometry, and pan/zoom update layer transforms directly between zoom buckets.
- Replaced distance-thinning display simplification with iterative Douglas–Peucker, preserving closed display rings and leaving source KML coordinates untouched.
- Live split preview now checks effective split positions without constructing output pieces.

- Added universal **right-drag pan** from Select, Split, or Hand modes; a simple right-click still opens the context menu.
- Imported split lines are now editable before applying: drag vertex handles, or right-click to add/delete vertices.

- Renamed the geometry operation from **Cut** to **Split** throughout the Line Editor; the shortcut is now **S**.
- The Line Editor now always opens with nothing selected.
- Added **Shift+click** to add one line to the current selection; Alt+click/drag removes from it.
- Added imported/drawn split-line workflows and **Use selected line** support.
- Live split preview uses the same spatial grid as selection, hover and the final split, and updates incrementally while drawing.
- Added a segment-grid spatial index and incremental index updates after geometry edits.
- Added three rendering layers: cached base geometry, selection/hover overlays, and split overlays; display geometry is simplified by zoom level and drawing resources are frozen/cached.
- Unchanged line features and display geometries are retained after edits; the redundant second geometry parse was removed.
- Space is reserved for pan and Line Editor buttons cannot be triggered by Space.
- A single crossing of a closed ring now creates an opening measured in coordinate degrees, safely above the closed-ring tolerance.
- Imported split lines remain temporary overlays and are not written to output KML/KMZ.
- Split/deletion changes stay in the loaded working document after **Apply & Close** until the user chooses **Save As**.
- Added main-editor **line colour** and **line width** editing through the existing scoped Style/StyleMap cloning workflow.


## 1.1.0

### Added

- Replacement placemark icon selection from PNG, JPG, JPEG, GIF, or BMP files.
- Automatic bundling of replacement icons inside KMZ output under `files/icons/`.
- Content-hashed icon filenames to avoid name collisions inside KMZ packages.
- Built-in current-versus-proposed placemark appearance preview.
- Previous and Next controls for reviewing representative matched placemarks.
- Temporary one-placemark KMZ preview that can be opened in Google Earth.

### Improved

- Installer metadata and output filenames now identify version 1.1.0.
- Version 1.1.0 upgrades version 1.0.0 in place by retaining the same installer `AppId`.
- Application description now reflects visual preview and custom-icon support.
- WPF type references are explicitly qualified where Windows Forms and WPF types overlap.

### Fixed

- Resolved ambiguous `Brush` and `UserControl` compiler references.
- Added the missing icon-image settings to `KmlBatchEditSettings`.
- Applied the preview and icon-replacement controls to the main application UI.
- Fixed the startup XAML binding exception for `IconFileNameDisplay`.

## 1.0.0

- Initial packaged release of KML Scoped Editor.
- KML and KMZ loading and saving.
- Folder, icon-image, and icon-variant selection.
- Scoped icon-size, icon-colour, text-size, and text-colour editing.
- Shared style and `StyleMap` cloning to protect unselected placemarks.
- Portable ZIP and per-user Windows installer packaging.
