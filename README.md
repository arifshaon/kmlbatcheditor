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

The Line Editor is intended for manual contour cleanup and cutting rather than automatic interpretation.

Typical workflow:

1. Open the contour KML/KMZ and choose **Line Editor**.
2. Use **Select** and draw across unwanted contours. Only real crossings are selected. Hold **Alt** while drawing to remove crossed lines from the selection.
3. Click **Delete Selected** to remove the highlighted lines. **Undo** restores the previous cut/deletion.
4. To use a cutline drawn in Google Earth, choose **Import Cut Lines...**, select a KML/KMZ, review the purple guide, then choose **Apply Cut**.
5. Alternatively, single-click one helper path already present in the document and choose **Use Selected as Cutline**.
6. After cutting, use **Select** again to mark unwanted line fragments and delete them.
7. Choose **Apply & Close**, then use **Save As** in the main window.

Imported guides are temporary overlays and are not saved into the output. Applied guides remain visible in a faded form so the cut location is easy to see. Orange markers show recent cut points. Mouse wheel zooms; right- or middle-drag pans; **Fit to View** includes imported guides.
