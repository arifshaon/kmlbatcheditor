# Changelog

## Unreleased

### Line Editor

- Simplified the permanent toolbar to **Select**, **Cut**, **Hand**, Undo, Fit, Zoom Selection, and zoom controls; Select/Cut actions now appear contextually.
- Added keyboard shortcuts: V Select, C Cut, H Hand, Ctrl+Z Undo, Delete, Backspace, Esc, F, and +/- zoom.
- Added explicit Hand mode, Space+drag pan, middle-drag pan, cursor feedback, and Zoom Selection.
- Hand-drawn cuts now support point-by-point and freehand input, with double-click/right-click/Enter to finish and apply immediately as one undoable step.
- Added live cut-impact preview markers and counts showing how many lines and intersections would be affected.
- Cut scope now follows the current line selection inside the outer main-tree scope: selected lines only when present, otherwise all editable lines in the tree scope.
- The map overlay permanently shows the active cut scope; out-of-scope lines remain visible in light grey and locked.
- Fixed **Use Selected as Cutline** so the helper line is removed before the cut and cannot split itself.
- Imported KML/KMZ cut guides remain preview-first and are committed explicitly with **Apply Imported Guide**.
- Selection strokes use real geometric crossings only; Alt+drag deselects.
- Added cached contour rendering, feature bounding-box rejection, hover highlighting, fixed projection for imported guides, and lightweight overlay-only live previews.
- A single crossing of a closed ring opens it with a visually negligible gap so a later cut can split it normally.
- Line edits remain on a working copy until **Apply & Close**.

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
