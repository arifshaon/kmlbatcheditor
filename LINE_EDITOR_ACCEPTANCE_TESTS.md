# Line Editor change requests — acceptance tests (24 November 2026)

These changes are implemented for **local testing** on branch
`fix/scoped-kml-style-resolution` (PR #6). They are **not yet merged to main**.
Use a copy of the test KML/KMZ and save to a different filename.

## REQ-1 — Simplify / smooth opens at 2 m

1. Open the Line Editor, Step 1 or Step 3, then **Simplify / smooth…**.
2. Confirm the tolerance shows **2 m**, Smooth curves remains **off**, and
   the first preview corresponds to 2 m.
3. Change to 5 m, close the panel, reopen: the tolerance must reset to **2 m**
   *before the new preview*; stale 5 m results must not appear.
4. Apply at 2 m and compare to the same source simplified after manually
   selecting 2 m. Existing 3 m and 5 m topology checks must remain stable.

## REQ-2 — Show line weights

1. Open a contour map with different `LineStyle/width` values (e.g. 1, 1.5, 2, 3).
2. **Show line weights** is checked by default. A width-2 contour should appear
   visibly bolder than width-1 across zoom levels.
3. Check that selection, hover, split lines and simplify preview remain visible
   on top of the thickest (up to 6 px) contours.
4. Uncheck **Show line weights**: all ordinary lines return to uniform grey.
   Recheck to restore relative thickness.
5. Edit a width in the main batch editor, reopen the Line Editor, and confirm
   the new effective width is shown.
6. Without applying geometry changes, close with Done, save and compare:
   style **values and XML elements** must not be altered by display rendering.

## REQ-3 — Confirm split scope before any change

1. Tick **Only split the selected lines** and select a group; Apply split
   should run without an extra prompt and affect only selected crossed lines.
2. Untick it while 5 lines are selected; Apply split should show three choices:
   **Split only the 5 selected lines** (default), **Split all M crossed lines**,
   and **Cancel**. Choosing selected-only should tick the checkbox.
3. With no selection, Apply split should show **Split all M crossed lines** and
   **Cancel**, with Cancel the default.
4. Cancel should leave a drawn or imported pending split line unchanged, and
   permit subsequent selection and Apply without redrawing the guide.
5. M is the number of distinct *currently crossed source lines*; for multiple
   pending guides, sequential splits may produce more pieces.
6. If the checkbox stays unticked when the Line Editor is reopened, the
   confirmation must still appear.

## REQ-4 — Preserve selection across splits

1. Select 10 lines, split across 4: the remaining 6 selected source lines and
   **all** pieces from the 4 split lines must stay selected.
2. Switch to Step 2 and split the same selected group again, confirming the
   selection continues to include split descendants, without widening scope.
3. Verify unselected lines and unselected generated pieces stay unselected.
4. After splitting, in Step 3 press **Delete selected pieces**: a warning must
   show the number being deleted, with **No / Cancel** as the default. Cancel
   must not change geometry or selection.
5. Clear selection explicitly, choose only the pieces intended for removal,
   and verify ordinary deletion continues normally.
6. Undo a split: verify both coordinates and the previously selected lines
   are restored. Undo a deletion: verify the previous selection is restored
   as well.
7. Check helper lines removed by **Use selected line** do not appear in the
   carried selection.

## Regression and performance

- Run `dotnet build KmlScopedEditor/KmlScopedEditor.csproj -c Release`.
- Run `dotnet run --project KmlScopedEditor.RegressionTests/KmlScopedEditor.RegressionTests.csproj -c Release`.
- Confirm previous geometry and style-safety regression checks remain green.
- Test zoom/pan, multi-guide splitting, Undo and on-map updates using the
  tester's approximately 4,700-line KMZ. Line-weight rendering caches pens
  by pixel width and zoom bucket; geometry must not be recalculated solely
  because the toggle changes.
- Verify no unintended changes to style values, line coordinates, embedded
  icons or non-selected placemarks in saved KML/KMZ.

## Reporting feedback

Please provide the original KMZ, the edited output, the line-selection count,
the expected and actual number of split pieces, and screenshots if a warning
or rendering mismatch appears. PR #6 remains unmerged until acceptance.
