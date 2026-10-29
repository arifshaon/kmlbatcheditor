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
- Includes an experimental Line Editor for drawing across contour/LineString geometry to select, cut, and delete lines or cut fragments.
- Supports zoom, pan, fit-to-view, invert selection, and undo while line editing.

---

## Experimental Line Editor

The Line Editor is intended for dependable manual contour cleanup and cutting rather than automatic interpretation.

Typical workflow:

1. In the main document tree, you can select a placemark/folder before opening **Line Editor**. Its lines are used as the initial selection, but **all lines in the document remain selectable and editable**. Choosing the whole document/root opens with no lines preselected.
2. Use **Select (V)** to draw across unwanted contours. Only real crossings are selected. **Alt+drag** removes crossed lines from the selection. **Delete** removes the highlighted lines.
3. If any lines are selected, a cut affects only those selected lines. If nothing is selected, the cut affects every line in the document. The map overlay always shows which rule is active.
4. Use **Cut (C)** to draw a cutline. Click to add connected points or press-and-drag for a freehand section. Live markers show the intersections and the overlay reports how many lines/points would be cut.
5. **Right-click opens cut-line options at the pointer** (add point, finish/apply, undo point, cancel). You can also finish and apply with **double-click** or **Enter**. **Backspace** removes the last point; **Esc** cancels the draft.
6. To use a Google Earth cutline, choose **Import Cut Lines...**. Imported guides are previewed first; choose **Apply Imported Guide** to commit them.
7. To use a helper LineString already in the document, select exactly that line and choose **Use Selected as Cutline**. The helper is removed before cutting so it cannot cut itself.
8. After cutting, return to **Select** and remove unwanted fragments. Then choose **Apply & Close** and use **Save As** in the main window.

Navigation: **Hand (H)** pans with left-drag; **Space+drag** and middle-drag also pan; the mouse wheel and **+/-** zoom; **F** fits the view; **Zoom Selection** fits the highlighted lines. **Ctrl+Z** undoes the last cut/deletion.

Imported/applied guides remain overlay state and are not saved into the output KML/KMZ.
