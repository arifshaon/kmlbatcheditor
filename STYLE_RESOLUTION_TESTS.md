# KML style-resolution fix — Windows acceptance checklist

**Testing branch:** `fix/scoped-kml-style-resolution` (draft PR #6).
This is a test candidate, **not merged into `main`**. Application version remains
1.3.0; do not mistake this branch's tester package for a formal new release.

## Before you begin

1. Use the **portable ZIP** from the Windows CI build for PR #6 to avoid
   overwriting an installed version 1.3.0 during acceptance testing.
2. Work with a copy of the source KML/KMZ and use **Save As** to create a
   separately named edited file. The original input must remain available.
3. In Google Earth, record the pre-edit appearance of at least one selected
   contour and an unselected contour: line width, icon scale, label scale,
   line colour, and visible geometry.
4. In Simple KML Editor, select the contours to change. Tick **Line colour**
   only; explicitly leave width, icon size, label size and other options
   unticked. Review the Preview before applying.

## Cases to check

| Input pattern | Expected Preview | Expected result after Apply / Save As |
| --- | --- | --- |
| Normal unique shared Style / StyleMap | No duplicate warnings; eligible | Only selected line colours change; width/icon/label settings stay intact |
| Same Style ID in separate nested Documents | Duplicate-ID warning; one locally resolvable style per enclosing Document | Selected contours use correctly scoped originals; two different originals do not share a clone |
| Duplicate Style IDs within the *same* Document | Ambiguous references shown as **skipped** | Affected placemarks and their original style URLs remain unchanged |
| External `styles.kml#id` / URL fragment | External references shown as **skipped**, even when a local `#id` exists | Original external references stay unchanged |
| StyleMap pair with an external/ambiguous reference or a cycle | The affected placemark is **skipped** | No partial StyleMap rewrites or new inline overrides |
| ID collision with an existing Folder/Placemark ID | Safe local style still editable | Generated clone ID does not duplicate any existing ID |
| Mixed safe + unsafe selection | Eligible and skipped counts and example names shown | Only eligible placemarks are changed; skipped entries are untouched |

Check both **Style Diagnostics** and **Icon image / Icon variant** inventories
against the same resolved appearance. These must not silently show a
different style from the edit operation.

## Inspect saved output

- Reopen the saved KML/KMZ in Google Earth and in Simple KML Editor.
- Compare original and edited source references and style properties.
- Confirm unselected placemarks retain their original style URLs and values.
- Confirm any skipped placemarks remain untouched, and that no clone ID
  collides with existing object IDs.
- Existing duplicate IDs are an input-data problem and **are not
  automatically renamed** by this change. The tool warns, resolves only
  unambiguous cases, and avoids creating *new* collisions.

## Automated checks

From the repository root, on Windows with the .NET 8 SDK:

```powershell
dotnet build KmlScopedEditor/KmlScopedEditor.csproj -c Release
dotnet run --project KmlScopedEditor.RegressionTests/KmlScopedEditor.RegressionTests.csproj -c Release
```

The regression runner covers geometry and real style service operations.
Automated tests cannot establish the appearance of a user's original
Google Earth file, which needs the manual comparisons above.

## When reporting results

Include the original and edited KML/KMZ files, the name of a selected
and unselected placemark, which property was ticked, screenshots of the
Preview and Style Diagnostics (including warnings), and whether width,
icon size, and label size stayed unchanged. Do not overwrite the original.
