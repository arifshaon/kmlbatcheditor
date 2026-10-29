# KML Scoped Editor

A Windows desktop application for safely batch-editing placemark styles and interactively editing line geometry in Google Earth **KML** and **KMZ** files.

KML Scoped Editor lets you select placemarks by folder, icon image, or icon variant, then change one or more style properties without deliberately changing unrelated placemark data or style settings.

The application is built with **C#**, **.NET 8**, and **WPF**.

---

## What the application does

KML Scoped Editor is designed for controlled changes across large groups of Google Earth placemarks.

You can change:

- icon size;
- icon colour;
- text or label size;
- text or label colour.

Each property is enabled separately. If a property is not selected, the application leaves that property unchanged.

For example, you can change the text colour of every placemark in a folder while preserving each placemark's existing icon image, icon size, icon colour, text size, coordinates, name, description, timestamps, and other KML content.

---

## Main features

- Opens both `.kml` and `.kmz` files.
- Displays the document, folders, subfolders, placemarks, overlays, and network links in a hierarchical tree.
- Selects placemarks by:
  - folder;
  - icon image;
  - icon variant.
- Optionally includes placemarks in nested subfolders.
- Calculates and displays the number of matching placemarks before editing.
- Changes icon size, icon colour, text size, and text colour independently or together.
- Previews the proposed changes before applying them.
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

The Line Editor is intended for dependable manual line cleanup and splitting rather than automatic interpretation.

Typical workflow:

1. Open the KML/KMZ and choose **Line Editor**. The editor always opens with nothing selected.
2. Use **Select (V)** to draw across lines. Only real crossings are selected. **Shift+click** adds one line; **Alt+click/drag** removes lines from the selection.
3. Choose **Split Selected Lines...** to keep the current selection as the split target and switch to **Split (S)**. If nothing is selected and you use Split directly, any document line crossed by the split line can be affected.
4. Draw a split line by clicking connected points or dragging freehand. Live markers and the map status show how many lines and intersection points would be split.
5. Right-click for split-line actions at the pointer, or use **double-click** / **Enter** to finish and apply. **Backspace** removes the last split point; **Esc** cancels the draft.
6. To use a Google Earth path, choose **Import Split Line...**. The imported line is editable before splitting: drag its vertex handles, or right-click for add/delete vertex options. Review the markers, then choose **Split with Imported Line**.
7. To use a helper LineString already present in the document, select exactly that line and choose **Use Selected as Split Line**. The helper is removed before the split so it cannot split itself.
8. After splitting, return to **Select** and delete unwanted pieces. Choose **Apply & Close** to keep those geometry changes in the currently loaded working document. Reopen Line Editor to continue. Use **Save As** only when you want to write the edited file to disk.
9. In the main editor, line colour and line width can be changed using the same scoped style-edit workflow as icon/text properties.

Navigation: **right-drag pans from any tool**; **Hand (H)** pans with left-drag; **Space+drag** and middle-drag also pan; the mouse wheel and **+/-** zoom; **F** fits the view; **Zoom Selection** fits the highlighted lines. **Ctrl+Z** undoes the last split/deletion.

Imported split lines are temporary overlays and are not saved into the output KML/KMZ.

For large contour files, the Line Editor uses a shared spatial grid for hit-testing/splitting, retained rendering layers, and display-only Douglas–Peucker simplification. These optimizations do not change the stored KML coordinates.
