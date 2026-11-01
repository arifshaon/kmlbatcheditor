# Simple KML Editor

A Windows desktop application for safely batch-editing placemark styles and interactively editing line geometry in Google Earth **KML** and **KMZ** files.

Simple KML Editor lets you select placemarks by tree item, placemark name, icon image, or icon variant, then change one or more style properties without deliberately changing unrelated placemark data or style settings.

The application is built with **C#**, **.NET 8**, and **WPF**.

For the current implementation decisions, packaging state, Line Editor behavior, and important constraints, see **[PROJECT_RESOURCE.md](PROJECT_RESOURCE.md)**.

## License — noncommercial use only

**Copyright © 2026 Arif Shaon.** The current project source, original assets,
documentation, packaging scripts, and future distributions of this licensed
code are provided under the
**[PolyForm Noncommercial License 1.0.0](LICENSE)**.
You may use, study, change, and share the software for permitted noncommercial
purposes under that license. **Commercial use is not authorized** under this
license; obtain separate permission from the copyright holder for commercial use.

This is **source-available software, not an OSI-approved open-source license**.
The license also permits specified educational, charitable, public research,
government, and other qualifying organizations to use the software, as defined
in its terms. See the complete [LICENSE](LICENSE) and [NOTICE](NOTICE).

**Existing MIT rights are not revoked:** versions and copies previously
distributed under the MIT License remain subject to those earlier grants.
Third-party components (if any) retain their own licenses.

---

## What the application does

Simple KML Editor is designed for controlled changes across large groups of Google Earth placemarks.

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
- Includes a Line Editor for selecting, splitting, deleting, simplifying, and smoothing KML LineString geometry.
- Supports additive pending split lines, topology warnings, zoom, pan, fit-to-view, lasso selection, and chronological undo while line editing.

---

## Experimental Line Editor

The Line Editor follows the manual cleanup workflow directly:

1. **Remove unwanted lines** — drag across source lines to select them, then delete them. Shift+click adds one line; Alt+click/drag removes from the selection.
2. **Split** — import a KML/KMZ split path, draw a new split path, or use one selected document line as the split path. Imported paths are editable: drag their vertex handles, or right-click to add/delete vertices.
3. **Remove pieces** — after the split is applied, select the unwanted resulting pieces and delete them.

For version 1.3, **Only split the selected lines** is checked by default. If any lines are selected, Apply split affects only those selected lines. If nothing is selected, Apply split still splits **all eligible lines**; the checkbox stays checked so the preference is not lost. Uncheck it deliberately to split all lines even when some are selected. The checkbox choice is remembered within the current document's editor session.

Imported, drawn, and **Use selected line** split paths can coexist as pending split lines. **Enter**, double-click, or **Finish Drawing Here** only finishes the current drawing; they never apply a split. Only **Apply split** changes the geometry. The preview summary reports how many lines and intersection points would be split.

After a successful split, the editor advances to **Remove pieces** and hides the temporary split overlay. **Done — keep changes** returns the geometry edits to the loaded working document; **Cancel — discard** closes without keeping them. The main-window **Save As** command is still required to write a KML/KMZ file to disk.

Navigation: **right-drag** or **Space+drag** pans from any step; middle-drag also pans; the mouse wheel and on-map controls zoom; **F** fits the view; **To selection** fits highlighted lines. **Ctrl+Z** undoes the newest action across geometry edits and pending split-line edits.

Imported split lines are temporary overlays and are not saved into the output KML/KMZ.

For large contour files, the Line Editor uses spatial indexing and retained drawing layers for performance. The explicit **Simplify / smooth...** feature can reduce stored coordinates using a metre-based tolerance, optionally smooth curves, preview measured deviation, and warn about newly introduced crossings before Apply. As in version 1.2, **All lines** is the initial Simplify / smooth scope; **Only selected lines** can be chosen explicitly.


### Select side and split regression checks

Step 3 Select side and Step 2 Split regression checks are documented in
[CLIP_REGRESSION_TESTS.md](CLIP_REGRESSION_TESTS.md). On Windows with .NET 8 installed:

```powershell
dotnet run --project KmlScopedEditor.RegressionTests/KmlScopedEditor.RegressionTests.csproj -c Release
```

The CI workflow builds the WPF application and runs geometry checks on Windows. Version 1.3 changes the Step 2 selection-only **default**, while retaining Apply split and the all-lines fallback. Side selection and optional repair take place in Step 3. Simplify / smooth keeps its version 1.2 All-lines default.
