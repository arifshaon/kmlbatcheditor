# KML Scoped Editor

A Windows desktop application for safely batch-editing placemark styles and interactively editing line geometry in Google Earth **KML** and **KMZ** files.

KML Scoped Editor lets you select placemarks by tree item, placemark name, icon image, or icon variant, then change one or more style properties without deliberately changing unrelated placemark data or style settings.

The application is built with **C#**, **.NET 8**, and **WPF**.

---

## What the application does

KML Scoped Editor is designed for controlled changes across large groups of Google Earth placemarks.

You can change:

- line colour, width, and opacity;
- icon image, size, colour, and opacity;
- text or label size, colour, and opacity.

Each property is enabled separately. If a property is not selected, the application leaves that property unchanged.

For example, you can change the text colour of every placemark in a folder while preserving each placemark's existing icon image, icon size, icon colour, text size, coordinates, name, description, timestamps, and other KML content.

---

## Main features

- Opens both `.kml` and `.kmz` files.
- Displays the document, folders, subfolders, placemarks, overlays, and network links in a hierarchical tree.
- Selects placemarks by:
  - tree item;
  - placemark name;
  - icon image;
  - icon variant.
- Optionally includes placemarks in nested subfolders.
- Updates the matching placemark count automatically as the scope changes.
- Groups available style changes into Lines, Icons, and Labels.
- Changes line, icon, and label appearance independently or together.
- Updates the built-in current/proposed preview automatically before Apply.
- Shows style diagnostics for an individual placemark.
- Handles inline styles, shared styles, and KML `StyleMap` references.
- Clones shared styles where necessary to avoid changing placemarks outside the selected scope.
- Preserves embedded files and images when saving a KMZ package.
- Shows progress and a central loading overlay during longer operations.
- Supports cancellation where an operation can be stopped safely.
- Displays clear success, warning, and error notifications.
- Saves the edited document as a new KML or KMZ file.
- Includes an experimental Line Editor for selecting, splitting, and deleting KML LineString geometry.
- Supports zoom, pan, fit-to-view, invert selection, and undo while line editing.

---

## Experimental Line Editor

The Line Editor follows the manual cleanup workflow directly:

1. **Remove unwanted lines** — drag across source lines to select them, then delete them. Shift+click adds one line; Alt+click/drag removes from the selection.
2. **Split** — import a KML/KMZ split path, draw a new split path, or use one selected document line as the split path. Imported paths are editable: drag their vertex handles, or right-click to add/delete vertices.
3. **Remove pieces** — after the split is applied, select the unwanted resulting pieces and delete them.

Nothing needs to be selected for a normal split. By default, any line crossed by the split path can be split. To deliberately restrict the operation, select the target lines first and tick **Only split the selected lines**.

There is one **Apply split** action for both imported and drawn split paths; **Enter** does the same thing. Double-click finishes a drawn path but does not apply it automatically. The preview summary reports how many lines and intersection points would be split.

After a successful split, the editor advances to **Remove pieces** and hides the temporary split overlay. **Done — keep changes** returns the geometry edits to the loaded working document; **Cancel — discard** closes without keeping them. The main-window **Save As** command is still required to write a KML/KMZ file to disk.

Navigation: **right-drag** or **Space+drag** pans from any step; middle-drag also pans; the mouse wheel and on-map controls zoom; **F** fits the view; **To selection** fits highlighted lines. **Ctrl+Z** undoes the last split/deletion.

Imported split lines are temporary overlays and are not saved into the output KML/KMZ.

For large contour files, the Line Editor uses a shared spatial grid for hit-testing/splitting, retained rendering layers, and display-only Douglas–Peucker simplification. These optimizations do not change the stored KML coordinates.
