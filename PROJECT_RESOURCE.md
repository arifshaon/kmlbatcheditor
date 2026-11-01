# Simple KML Editor — Project Resource

_Last updated: 1 November 2026_

This file is the canonical project handover/resource for the **Simple KML Editor** project. It records the important product decisions, implemented behavior, packaging choices, and known constraints so future changes do not accidentally undo deliberate decisions.

## 1. Product identity

- Visible product name: **Simple KML Editor**.
- Internal project/repository names remain **KmlScopedEditor** / `kmlbatcheditor`.
- Current application version: **1.2.0**.
- Developer/publisher: **Arif Shaon**.
- Repository: **https://github.com/arifshaon/kmlbatcheditor**.
- Main purpose: edit KML/KMZ placemark styles and line geometry, including splitting, deleting, simplifying, smoothing, previewing, and scoped batch editing.
- Do **not** rename the internal namespaces/project unless there is a specific reason. The visible branding can stay separate from internal identifiers.

## 2. Branding and application shell

### Application icon
- The current icon is a pure WPF vector `DrawingImage` resource named `AppIcon` in `App.xaml`.
- It shows a folded map, route nodes, and a red cut/split mark.
- The vector icon is used by the WPF windows.
- A binary compiler-level `.ico` application resource was removed after Visual Studio reported:
  - `CS7065 Error building Win32 resources -- Unable to read beyond the end of the stream`.
- **Important:** do not reintroduce `<ApplicationIcon>` or a binary `.ico` reference casually. The current vector-only approach was chosen to keep builds reliable.
- Trade-off: the WPF windows can show the custom icon, but the raw EXE may still use a default Explorer icon until a safe binary ICO is deliberately restored.

### Splash screen
- Real startup splash window exists: `Views/SplashWindow.xaml`.
- It shows:
  - the large vector icon;
  - **Simple KML Editor**;
  - tagline **Edit · Split · Simplify · Style**;
  - version number;
  - an indeterminate progress bar.
- Startup is controlled in `App.xaml.cs`; `StartupUri` is not used.
- Splash is displayed for a minimum of roughly **1.1 seconds** so users actually see it.
- Because the project references both WPF and Windows Forms, `App` explicitly derives from:
  - `System.Windows.Application`.
- When adding UI code, watch for WPF/WinForms type ambiguity (`Application`, `MessageBox`, `KeyEventArgs`, etc.) and qualify WPF types when necessary.

### Main menu
The main window has a standard menu:
- **File**
  - Open...
  - Save As...
  - Edit Lines...
  - Exit
- **Help**
  - About Simple KML Editor

The menu reuses the same existing commands/handlers as the toolbar. It should not introduce separate application logic.

### About dialog
- `Views/AboutWindow.xaml` is the current About window.
- It includes:
  - large application icon;
  - product name;
  - current version;
  - brief description;
  - **Developed by Arif Shaon**;
  - clickable project repository link;
  - copyright.
- Current layout deliberately uses:
  - scrollable content area;
  - a separate fixed **64 px footer**;
  - Close button in that footer.
- This was done because earlier layouts clipped the developer information or made the Close button barely visible.
- Keep the footer structurally separate from scrollable content.

### Tooltips
- Main-window buttons/toggles, Line Editor buttons/toggles, preview controls, placemark-name controls, and About controls now have simple hover tooltips.
- Tooltips should explain the action in plain language, not restate the button label.

## 3. Main-window selection behavior

### Tree ticking
- Tree checkboxes are user-facing two-state controls:
  - `IsThreeState="False"`.
- Programmatic `null` can still be used internally to display a partial/indeterminate parent state.
- User expectation: normal one-click tick / one-click untick behavior.

### Ticked-items mode
- `PlacemarkSelectionMode.Ticked` exists.
- Main selection modes include:
  - Tree item
  - Ticked
  - Placemark name
  - Icon image
  - Icon variant
- `Tick all` and `Untick all` commands are available.

### Tick preservation through Line Editor
Opening and returning from Line Editor should not deliberately reset the user’s ticked tree state.

Current strategy:
- create a cloned Line Editor document snapshot;
- temporarily mark checked leaf nodes in the clone with:
  - namespace `urn:kml-scoped-editor:temporary`;
  - marker `tree-ticked`;
- restore checked nodes after Line Editor output is applied;
- remove temporary markers before continuing.

Caveat:
- splitting a marked line can clone marker state into resulting pieces. Preserve this behavior unless a better semantic mapping is deliberately designed.

## 4. Line Editor workflow — key decisions

The Line Editor is a staged workflow:

1. **Remove unwanted lines**
2. **Split**
3. **Remove pieces**

### Pending split lines are additive
This is a deliberate design decision.

- Imported split lines, drawn split lines, and **Use selected line** guides can coexist.
- Finishing a drawing does **not** apply it.
- Enter, double-click, right-click finish, or equivalent drawing completion only adds/finishes the pending guide.
- **Only the explicit Apply split action changes geometry.**
- Multiple pending guides are applied together.

Do not revert this to “latest guide replaces previous guide”.

### Pending split editing/removal
- Pending guides can be selected/edited with handles.
- Right-click actions are available.
- Delete/Backspace in Step 2 affects the current draft/pending guide, not the underlying KML geometry.
- Removing a pending guide is not the same as deleting a KML line.

### Previous split appearance
- Applied split history can remain visible.
- **Show previous splits** is enabled by default.
- Previous split lines use a muted purple/grey appearance distinct from pending guides and split points.

### Apply split and Done
- **Apply split** consumes the current pending guide set and changes geometry.
- **Done — keep changes** returns geometry changes to the main working document.
- **Save As** in the main window is still required to write the file to disk.
- **Cancel — discard** closes the editor without keeping the geometry edits.

### Done with unapplied pending guides
Important state-loss fix:
- if the user chooses to close while pending guides exist, do not silently mutate/remove them first and then leave the editor open;
- confirmed close can return without applying pending guides;
- if no geometry changed, the editor closes without pretending there were saved geometry changes.

## 5. Split-helper/source-line behavior

When **Use selected line** creates a temporary split guide:
- helper source identity is marked using:
  - namespace `urn:kml-scoped-editor:temporary`;
  - marker `split-guide-source-id`.
- helper-derived pending state carries `SourceLineId`.

Important correctness rule:
- all helper fragments belonging to pending helper-derived guides must be identified **before** splitting starts;
- all matching helper elements are removed before split processing;
- this prevents a helper from being split first and leaving cloned helper fragments behind.

Output must not retain temporary editor markers.

## 6. Clip to boundary

The Line Editor Step 2 now supports clipping editable line geometry using either an **open** or **closed** pending boundary.

### User behavior
- **Closed boundary**
  - **Keep inside** previews/deletes eligible LineStrings outside the boundary.
  - **Keep outside** previews/deletes eligible LineStrings inside the boundary.
- **Open boundary**
  - **Choose side** asks the user to click clearly on the side to keep.
  - The software extends both boundary ends beyond the geometry extent before splitting/classification.
- Crossed lines are split first; eligible line pieces and untouched eligible whole lines are classified. Clip removes **LineStrings only**, never Points, Polygons, or overlays.
- Clip always previews before destructive changes:
  - deletion area shaded;
  - open-line extensions shown dashed;
  - kept/deleted counts shown;
  - **Apply clip** performs the operation;
  - **Cancel preview** changes nothing.
- After Apply, the clip boundary becomes applied split history and the editor advances to Step 3 for optional cleanup.

### Closed boundaries
- `KmlCutGuide` has `IsClosed`.
- Closure is propagated through pending-guide undo and Line Editor session history.
- A line is treated as closed when it has a valid ring and its first/last coordinates are within about 0.5 m; closed geometry is normalized so the last coordinate exactly equals the first.
- **Close loop** works while drawing and for a single pending open guide. A refused non-dividing open clip guide offers to join its endpoints; the pending-guide change is undoable, with validation for degenerate loops.
- Closed-loop editing preserves closure and does not expose the duplicated final coordinate as a separate logical handle.
- A valid closed loop cannot be reduced below three distinct vertices.
- **Use selected line** recognizes a selected closed contour as a closed boundary.
- Imported Google Earth `Polygon/outerBoundaryIs/LinearRing` is supported as a closed boundary.
- Polygon inner rings/holes are intentionally not imported as independent clip loops in this version. Treating holes as union loops would invert their semantics.
- Several closed boundaries are allowed:
  - Keep inside = inside **any** loop;
  - Keep outside = outside **all** loops.

### Open boundaries
- Clip uses **one open boundary at a time**.
- An open boundary cannot be combined with another open or closed pending boundary for Clip.
- Endpoint direction is sampled from roughly 10 screen pixels back along the path rather than relying on a potentially jittery final hand-drawn segment.
- The prepared divider is extended to a bounds rectangle outside the editable geometry.
- A self-intersecting or otherwise invalid open divider is refused. The user can opt to close a non-dividing open guide, then use Keep inside/Keep outside.
- The preview stores the exact resulting document, so zooming between Preview and Apply cannot recalculate a different clip.

### Classification
- Geometry uses a **1 metre boundary-retention band** and a separate ~1 mm point-on-segment predicate tolerance.
- Degenerate edges are treated as points rather than marking every point as on-boundary.
- Per-piece classification uses an indexed boundary-distance search: the first candidate clearly farther than 5 m decides the side; pieces near the boundary retain a farthest-candidate check. Pieces entirely within 1 m of the boundary are kept.
- Closed boundaries use indexed even-odd point-in-polygon logic; self-crossing loops show an explicit warning.
- Rectangle perimeter corner positions are unwrapped and sorted to construct untwisted open-side regions.
- Open boundaries divide an enclosing rectangle into two regions; the user click chooses which region is retained.

### Selection scope
The Step 2 checkbox is now:
- **Limit to selected lines when selected**
- checked by default.

Semantics:
- **Clip:** checked + zero selected lines = refuse safely; checked + selected lines = only the selected eligible lines; unchecked = all eligible lines.
- **Split:** retains its existing no-selection fallback to all lines.
- Clip's preview always states the effective scope, excluding protected boundary source lines.
- The checked/unchecked preference is retained when reopening the editor on the same document.

### Preview and transaction safety
- Clip preview is calculated on a cloned document using the same real split/classification logic rather than estimating only intersections.
- Every eligible clip target is marked explicitly, including when the scope is all lines. Lines used as source boundaries are protected and excluded from splitting/classification, not deleted.
- Apply Split still removes helper sources; Clip deliberately does not.
- A completed preview contains the **exact result document** and its scope/geometry/guide revision; stale previews cannot be applied.
- Apply captures one `UndoActionKind.Clip` snapshot and commits the prepared result without repeating split/classification. A failed transaction restores the pre-clip snapshot.
- Target markers are removed from the prepared document before Apply.
- A single Ctrl+Z reverses the whole clip, returns to Step 2, and reports **Clip undone.**
- Success message follows:
  - `Clipped to boundary: X line(s) split, Y piece(s) kept, Z deleted.`

Regression checks and Windows GUI acceptance steps are in [CLIP_REGRESSION_TESTS.md](CLIP_REGRESSION_TESTS.md).

## 7. Ring opening / hairpin handling

A previous narrow-ring opening could still be detected as closed because the endpoint gap was smaller than geometry tolerance.

Current rule:
- opening a ring must guarantee an endpoint gap of at least **0.01 metres (1 cm)**.
- the editor may trim farther along the line until that minimum separation is reached.
- if an open result cannot be guaranteed safely, fail rather than producing a falsely “open” ring.

## 8. Simplify / Smooth feature

This is one of the main v1.2.0 additions.

### UI
Available in Step 1 and Step 3 using:
- **Simplify / smooth...**

Panel includes:
- Simplify:
  - Off
  - 0.5 m
  - 1 m (default)
  - 2 m
  - 5 m
  - 10 m
  - custom value
- **Smooth curves**
- Apply to:
  - All lines
  - Only selected lines
- preview summary
- topology warning text
- Cancel preview
- Apply

### Simplification algorithm
- Douglas–Peucker simplification in a metric projected plane.
- Longitude and latitude use separate metre factors derived from mean latitude.
- Open-line endpoints remain exact.
- Closed rings remain closed.
- Altitude is interpolated where needed.
- Changed interior coordinates are written with approximately **8 decimal places**.
- Unchanged open endpoints use exact round-trip formatting.

### Smoothing
- Chaikin smoothing is applied **twice**.
- Open-line endpoints remain unchanged.
- Closed rings remain closed.
- Smooth-only mode is allowed but warns the user because smoothing can increase point count and changes geometry.

### Preview/application model
- Preview runs asynchronously with debounce/cancellation/versioning.
- Preview must not modify geometry.
- Apply uses the exact preview coordinates rather than recalculating a different result.
- No stale preview should remain visible while a recalculation is pending.
- Base geometry is faded while the proposed geometry is drawn separately.

### Topology checking
Simplification/smoothing checks for newly introduced topology problems:
- proper crossings;
- touches;
- collinear overlaps;
- non-adjacent self-intersections.

The checker compares before/after topology and warns about **new** problems introduced by the proposed operation.

Important:
- only true endpoint joins of two open complete lines are ignored;
- arbitrary coincident vertices are not ignored.

### Expected synthetic spike behavior
A regression case used during development:
- 1 m simplification → 0 new crossings
- 3 m → 0
- 5 m → 2

### Performance
- geometry/spatial indexing and cached baseline topology are used to avoid obvious repeated work;
- no-op fast path exists when Simplify is Off and Smooth is off.
- Exact performance on very large real files remains something to test rather than assume.

## 9. Undo behavior

Undo is chronological across different action types.

Current undo categories include:
- split;
- deletion;
- simplify;
- pending-guide edit.

Key rule:
- active unfinished draft point is undone first;
- otherwise, compare shared sequence numbers across document undo and pending-guide undo histories;
- undo the **newest action**, regardless of which internal stack owns it.

Simplify undo:
- restores the prior coordinate snapshot;
- keeps the current workflow step;
- restores associated cut points consistently;
- status reports **Simplify undone.**

Do not revert to separate stack behavior that can undo an older document action before a newer pending-guide edit.

## 10. Selection and lasso behavior

- Click toggles line/piece selection.
- Double-click is protected from accidental toggle behavior.
- Shift adds; Alt removes where applicable.
- Lasso supports explicit Add/Remove behavior.
- Step 1 deletion removes selected source lines.
- Step 3 deletion removes selected resulting pieces.
- Step 2 Delete/Backspace must never delete underlying KML geometry.

## 11. Geometry and document preservation

General intent:
- edit only the requested geometry/style scope;
- preserve unrelated KML data;
- preserve KMZ embedded resources where applicable;
- keep existing `LineString` XElement identity when coordinate replacement is possible;
- rebuild spatial indexes/bounds after batch coordinate replacement;
- preserve selection using element identity where feasible.

## 12. Main-window style editing

The application supports scoped placemark style editing for:
- Lines
  - colour
  - width
  - opacity
- Icons
  - image
  - size
  - colour
  - opacity
- Labels/text
  - size
  - colour
  - opacity

Important behavior:
- each style property is independently enabled;
- properties not selected for change should remain untouched;
- shared/inline styles and `StyleMap` references are handled carefully to avoid unintended changes outside the selected scope;
- custom icon files can be bundled into KMZ output;
- built-in current/proposed preview and Google Earth preview are available.

## 13. Save semantics

- Main-window edits modify the working in-memory document.
- Line Editor **Done** returns geometry changes to that working document.
- The original source file is not overwritten automatically.
- **Save As** is the explicit write-to-disk action.
- Unsaved changes indicator/warning remains important.

## 14. Packaging and installer

### Current version
- **1.2.0**

Project file:
- `Version 1.2.0`
- `FileVersion 1.2.0.0`
- `AssemblyVersion 1.2.0.0`

### Local build outputs
`Build-Release.ps1` produces:

- Published EXE:
  - `artifacts\publish\win-x64\KmlScopedEditor.exe`
- Portable ZIP:
  - `artifacts\portable\Simple-KML-Editor-Portable-1.2.0-win-x64.zip`
- Installer:
  - `artifacts\installer\Simple-KML-Editor-Setup-1.2.0.exe`

### Build commands
- `Build-Portable.cmd`
  - builds the self-contained portable package;
  - opens `artifacts\portable` in File Explorer after success.
- `Build-Installer.cmd`
  - builds portable output plus the Inno Setup installer;
  - opens `artifacts\installer` in File Explorer after success.

### Installer prerequisite
Local installer creation requires **Inno Setup 6**.

If missing:
```powershell
winget install --id JRSoftware.InnoSetup -e
```

The application itself can still publish successfully without Inno Setup; only Setup.exe creation fails.

### Installer identity
- Display name: **Simple KML Editor**
- Publisher: **Arif Shaon**
- Fresh default location:
  - `%LOCALAPPDATA%\Programs\Simple KML Editor`
- Per-user install; no admin privilege required.
- Start-menu shortcut plus optional desktop shortcut.
- The permanent installer `AppId` is:
  - `{7F5C89B1-E9E9-438F-AF80-5057DD72C1F0}`
- **Do not change this AppId.**
- It is deliberately retained so v1.2.0 upgrades earlier KML Scoped Editor installations instead of creating a second installed product.

### Signing
- Installer/application are currently unsigned.
- Testers may see Windows SmartScreen / **Unknown publisher** warnings.
- Public distribution should eventually use a trusted code-signing certificate.

## 15. GitHub Actions Windows build

Workflow:
- `.github/workflows/windows-release.yml`

It:
- runs on `windows-latest`;
- installs .NET 8;
- installs Inno Setup;
- executes `Build-Release.ps1 -Installer`;
- uploads:
  - `Simple-KML-Editor-Setup-1.2.0`
  - `Simple-KML-Editor-Portable-1.2.0-win-x64`

A successful Windows Actions run confirmed that the v1.2.0 source and installer process compile successfully on Windows.

Note:
- GitHub Actions artifacts are downloaded as ZIP archives, even when the artifact contains a single installer EXE.

## 16. Repository artifacts

The repository has historically contained generated artifacts under `artifacts/`.

Be careful distinguishing:
- **committed historical artifacts** already present in the repo; and
- **new locally/generated build outputs**, which may exist only on the build machine or as GitHub Actions artifacts.

For tester distribution, prefer the freshly built v1.2.0 installer, not an old committed v1.1.0 binary.

## 17. Tester-facing guidance

Current tester focus should include:
- splash screen appears correctly;
- File/Help menu works;
- About dialog shows:
  - developer name;
  - GitHub link;
  - fully visible Close button;
- tooltips are understandable;
- tree tick/untick behavior;
- Line Editor staged workflow;
- additive pending split guides;
- Apply split only changes geometry;
- Undo order is chronological;
- Simplify/Smooth preview/apply/warnings;
- Save As writes the expected KML/KMZ;
- installer upgrades an earlier installation cleanly.

A short Word tester guide has also been produced for v1.2.0, focused on the Simplify/Smooth workflow.

## 18. Known constraints / things not to assume

- Do not treat Visual Studio `XDG0008` designer errors as the root cause when a real C# compile/resource error is present; they often cascade after compilation fails.
- Because `UseWindowsForms=true` and `UseWPF=true`, ambiguous framework type names are possible.
- Do not claim a build is successful unless it has actually compiled locally or in CI. Source/static checks alone are not a build.
- Simplification topology checks are intentionally conservative.
- Very large-file performance should be measured on representative files rather than assumed from synthetic tests.
- Closed-ring simplification may not be rotationally invariant because the stored ring sequence has a chosen starting point.
- Topology difference matching is count/group based rather than a full geometric one-to-one persistence model.
- Windows on ARM would require a separate `win-arm64` package; current release targets `win-x64`.

## 19. Core source files

- `KmlScopedEditor/MainWindow.xaml`
- `KmlScopedEditor/MainWindow.xaml.cs`
- `KmlScopedEditor/ViewModels/MainViewModel.cs`
- `KmlScopedEditor/Models/KmlTreeNode.cs`
- `KmlScopedEditor/Models/PlacemarkSelectionMode.cs`
- `KmlScopedEditor/Services/KmlLineGeometryService.cs`
- `KmlScopedEditor/Services/KmlSelectionService.cs`
- `KmlScopedEditor/Services/LineSimplificationService.cs`
- `KmlScopedEditor/Services/KmlCutGuideService.cs`
- `KmlScopedEditor/Views/LineEditorWindow.xaml`
- `KmlScopedEditor/Views/LineEditorWindow.xaml.cs`
- `KmlScopedEditor/Views/LineEditorWindow.Simplify.cs`
- `KmlScopedEditor/Views/LineEditorSurface.cs`
- `KmlScopedEditor/Views/SplashWindow.xaml`
- `KmlScopedEditor/Views/AboutWindow.xaml`
- `Installer/KmlScopedEditor.iss`
- `Build-Release.ps1`
- `Build-Installer.cmd`
- `Build-Portable.cmd`

## 20. Product-direction principles

When making future changes:
- prefer simple, explicit user actions over hidden automatic behavior;
- preview destructive/geometry-changing operations before Apply;
- preserve unrelated KML/KMZ data;
- keep terminology understandable to non-GIS users;
- do not make finishing a drawing equivalent to applying a destructive operation;
- keep installer/update behavior predictable;
- keep visible branding professional but lightweight;
- keep advanced implementation details out of the normal user workflow unless needed for safety or clarity.
