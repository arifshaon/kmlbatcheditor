# Clip regression verification (Windows, .NET 8)

This document accompanies the Clip correctness and workflow fixes. Run the
geometry checks from the repository root on Windows:

```powershell
dotnet run --project KmlScopedEditor.RegressionTests/KmlScopedEditor.RegressionTests.csproj -c Release
```

The test runner uses the same compiled geometry service as the application,
without external test packages. It exercises: repeated closing coordinates;
two-loop union and complement; ~0.1 m edges; horizontal, vertical, diagonal,
and U-shaped open boundaries; self-crossing detection; length conservation;
selection scope; preserving Point and Polygon geometry; and classification of
1,500 lines against a 2,000-edge loop. The benchmark prints elapsed time;
the suggested target is below 500 ms on the reference Windows machine.

## Line Editor integration checks

These require the GUI; they cannot be established by geometry-only tests.

1. **Closed loops:** Import/draw one loop, Keep inside; Undo; Keep outside.
   Confirm complementary retained/deleted segments and no off-side segments.
2. **Two loops:** Keep inside means inside either loop; Keep outside is their
   complement. No boundary source is deleted.
3. **Open boundaries:** Test vertical, horizontal, diagonal, cut.kmz, and a U
   shape. Clicking the opposite side must invert the result without strange
   corner connections. Self-intersecting or non-dividing open boundaries
   should still be refused.
4. **Non-dividing open line:** Use a hook or ends facing each other; on refusal
   accept “Join its ends.” The pending guide becomes a closed loop, the
   original source line stays unchanged, and Ctrl+Z restores the open guide.
   Check insufficient distinct vertices are rejected.
5. **Use selected line:** Select an existing closed source contour, choose
   Use selected line, turn off the selected-only limit if no other lines are
   selected, then Clip. The source remains one intact LineString while other
   eligible lines are clipped. Repeat with multiple boundary sources.
   Separately verify Apply Split still removes helper sources as before.
6. **Scope:** Select several lines and enable Limit to selected. Preview
   identifies N selected eligible lines; unselected KML coordinate strings
   remain unchanged. With no selection, Clip refuses rather than expanding
   to the whole document.
7. **Preview exactness:** Preview Clip, change the boundary, selection, or
   document, then attempt Apply. A stale plan must never apply. With an
   unchanged preview, Apply must yield the exact geometry previewed.
8. **Undo:** Apply a Clip, Ctrl+Z, and inspect the full KML document and
   pending guide state. Undo restores the original lines. Then apply again.
9. **Preservation:** With Point, Polygon and GroundOverlay features in the
   same file, Clip must modify only eligible LineStrings.
10. **Persistence:** Turn off selected-only scope, close the editor without
    saving geometry, reopen it; the checkbox should remain off for the same
    document. Opening a new KML resets the session.
11. **Self-crossing loop:** Preview a figure-eight and confirm the even-odd
    warning appears; ensure displayed retained/delete regions match the rule.
12. **Performance:** With the Winchester dataset (~132,000 vertices),
    preview and apply a 557-point boundary and a ~2,400-point one.
    Record elapsed time and verify the UI remains usable. Apply must not
    recalculate the expensive classification.

## Known verification limits

The container used to prepare these changes does not have the .NET 8 SDK or
a Windows WPF runtime. A Windows clean rebuild and the GUI checks above are
required before describing the release as runtime-verified.
