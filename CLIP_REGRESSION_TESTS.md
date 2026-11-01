# Select side regression and Windows acceptance checks

## Automated checks

On Windows, from the repository root:

```powershell
dotnet build KmlScopedEditor/KmlScopedEditor.csproj -c Release
dotnet run --project KmlScopedEditor.RegressionTests/KmlScopedEditor.RegressionTests.csproj -c Release
```

The geometry runner covers open and closed divider regions, a 0.1 m
boundary edge, source scope, point/polygon preservation, and true segment
crossings. Crucially, regression cases run the **real SplitWithStroke**
implementation first and verify that fresh closed/open split pieces show
**zero false crossings** and selected-only splitting of **5 of 18** lines
leaves exactly **13 genuine crossings**. The 3 m loop crossed by a line with
2 m vertex spacing remains detectable. Repair-offer tracking is tested across
repeated clicks, failed/declined repairs, geometry revisions and guide
sequences. The benchmark uses a 2,000-vertex boundary against 1,500 lines. The performance target on the
reference Windows machine is under **500 ms**; the test reports elapsed
milliseconds, not an assertion about arbitrary hardware.

## Manual integration acceptance

1. Confirm the **Version 1.2 Step 2** Split controls and behaviour:
   import/draw/use a line; Apply split; no selection targets all lines;
   checkbox is disabled when nothing is selected and initially unchecked.
   Existing source helper removal must remain unchanged.
2. **Closed loop:** cut.kmz → Close loop → Apply split → Step 3 Select side →
   click outside → Delete. **58 pieces remain**, as measured in the
   prototype. Recheck the result against the reference data.
3. **Open divider:** cut.kmz → Apply split → Select side → click below the
   line → authorize an extra split if prompted → **180 pieces below** are
   selected, as measured by the prototype. Delete only after inspection.
4. Check horizontal, vertical, diagonal, and U-shaped open guides, both
   sides, and opposite-side complementarity. Refuse a hook/ends-facing
   divider that does not partition the map.
5. Check narrow-loop crossing of a long segment whose endpoints and midpoint
   are outside, plus the 3 m / 2 m-spaced-points regression.
6. Crossing repair is offered once. If declined, crossings remain unselected.
   If accepted, only flagged crossing lines split, the selection is repeated,
   no prompt loop occurs, and the latest applied guide sequence is unchanged.
7. Undo deletes first, then the extra repair split while staying in Step 3,
   then the original split while returning to Step 2. A crossing repair
   declined or found ineffective must **not** repeatedly prompt on each
   subsequent click for the same geometry/guide revision.
8. Verify Step 3 Select side stays active on ordinary clicks and Delete, and
   exits on toggle, Escape, Pan, step change, Apply split, Undo, Done, Cancel
   and opening Simplify. No stale dashed overlay is left after exit.
9. Confirm selections restored across normal surface refresh, correct
   Shift-add / Alt-remove, and refusal of clicks within 1 m of the boundary.
10. **Ridge regression:** Preserve the previously measured result of
    **38 pieces (87.7%)**. Test normal Split, lasso, Simplify and Done.
11. **Simplify / smooth:** Default must be **Only selected lines** in Steps
    1 and 3. If zero lines are selected, do not silently simplify the whole
    document. Explicit All lines remains available.
12. Confirm Points, Polygons, mixed MultiGeometry, overlays, original source
    line deletion semantics and Save As are unaffected.

## Verification limitation

Source auditing alone is not enough. A successful Windows build, automated
geometry tests and manual checks with the reference files are required
before declaring the redesign release-ready.
