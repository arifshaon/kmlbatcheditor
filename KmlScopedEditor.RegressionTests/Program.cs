using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Xml.Linq;
using KmlScopedEditor.Services;
using KmlScopedEditor.Models;
using KmlScopedEditor.Views;

using Point = System.Windows.Point;

internal static class Program
{
    private static readonly XNamespace Kml = "http://www.opengis.net/kml/2.2";
    private static int _passed;

    private static void Main()
    {
        // Select side replaces the old Clip classifier; preserve
        // independent geometry/deletion checks alongside the new tests.
        Run(nameof(SideSelectionTwoSides), SideSelectionTwoSides);
        Run(nameof(SideSelectionCrossingLongSegment), SideSelectionCrossingLongSegment);
        Run(nameof(SideSelectionThreeMetreLoop), SideSelectionThreeMetreLoop);
        Run(nameof(SideSelectionBoundaryPoints), SideSelectionBoundaryPoints);
        Run(nameof(SideSelectionOpenComplement), SideSelectionOpenComplement);
        Run(nameof(SideSelectionShortEdges), SideSelectionShortEdges);
        Run(nameof(SideSelectionAfterRealClosedSplit), SideSelectionAfterRealClosedSplit);
        Run(nameof(SideSelectionAfterRealOpenSplit), SideSelectionAfterRealOpenSplit);
        Run(nameof(SideSelectionAfterLimitedRealSplit), SideSelectionAfterLimitedRealSplit);
        Run(nameof(SideRepairPromptRememberedUntilRevisionChanges), SideRepairPromptRememberedUntilRevisionChanges);
        Run(nameof(SideSelectionLargeIndexBenchmark), SideSelectionLargeIndexBenchmark);
        Run(nameof(SelfCrossingLoopIsDetected), SelfCrossingLoopIsDetected);
        Run(nameof(OpenBoundaryDisplayStopsAtMapEdge), OpenBoundaryDisplayStopsAtMapEdge);
        Run(nameof(SelectedScopeDoesNotTouchOthers), SelectedScopeDoesNotTouchOthers);
        Run(nameof(SplitDefaultsToSelectedButUsesAllIfNoneSelected), SplitDefaultsToSelectedButUsesAllIfNoneSelected);
        Run(nameof(PointsAndPolygonsSurviveLineDeletion), PointsAndPolygonsSurviveLineDeletion);
        Run(nameof(StyleEditPreservesUntickedSettingsAndScope), StyleEditPreservesUntickedSettingsAndScope);
        Run(nameof(DuplicateStylesInSeparateDocumentsResolveLocally), DuplicateStylesInSeparateDocumentsResolveLocally);
        Run(nameof(DuplicateStyleMapsUseElementCloneCache), DuplicateStyleMapsUseElementCloneCache);
        Run(nameof(AmbiguousSameDocumentStylesAreSkipped), AmbiguousSameDocumentStylesAreSkipped);
        Run(nameof(ExternalStyleAndExternalMapPairAreSkipped), ExternalStyleAndExternalMapPairAreSkipped);
        Run(nameof(StyleMapCyclesAreSkipped), StyleMapCyclesAreSkipped);
        Run(nameof(CloneIdsAvoidEveryExistingObjectId), CloneIdsAvoidEveryExistingObjectId);
        Run(nameof(InlineIdCollisionIsNotSilentlyResolved), InlineIdCollisionIsNotSilentlyResolved);
        Console.WriteLine($"PASS: {_passed} regression checks.");
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    // End-to-end style tests use the real batch editing service, resolver and
    // inspector. They prevent unselected style properties from being lost.
    private static XElement SharedStyle(
        string id, string width, string iconScale = "0.3",
        string labelScale = "0.6", string? href = null) =>
        new(Kml + "Style",
            new XAttribute("id", id),
            new XElement(Kml + "LineStyle",
                new XElement(Kml + "color", "ff000000"),
                new XElement(Kml + "width", width)),
            new XElement(Kml + "IconStyle",
                new XElement(Kml + "scale", iconScale),
                new XElement(Kml + "Icon",
                    new XElement(Kml + "href", href ?? "http://example.org/icon.png"))),
            new XElement(Kml + "LabelStyle",
                new XElement(Kml + "scale", labelScale)));

    private static XElement StyledPlacemark(string name, string styleUrl) =>
        new(Kml + "Placemark",
            new XElement(Kml + "name", name),
            new XElement(Kml + "styleUrl", styleUrl),
            new XElement(Kml + "LineString",
                new XElement(Kml + "coordinates", "0,0 1,1")));

    private static XElement StyleMap(string id, string normal, string highlight) =>
        new(Kml + "StyleMap", new XAttribute("id", id),
            new XElement(Kml + "Pair",
                new XElement(Kml + "key", "normal"),
                new XElement(Kml + "styleUrl", normal)),
            new XElement(Kml + "Pair",
                new XElement(Kml + "key", "highlight"),
                new XElement(Kml + "styleUrl", highlight)));

    private static KmlDocumentContext StyleContext(XDocument document)
    {
        var context = new KmlDocumentContext
        {
            SourcePath = "test.kml",
            LoadedKmlPath = "test.kml",
            Document = document,
            RootNode = new KmlTreeNode
            {
                Name = "Test", NodeType = KmlNodeType.Document,
                SourceElement = document.Root
            },
            Placemarks = document.Descendants(Kml + "Placemark").ToList()
        };
        context.RebuildStyleIndexes();
        return context;
    }

    private static KmlBatchEditSettings ColorOnly() => new()
    {
        ChangeLineColor = true,
        LineColorText = "#FF0000"
    };

    private static void StyleEditPreservesUntickedSettingsAndScope()
    {
        var selected = StyledPlacemark("selected", "#contour");
        var untouched = StyledPlacemark("untouched", "#contour");
        using var ctx = StyleContext(new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                SharedStyle("contour", "2.5"), selected, untouched))));
        var edit = new KmlBatchEditService();
        Require(edit.Preview(new[] { selected }, ctx, ColorOnly()).CanApply,
            "Normal shared style should be safely editable.");
        var result = edit.Apply(new[] { selected }, ctx, ColorOnly());
        Require(result.PlacemarksChanged == 1, "Exactly one selected placemark must change.");
        Require((string?)untouched.Element(Kml + "styleUrl") == "#contour",
            "Unselected placemark's style URL changed.");
        var inspector = new KmlStyleInspector();
        var selectedStyle = inspector.Inspect(selected, ctx);
        var untouchedStyle = inspector.Inspect(untouched, ctx);
        Require(selectedStyle.LineWidth == "2.5" && selectedStyle.IconScale == "0.3" &&
            selectedStyle.LabelScale == "0.6", "Unselected settings were lost.");
        Require(untouchedStyle.LineWidth == "2.5" &&
            untouchedStyle.LineColor == "ff000000", "Unselected style was mutated.");
        Require(selectedStyle.LineColor != untouchedStyle.LineColor,
            "Selected line color was not changed.");
    }

    private static void DuplicateStylesInSeparateDocumentsResolveLocally()
    {
        var first = StyledPlacemark("first", "#inline");
        var second = StyledPlacemark("second", "#inline");
        var untouched = StyledPlacemark("untouched", "#inline");
        using var ctx = StyleContext(new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                SharedStyle("inline", "1"),
                new XElement(Kml + "Document",
                    SharedStyle("inline", "2.5"), first, untouched),
                new XElement(Kml + "Document",
                    SharedStyle("inline", "4.5"), second)))));
        var edit = new KmlBatchEditService();
        var preview = edit.Preview(new[] { first, second }, ctx, ColorOnly());
        Require(preview.CanApply && preview.RecoveredDuplicateReferenceCount == 2,
            "Nearest Document recovery should select two distinct styles.");
        var result = edit.Apply(new[] { first, second }, ctx, ColorOnly());
        Require(result.SharedStylesCloned == 2 && result.PlacemarksChanged == 2,
            "Two original style elements should produce separate clones.");
        var inspect = new KmlStyleInspector();
        Require(inspect.Inspect(first, ctx).LineWidth == "2.5" &&
            inspect.Inspect(second, ctx).LineWidth == "4.5",
            "Clones crossed Document style boundaries.");
        Require(inspect.Inspect(untouched, ctx).LineColor == "ff000000",
            "Unselected placemark must retain original color.");
        Require((string?)first.Element(Kml + "styleUrl") !=
            (string?)second.Element(Kml + "styleUrl"),
            "Different source elements incorrectly reused the same clone.");
    }

    private static void DuplicateStyleMapsUseElementCloneCache()
    {
        var first = StyledPlacemark("first", "#map");
        var second = StyledPlacemark("second", "#map");
        using var ctx = StyleContext(new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                new XElement(Kml + "Document",
                    SharedStyle("s", "2.5"), StyleMap("map", "#s", "#s"), first),
                new XElement(Kml + "Document",
                    SharedStyle("s", "4.5"), StyleMap("map", "#s", "#s"), second)))));
        var edit = new KmlBatchEditService();
        var preview = edit.Preview(new[] { first, second }, ctx, ColorOnly());
        Require(preview.CanApply && preview.SkippedPlacemarkCount == 0,
            "Document-local StyleMap pairs should resolve safely.");
        var result = edit.Apply(new[] { first, second }, ctx, ColorOnly());
        Require(result.StyleMapsCloned == 2 && result.SharedStylesCloned == 2,
            "StyleMaps and referenced styles must be cloned by element identity.");
        var inspector = new KmlStyleInspector();
        Require(inspector.Inspect(first, ctx).LineWidth == "2.5" &&
            inspector.Inspect(second, ctx).LineWidth == "4.5",
            "Duplicate StyleMap resolution lost original widths.");
    }

    private static void AmbiguousSameDocumentStylesAreSkipped()
    {
        var target = StyledPlacemark("ambiguous", "#duplicate");
        using var ctx = StyleContext(new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                SharedStyle("duplicate", "2"),
                SharedStyle("duplicate", "5"), target))));
        var edit = new KmlBatchEditService();
        var preview = edit.Preview(new[] { target }, ctx, ColorOnly());
        Require(!preview.CanApply && preview.AmbiguousStyleReferenceCount == 1 &&
            preview.DuplicateIdCount > 0, "Ambiguous references need a preview warning.");
        var before = target.ToString();
        var result = edit.Apply(new[] { target }, ctx, ColorOnly());
        Require(result.PlacemarksChanged == 0 && result.SkippedPlacemarkCount == 1 &&
            target.ToString() == before, "Ambiguous placemarks must remain unchanged.");
    }

    private static void ExternalStyleAndExternalMapPairAreSkipped()
    {
        var external = StyledPlacemark("external", "styles.kml#contour");
        var mapTarget = StyledPlacemark("map-target", "#map");
        using var ctx = StyleContext(new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                SharedStyle("contour", "2.5"),
                StyleMap("map", "#contour", "styles.kml#highlight"),
                external, mapTarget))));
        var edit = new KmlBatchEditService();
        var preview = edit.Preview(new[] { external, mapTarget }, ctx, ColorOnly());
        Require(!preview.CanApply && preview.ExternalStyleReferenceCount == 2,
            "External references, including StyleMap pairs, must be reported.");
        var before = external.ToString() + mapTarget.ToString();
        var result = edit.Apply(new[] { external, mapTarget }, ctx, ColorOnly());
        Require(result.SkippedPlacemarkCount == 2 && result.PlacemarksChanged == 0,
            "External references must be left alone.");
        Require(before == external.ToString() + mapTarget.ToString(),
            "External or StyleMap references were rewritten.");
    }

    private static void StyleMapCyclesAreSkipped()
    {
        var target = StyledPlacemark("cycle", "#cycle");
        using var ctx = StyleContext(new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                StyleMap("cycle", "#cycle", "#cycle"), target))));
        var edit = new KmlBatchEditService();
        var preview = edit.Preview(new[] { target }, ctx, ColorOnly());
        Require(!preview.CanApply && preview.CyclicStyleReferenceCount == 1,
            "StyleMap cycle should be reported and skipped.");
        var result = edit.Apply(new[] { target }, ctx, ColorOnly());
        Require(result.PlacemarksChanged == 0, "StyleMap cycle should not be edited.");
    }

    private static void CloneIdsAvoidEveryExistingObjectId()
    {
        var target = StyledPlacemark("target", "#contour");
        using var ctx = StyleContext(new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                SharedStyle("contour", "2.5"),
                new XElement(Kml + "Folder", new XAttribute("id", "contour_edited")),
                target))));
        var result = new KmlBatchEditService().Apply(
            new[] { target }, ctx, ColorOnly());
        Require(result.PlacemarksChanged == 1 &&
            (string?)target.Element(Kml + "styleUrl") == "#contour_edited_2",
            "Clone must not collide with Folder ID.");
        var ids = ctx.Document.Descendants()
            .Attributes("id").Select(attribute => attribute.Value).ToList();
        Require(ids.Distinct(StringComparer.Ordinal).Count() == ids.Count,
            "Generated clone ID must be unique throughout KML.");
    }

    private static void InlineIdCollisionIsNotSilentlyResolved()
    {
        var target = StyledPlacemark("target", "#same");
        var shadow = new XElement(Kml + "Placemark",
            new XElement(Kml + "name", "inline-style-owner"),
            SharedStyle("same", "4.5"));
        using var ctx = StyleContext(new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                SharedStyle("same", "2.5"), shadow, target))));
        var preview = new KmlBatchEditService().Preview(
            new[] { target }, ctx, ColorOnly());
        Require(!preview.CanApply && preview.AmbiguousStyleReferenceCount == 1,
            "ID-bearing inline Style must not silently shadow a shared style.");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private static KmlLineGeometryService Geometry(
        params (string Name, (double Lon, double Lat)[] Points)[] lines)
    {
        var document = new XDocument(new XElement(Kml + "kml",
            new XElement(Kml + "Document",
                lines.Select(line => new XElement(Kml + "Placemark",
                    new XElement(Kml + "name", line.Name),
                    new XElement(Kml + "LineString",
                        new XElement(Kml + "coordinates", string.Join(" ",
                            line.Points.Select(p =>
                                $"{p.Lon.ToString("R", CultureInfo.InvariantCulture)},{p.Lat.ToString("R", CultureInfo.InvariantCulture)}")))))))));
        return new KmlLineGeometryService(document);
    }

    private static IReadOnlyList<Point> Loop(
        KmlLineGeometryService geometry,
        params (double Lon, double Lat)[] coords) =>
        coords.Select(p => geometry.Project(new KmlCoordinate(
            p.Lon, p.Lat, null))).ToList();

    private static HashSet<string> Names(
        KmlLineGeometryService geometry,
        IReadOnlyList<XElement> lines) =>
        geometry.Features
            .Where(f => lines.Contains(f.LineStringElement))
            .Select(f => f.Name).ToHashSet();

    private static void OpenBoundaryDisplayStopsAtMapEdge()
    {
        var geometry = Geometry(
            ("south", new[] { (-.002, -.001), (.002, -.001) }),
            ("north", new[] { (-.002, .001), (.002, .001) }));
        var line = new[] { (-.0005, -.0005), (.0005, .0005) }
            .Select(p => geometry.Project(new KmlCoordinate(p.Item1, p.Item2, null)))
            .ToList();
        Require(KmlBoundaryClipService.TryPrepareOpenBoundary(
                line, geometry.WorldBounds,
                KmlBoundaryClipService.WorldUnitsFromMetres(10d),
                out var prepared, out var error) && prepared is not null,
            "Open divider refused: " + error);

        var bounds = geometry.WorldBounds;
        const double eps = 1e-12;
        bool Inside(Point p) =>
            p.X >= bounds.Left - eps && p.X <= bounds.Right + eps &&
            p.Y >= bounds.Top - eps && p.Y <= bounds.Bottom + eps;
        bool OnEdge(Point p) =>
            Math.Abs(p.X - bounds.Left) < eps || Math.Abs(p.X - bounds.Right) < eps ||
            Math.Abs(p.Y - bounds.Top) < eps || Math.Abs(p.Y - bounds.Bottom) < eps;

        var display = KmlBoundaryClipService.ClipOpenBoundaryForDisplay(prepared!, bounds);
        Require(display.SelectMany(path => path).All(Inside),
            "Displayed extensions must stay within the map extent.");
        Require(OnEdge(display[0][0]) && OnEdge(display[^1][^1]),
            "Displayed extensions must end exactly on the map edge.");
        Require(!prepared!.ExtendedPath.All(Inside),
            "The selection boundary must still extend beyond the map extent.");
    }

    private static void SelfCrossingLoopIsDetected()
    {
        var loop = new[]
        {
            new Point(0,0),new Point(.002,.002),new Point(0,.002),
            new Point(.002,0),new Point(0,0)
        };
        Require(KmlBoundaryClipService.LoopSelfIntersects(loop),
            "Figure-eight boundary must be warned about.");
    }

    private static void SelectedScopeDoesNotTouchOthers()
    {
        var geometry = Geometry(
            ("selected", new[] { (-.002, 0d), (.002, 0d) }),
            ("untouched", new[] { (-.002, .001), (.002, .001) }));
        var marker = XName.Get("test-target", "urn:split-tests");
        geometry.Features.Single(f => f.Name == "selected")
            .LineStringElement.SetAttributeValue(marker, "true");
        var before = geometry.Features.Single(f => f.Name == "untouched")
            .LineStringElement.ToString(SaveOptions.DisableFormatting);
        var cut = Loop(geometry, (0, -.003), (0, .003));
        var result = geometry.SplitWithStroke(cut, marker);
        Require(result.LinesSplit == 1, "Selected scope should split one line.");
        var stillUntouched = geometry.Features.Single(f => f.Name == "untouched")
            .LineStringElement.ToString(SaveOptions.DisableFormatting);
        Require(stillUntouched == before,
            "Unselected line changed after scoped split.");
    }

    private static void SplitDefaultsToSelectedButUsesAllIfNoneSelected()
    {
        var initialSession = new LineEditorSessionState(
            Array.Empty<LineEditorSessionGuideState>(),
            false, "#FF8E84A8", 2d);
        Require(initialSession.LimitToSelectedLines,
            "Version 1.3 must default to split selected lines.");
        Require(!(initialSession with { LimitToSelectedLines = false })
                .LimitToSelectedLines,
            "Users must still be able to choose splitting all lines.");

        // The fallback deliberately passes no target marker when the
        // selection is empty, regardless of the checked preference.
        var geometry = Geometry(
            ("first", new[] { (-.001, -.0005), (.001, -.0005) }),
            ("second", new[] { (-.001, .0005), (.001, .0005) }));
        var cut = Loop(geometry, (0, -.002), (0, .002));
        var selectedTargets = Array.Empty<XElement>();
        var marker = XName.Get("scope-target", "urn:regression");
        var targetAttribute = initialSession.LimitToSelectedLines &&
            selectedTargets.Length > 0 ? marker : null;
        var split = geometry.SplitWithStroke(cut, targetAttribute);
        Require(split.LinesSplit == 2,
            "With zero selected lines Apply split must still affect all eligible lines.");
    }

    private static void PointsAndPolygonsSurviveLineDeletion()
    {
        var geometry = Geometry(
            ("remove", new[] { (0d, 0d), (.001, 0d) }));
        var doc = geometry.Features.Single().LineStringElement.Document!;
        var container = doc.Root!.Element(Kml + "Document")!;
        container.Add(new XElement(Kml + "Placemark",
            new XElement(Kml + "Point",
                new XElement(Kml + "coordinates", "0,0"))));
        container.Add(new XElement(Kml + "Placemark",
            new XElement(Kml + "Polygon",
                new XElement(Kml + "outerBoundaryIs",
                    new XElement(Kml + "LinearRing",
                        new XElement(Kml + "coordinates",
                            "0,0 1,0 1,1 0,0"))))));
        geometry.DeleteLines(geometry.Features.Select(f => f.LineStringElement));
        Require(doc.Descendants(Kml + "Point").Count() == 1 &&
                doc.Descendants(Kml + "Polygon").Count() == 1,
            "Clip must never remove points and polygon elements.");
    }

    private static bool SelectSide(
        KmlLineGeometryService geometry,
        IReadOnlyList<IReadOnlyList<Point>> loops,
        PreparedOpenBoundary? open,
        Point click,
        out SideSelectionResult result)
    {
        var ok = KmlBoundaryClipService.TrySelectSide(
            geometry.Features, loops, open, click, out result, out var error);
        if (!ok)
            Console.WriteLine("  Side selection refused: " + error);
        return ok;
    }

    private static void SideSelectionTwoSides()
    {
        var geometry = Geometry(
            ("inner", new[] { (.0002, .0002), (.0003, .0003) }),
            ("outer", new[] { (.003, .003), (.004, .004) }));
        var loop = Loop(geometry,
            (0, 0), (.001, 0), (.001, .001), (0, .001), (0, 0));
        Require(SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(.0005, .0005, null)), out var inside),
            "Unable to select inside.");
        Require(Names(geometry, inside.ClickedSide).SetEquals(new[] { "inner" }),
            "Inside click selected wrong line.");
        Require(SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(.003, .003, null)), out var outside),
            "Unable to select outside.");
        Require(Names(geometry, outside.ClickedSide).SetEquals(new[] { "outer" }),
            "Outside click selected wrong line.");
    }

    private static void SideSelectionCrossingLongSegment()
    {
        var geometry = Geometry(
            ("crossing", new[] { (-.002, .0005), (.003, .0005) }),
            ("outside", new[] { (.003, .003), (.004, .004) }));
        var loop = Loop(geometry,
            (0, 0), (.001, 0), (.001, .001), (0, .001), (0, 0));
        Require(SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(.003, .003, null)), out var result),
            "Side selection unexpectedly failed.");
        Require(Names(geometry, result.Crossing).SetEquals(new[] { "crossing" }),
            "Long crossing segment was missed by sample-only classification.");
    }

    private static void SideSelectionThreeMetreLoop()
    {
        const double metres = 1d / 111_195d;
        var geometry = Geometry(
            ("2m-vertices", new[]
            {
                (-2 * metres, 1.5 * metres),
                (0 * metres, 1.5 * metres),
                (2 * metres, 1.5 * metres),
                (4 * metres, 1.5 * metres),
                (6 * metres, 1.5 * metres)
            }),
            ("away", new[] { (-.001, -.001), (-.002, -.002) }));
        var loop = Loop(geometry,
            (0, 0), (3 * metres, 0), (3 * metres, 3 * metres),
            (0, 3 * metres), (0, 0));
        Require(SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(-.001, -.001, null)), out var result),
            "Side selection refused valid 3-metre loop.");
        Require(Names(geometry, result.Crossing).Contains("2m-vertices"),
            "3m loop crossing was not detected for 2m vertex spacing.");
    }

    private static void SideSelectionBoundaryPoints()
    {
        var geometry = Geometry(
            ("on", new[] { (.0002, 0d), (.0003, 0d) }),
            ("inside", new[] { (.0002, .0002), (.0003, .0003) }));
        var loop = Loop(geometry,
            (0, 0), (.001, 0), (.001, .001), (0, .001), (0, 0));
        Require(SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(.0005, .0005, null)), out var result),
            "Inside click refused.");
        Require(Names(geometry, result.OnBoundary).Contains("on"),
            "Boundary-aligned piece must not be selected.");
        Require(!SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(.0005, 0, null)), out _),
            "Click directly on boundary must be refused.");
    }

    private static void SideSelectionOpenComplement()
    {
        var geometry = Geometry(
            ("above", new[] { (-.0005, .0004), (.0005, .0004) }),
            ("below", new[] { (-.0005, -.0004), (.0005, -.0004) }));
        var guide = Loop(geometry, (-.002, 0), (.002, 0));
        Require(KmlBoundaryClipService.TryPrepareOpenBoundary(
            guide, geometry.WorldBounds,
            KmlBoundaryClipService.WorldUnitsFromMetres(10d),
            out var prepared, out var error), error);
        Require(SelectSide(geometry, Array.Empty<IReadOnlyList<Point>>(),
            prepared, geometry.Project(new KmlCoordinate(0, .0003, null)),
            out var above), "Open above click failed.");
        Require(SelectSide(geometry, Array.Empty<IReadOnlyList<Point>>(),
            prepared, geometry.Project(new KmlCoordinate(0, -.0003, null)),
            out var below), "Open below click failed.");
        Require(Names(geometry, above.ClickedSide).SetEquals(new[] { "above" }),
            "Above side selection wrong.");
        Require(Names(geometry, below.ClickedSide).SetEquals(new[] { "below" }),
            "Below side selection wrong.");
    }

    private static void SideSelectionShortEdges()
    {
        var geometry = Geometry(
            ("inside", new[] { (.0003, .0003), (.0004, .0004) }),
            ("outside", new[] { (-.0003, -.0003), (-.0004, -.0004) }));
        var loop = Loop(geometry,
            (0, 0), (.0000009, 0), (.001, 0), (.001, .001),
            (0, .001), (0, 0));
        Require(SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(.0005, .0005, null)),
            out var result), "Click inside short-edge loop refused.");
        Require(Names(geometry, result.ClickedSide).SetEquals(new[] { "inside" }),
            "0.1 m boundary edge corrupted selection.");
    }

    // Every classification in these tests runs against the actual output
    // of KmlLineGeometryService.SplitWithStroke, not untouched input lines.
    // Split-created vertices can fall just off the boundary through rounding.
    private static void SideSelectionAfterRealClosedSplit()
    {
        var geometry = Geometry(
            ("horizontal", new[] { (-.002, .0005), (.003, .0005) }),
            ("vertical", new[] { (.0005, -.002), (.0005, .003) }),
            ("inside", new[] { (.0002, .0002), (.0003, .0003) }),
            ("outside", new[] { (.003, .003), (.004, .004) }));
        var loop = Loop(geometry,
            (0, 0), (.001, 0), (.001, .001), (0, .001), (0, 0));

        var split = geometry.SplitWithStroke(loop);
        Require(split.LinesSplit >= 2,
            "Expected the closed guide to split the real crossing lines.");

        Require(SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(.0005, .0004, null)),
            out var inside), "Closed inside click failed after real split.");
        Require(inside.Crossing.Count == 0,
            $"Real closed split left {inside.Crossing.Count} false crossings.");

        Require(SelectSide(geometry, new[] { loop }, null,
            geometry.Project(new KmlCoordinate(.003, .003, null)),
            out var outside), "Closed outside click failed after real split.");
        Require(outside.Crossing.Count == 0,
            "Real closed split must have zero crossings on both sides.");
        Require(inside.ClickedSide.Count == outside.OtherSide.Count &&
                outside.ClickedSide.Count == inside.OtherSide.Count,
            "Split closed sides must be complementary.");
    }

    private static void SideSelectionAfterRealOpenSplit()
    {
        var geometry = Geometry(
            ("vertical-a", new[] { (-.0005, -.001), (-.0005, .001) }),
            ("vertical-b", new[] { (.0005, -.001), (.0005, .001) }),
            ("above", new[] { (-.0003, .0004), (.0003, .0004) }),
            ("below", new[] { (-.0003, -.0004), (.0003, -.0004) }));
        var guide = Loop(geometry, (-.002, 0), (.002, 0));

        var split = geometry.SplitWithStroke(guide);
        Require(split.LinesSplit >= 2,
            "Expected open guide to split two real lines.");
        Require(KmlBoundaryClipService.TryPrepareOpenBoundary(
            guide, geometry.WorldBounds,
            KmlBoundaryClipService.WorldUnitsFromMetres(10d),
            out var prepared, out var error), error);

        Require(SelectSide(geometry, Array.Empty<IReadOnlyList<Point>>(),
            prepared, geometry.Project(new KmlCoordinate(0, .0006, null)),
            out var above), "Open above click failed after real split.");
        Require(above.Crossing.Count == 0,
            $"Real open split left {above.Crossing.Count} false crossings.");
        Require(SelectSide(geometry, Array.Empty<IReadOnlyList<Point>>(),
            prepared, geometry.Project(new KmlCoordinate(0, -.0006, null)),
            out var below), "Open below click failed after real split.");
        Require(below.Crossing.Count == 0,
            "Real open split must have zero crossings on both sides.");
        Require(above.ClickedSide.Count == below.OtherSide.Count &&
                below.ClickedSide.Count == above.OtherSide.Count,
            "Real open split sides must be complementary.");
    }

    private static void SideSelectionAfterLimitedRealSplit()
    {
        var lines = Enumerable.Range(0, 18)
            .Select(index => (
                $"line-{index}",
                new[] {
                    (-.001, (index - 9) * .00012),
                    (.001, (index - 9) * .00012)
                }))
            .ToArray();
        var geometry = Geometry(lines);
        var marker = XName.Get("selected-split", "urn:regression");
        foreach (var feature in geometry.Features.Take(5))
            feature.LineStringElement.SetAttributeValue(marker, "true");

        var guide = Loop(geometry, (0, -.003), (0, .003));
        var split = geometry.SplitWithStroke(guide, marker);
        Require(split.LinesSplit == 5,
            "Selected-only split should affect exactly 5 of 18 lines.");

        Require(KmlBoundaryClipService.TryPrepareOpenBoundary(
            guide, geometry.WorldBounds,
            KmlBoundaryClipService.WorldUnitsFromMetres(10d),
            out var prepared, out var error), error);
        Require(SelectSide(geometry, Array.Empty<IReadOnlyList<Point>>(),
            prepared, geometry.Project(new KmlCoordinate(-.0005, .0004, null)),
            out var result), "Selected-only side selection failed.");

        Require(result.Crossing.Count == 13,
            $"Expected the 13 unsplit lines to cross; found {result.Crossing.Count}.");
    }

    private static void SideRepairPromptRememberedUntilRevisionChanges()
    {
        var tracker = new SideRepairPromptTracker();
        var a = new XElement("line");
        var b = new XElement("line");
        var c = new XElement("line");

        Require(tracker.TakeNewCrossings(42, 3, new[] { a, b }).Count == 2,
            "First crossing offer must include both lines.");
        Require(tracker.TakeNewCrossings(42, 3, new[] { a, b }).Count == 0,
            "Declined crossing offer must not repeat on another click.");
        var newCrossings = tracker.TakeNewCrossings(42, 3, new[] { a, c });
        Require(newCrossings.Count == 1 &&
                ReferenceEquals(newCrossings[0], c),
            "Only newly discovered crossings should be offered.");

        tracker.RememberUnresolved(43, 3, new[] { b, c });
        Require(tracker.TakeNewCrossings(43, 3, new[] { b, c }).Count == 0,
            "Remaining crossings after repair must not trigger another prompt.");
        Require(tracker.TakeNewCrossings(44, 3, new[] { b }).Count == 1,
            "A later geometry revision should allow new repair decisions.");
        Require(tracker.TakeNewCrossings(44, 4, new[] { b }).Count == 1,
            "A different applied-guide sequence must invalidate offers.");
        tracker.Clear();
        Require(tracker.TakeNewCrossings(44, 4, new[] { b }).Count == 1,
            "Exiting Select side mode should clear offers.");
    }

    private static void SideSelectionLargeIndexBenchmark()
    {
        var lines = Enumerable.Range(0, 1500).Select(i =>
            ($"line-{i}", new[]
            {
                ((i % 60 - 30) / 10000d, (i / 60 - 12) / 10000d),
                ((i % 60 - 30) / 10000d + .0000003d,
                 (i / 60 - 12) / 10000d + .0000003d)
            })).ToArray();
        var geometry = Geometry(lines);
        var circle = Enumerable.Range(0, 2000).Select(i =>
        {
            var angle = 2d * Math.PI * i / 2000d;
            return (Math.Cos(angle) * .002d, Math.Sin(angle) * .002d);
        }).ToList();
        circle.Add(circle[0]);
        var loop = Loop(geometry, circle.ToArray());
        var stopwatch = Stopwatch.StartNew();
        Require(KmlBoundaryClipService.TryPrepareSideSelection(
            new[] { loop }, null, out var context, out var error), error);
        Require(context.TrySelect(geometry.Features,
            geometry.Project(new KmlCoordinate(.003, .003, null)),
            out var result, out error), error);
        stopwatch.Stop();
        Require(result.ClickedSide.Count + result.OtherSide.Count +
            result.OnBoundary.Count + result.Crossing.Count ==
            geometry.Features.Count, "Not all lines were categorised.");
        Console.WriteLine($"  2,000 boundary vertices / 1,500 lines: " +
            $"{stopwatch.ElapsedMilliseconds} ms (reference target <500 ms).");
    }


}
