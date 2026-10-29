# Changelog

## Unreleased

### Line Editor

- Renamed the geometry operation from **Cut** to **Split** throughout the Line Editor; the shortcut is now **S**.
- The Line Editor now always opens with nothing selected.
- Added **Shift+click** to add one line to the current selection; Alt+click/drag removes from it.
- Added **Split Selected Lines...**, **Import Split Line...**, **Use Selected as Split Line**, and **Split with Imported Line**.
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
