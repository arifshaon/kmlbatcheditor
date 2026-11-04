# Simple KML Editor — Project Resource

_Last updated: 4 November 2026_

This file is the canonical project handover/resource for the **Simple KML Editor** project. It records the important product decisions, implemented behavior, packaging choices, and known constraints so future changes do not accidentally undo deliberate decisions.

## 1. Product identity

- Visible product name: **Simple KML Editor**.
- Internal project/repository names remain **KmlScopedEditor** / `kmlbatcheditor`.
- Current source/application version in **main**: **1.3.0** (a separate formal GitHub Release/tag has not been published).
- Developer/publisher: **Arif Shaon**.
- Repository: **https://github.com/arifshaon/kmlbatcheditor**.
- Licensing of the current source: **PolyForm Noncommercial License 1.0.0**,
  a source-available license that does not grant commercial use. The legal
  text is in `LICENSE`; `NOTICE` records the copyright and important
  exceptions. Previously distributed MIT-licensed copies retain their
  historic grants. Third-party materials keep their respective licenses.
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

## 6. Select side (Step 3)

Version 1.3 keeps the established **Step 2 Apply split** workflow: import,
draw or use a selected line as a guide, optionally Close loop, then Apply
split. The later Clip panel has been removed; Select side belongs to Step 3.

**New version 1.3 selection-scope default:** **Only split the selected lines**
is checked by default. When one or more lines are selected, Apply split
targets those lines only. When no lines are selected, Apply split still
targets **all eligible lines**, without clearing the checked preference.
The checkbox stays enabled and the current scope is explained in the UI.
Users may uncheck it to split all lines even when some are selected.
The scope preference is preserved across editor sessions for the same
document, including when the editor closes without keeping geometry changes.

### Step 3: select a side, then delete if desired

- After applying a split, click **Select side** in Step 3.
- The system uses the most recent applied sequence, including guides restored
  from the current document's editor session.
- For closed guides, inside means inside any latest closed loop; outside means
  outside all. For open guides, a single simple divider is extended beyond
  the editable geometry and divided into two regions. Multiple open guides or
  mixed open/closed guides from the last sequence are not supported.
- The boundary is displayed as a dashed overlay. Open-line extensions are drawn
  only up to the map's extent; the selection calculation still extends them
  beyond it with a margin. Click in the map on the side you want. Clicks within
  1 m of the divider are refused.
- Plain click replaces the selection; Shift adds and Alt removes. Dragging does
  not trigger side selection. The mode remains enabled for further clicks.
- Pieces on the boundary are excluded. Pieces that actually cross the guide
  are identified by indexed **segment intersections** as well as sampled-side
  checks, so long segments crossing narrow loops are not silently selected.
- If crossings remain, the user can authorize an additional split of those
  crossing lines only. The repair creates a separate `RepairSplit` undo action,
  does not change the original guide's `AppliedSequence`, reclassifies once,
  and does not repeatedly prompt. No-op repairs have no undo entry.
- The actual deletion remains the existing **Delete selected pieces** action.
  Select side by itself does not change KML geometry.
- Toggle off, Escape, Pan, workflow changes, Apply split, Undo, Done, Cancel,
  or opening Simplify end Select side. Deletion refreshes the boundary cache
  while retaining the mode if it is still valid.
- The prepared side selection includes an indexed divider, prepared polygons,
  and a geometry revision; stale state is rebuilt after a geometry change.

### Simplify / smooth scope

Simplify/smooth continues to work in Step 1 and Step 3 with **the original
version 1.2 default of All lines**. Users may explicitly select
**Only selected lines**. As in version 1.2, opening Simplify/smooth with an
empty selection resets a previously selected-only scope to All lines.
The panel never applies changes without the user choosing Apply.

The full regression instructions are in
[CLIP_REGRESSION_TESTS.md](CLIP_REGRESSION_TESTS.md), retained as a filename
for compatibility with earlier references.

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
- **Version 1.3 fix (4 November 2026):** an editable WPF tolerance ComboBox raises `SelectionChanged` before updating `Text`. Defer tolerance-selection handling via the dispatcher before recalculating the preview or smooth-only warning.
- Apply must confirm the preview's tolerance, Smooth option, selected-only scope, actual target-line set, and geometry revision still match the current settings. If any differ, recalculate instead of applying stale coordinates. Keep this safety check even if the ComboBox event handling changes.
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

### Real-file acceptance checks — 4 November 2026
- The source file `winchester contour(1).kmz` contains 152 LineStrings
  with 132,621 coordinates.
- User-supplied edited KMZ results yielded 12,921 vertices at 1 m,
  5,371 at 5 m and 3,583 at 10 m, each with 176 LineStrings.
- A prior comparison of matched retained contours reported peak
  deviations from the original of about 0.9997 m, 4.9979 m and
  9.9948 m, respectively.
- The source and edited files have different structure (152 original
  placemarks versus 132 edited placemarks, with line pieces split).
  These tests support correct preset selection and retained-contour
  geometry, not a whole-document all-lines-only performance claim.
- The stale-preview fix is merged into `main` (PR #5). See
  [CHANGELOG.md](CHANGELOG.md) for the acceptance summary.
- A dedicated WPF ComboBox UI regression test is still desirable;
  the existing synthetic regression runner does not exercise its
  event ordering.

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

### Noncommercial license packaging
- The current source is offered under PolyForm Noncommercial 1.0.0,
  rather than the former MIT License.
- Do not remove the repository's `LICENSE` or `NOTICE`. The WPF
  project copies both into publish output; `Build-Release.ps1` verifies
  their presence, so both portable and installed packages include them.
- The installer displays the license during setup through
  `Installer/KmlScopedEditor.iss`.
- Existing MIT grants for historical versions cannot be revoked by
  relicensing future distributions. This change applies only to the
  project material whose copyright can lawfully be licensed here.
- Preserve third-party dependencies' separate licensing and notices.


### Current development version (not yet released)
- **1.3.0**

Project file:
- `Version 1.3.0`
- `FileVersion 1.3.0.0`
- `AssemblyVersion 1.3.0.0`

Version 1.3.0 is the current release target. The packaging scripts
read the version from the project file and name the new installer and
portable ZIP accordingly. Historical version 1.2.0 packages are not
version 1.3 release artifacts.

### Local build outputs
`Build-Release.ps1` produces:

- Published EXE:
  - `artifacts\publish\win-x64\KmlScopedEditor.exe`
- Portable ZIP:
  - `artifacts\portable\Simple-KML-Editor-Portable-1.3.0-win-x64.zip`
- Installer:
  - `artifacts\installer\Simple-KML-Editor-Setup-1.3.0.exe`

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
- It is deliberately retained so v1.3.0 upgrades existing v1.2.0 installations without creating a second installed product.

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
  - `Simple-KML-Editor-Setup-1.3.0`
  - `Simple-KML-Editor-Portable-1.3.0-win-x64`

Windows release packaging must produce version 1.3.0 artifacts and pass the installer build before the new version is distributed.

Note:
- GitHub Actions artifacts are downloaded as ZIP archives, even when the artifact contains a single installer EXE.

## 16. Repository artifacts

The repository has historically contained generated artifacts under `artifacts/`.

Be careful distinguishing:
- **committed historical artifacts** already present in the repo; and
- **new locally/generated build outputs**, which may exist only on the build machine or as GitHub Actions artifacts.

For version 1.3 testers, use only a newly built and verified version 1.3.0 installer. Previous version 1.2.0 packages remain historical artifacts.

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
