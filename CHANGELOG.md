# Changelog

## Unreleased

### Added

- Experimental Line Editor for KML/KMZ LineString geometry.
- Draw-across selection: a freehand stroke selects every contour line or cut fragment it crosses.
- Batch deletion of selected lines/fragments, with Clear Selection and Invert Selection.
- Draw-across Cut tool that splits open and closed LineStrings at actual stroke intersections.
- Undo for cut and delete operations.
- Mouse-wheel zoom, right/middle-button pan, and Fit to View.
- Line edits are made on a working copy and are committed to the loaded document only with Apply & Close.
- Document tree, placemark, style, icon, and placemark-name indexes are rebuilt after structural line edits.

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
