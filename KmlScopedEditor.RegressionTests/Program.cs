using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Xml.Linq;
using KmlScopedEditor.Services;

using Point = System.Windows.Point;

internal static class Program
{
    private static readonly XNamespace Kml = "http://www.opengis.net/kml/2.2";
    private static int _passed;

    private static void Main()
    {
        Run(nameof(ClosedLoopWithRepeatedEndpoint), ClosedLoopWithRepeatedEndpoint);
        Run(nameof(TwoClosedLoopsUseUnion), TwoClosedLoopsUseUnion);
        Run(nameof(ShortBoundaryEdges), ShortBoundaryEdges);
        Run(nameof(OpenLineSidesAreComplementary), OpenLineSidesAreComplementary);
        Run(nameof(SelfCrossingLoopIsDetected), SelfCrossingLoopIsDetected);
        Run(nameof(SplitThenClipPreservesLength), SplitThenClipPreservesLength);
        Run(nameof(SelectedScopeDoesNotTouchOthers), SelectedScopeDoesNotTouchOthers);
        Run(nameof(PointsAndPolygonsSurviveLineDeletion), PointsAndPolygonsSurviveLineDeletion);
        Run(nameof(LargeBoundaryClassificationBenchmark), LargeBoundaryClassificationBenchmark);
        Run(nameof(SideSelectionTwoSides), SideSelectionTwoSides);
        Run(nameof(SideSelectionCrossingLongSegment), SideSelectionCrossingLongSegment);
        Run(nameof(SideSelectionThreeMetreLoop), SideSelectionThreeMetreLoop);
        Run(nameof(SideSelectionBoundaryPoints), SideSelectionBoundaryPoints);
        Run(nameof(SideSelectionOpenComplement), SideSelectionOpenComplement);
        Run(nameof(SideSelectionLargeIndexBenchmark), SideSelectionLargeIndexBenchmark);
        Console.WriteLine($"PASS: {_passed} regression checks.");
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
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

    private static void ClosedLoopWithRepeatedEndpoint()
    {
        var geometry = Geometry(
            ("inside", new[] { (0.0001, 0.0001), (0.0002, 0.0002) }),
            ("outside", new[] { (0.004, 0.004), (0.005, 0.005) }));
        var loop = Loop(geometry, (0, 0), (.001, 0), (.001, .001),
            (0, .001), (0, 0)); // repeated final coordinate
        var inside = KmlBoundaryClipService.ClassifyClosed(
            geometry.Features, new[] { loop }, true);
        var outside = KmlBoundaryClipService.ClassifyClosed(
            geometry.Features, new[] { loop }, false);
        Require(Names(geometry, inside.KeptLines).SetEquals(new[] { "inside" }),
            "Keep inside failed on closed loop with repeated endpoint.");
        Require(Names(geometry, outside.KeptLines).SetEquals(new[] { "outside" }),
            "Keep outside failed on closed loop with repeated endpoint.");
    }

    private static void TwoClosedLoopsUseUnion()
    {
        var geometry = Geometry(
            ("one", new[] { (.0002, .0002), (.0003, .0003) }),
            ("two", new[] { (.0042, .0002), (.0043, .0003) }),
            ("outside", new[] { (.009, .009), (.0092, .0092) }));
        var loops = new[]
        {
            Loop(geometry, (0,0),(.001,0),(.001,.001),(0,.001),(0,0)),
            Loop(geometry, (.004,0),(.005,0),(.005,.001),(.004,.001),(.004,0))
        };
        var inside = KmlBoundaryClipService.ClassifyClosed(
            geometry.Features, loops, true);
        var outside = KmlBoundaryClipService.ClassifyClosed(
            geometry.Features, loops, false);
        Require(Names(geometry, inside.KeptLines).SetEquals(new[] { "one", "two" }),
            "Two-loop inside must mean inside any loop.");
        Require(Names(geometry, outside.KeptLines).SetEquals(new[] { "outside" }),
            "Two-loop outside must be complement of inside any loop.");
    }

    private static void ShortBoundaryEdges()
    {
        var geometry = Geometry(
            ("inside", new[] { (.000010, .000010), (.000011, .000011) }),
            ("outside", new[] { (.000010, -.000010), (.000011, -.000011) }));
        // One edge is ~0.1 m long; no point ~1 m away can be "on" it.
        var loop = Loop(geometry, (0,0),(.0000009,0),(.00002,0),
            (.00002,.00002),(0,.00002),(0,0));
        var result = KmlBoundaryClipService.ClassifyClosed(
            geometry.Features, new[] { loop }, true);
        Require(Names(geometry, result.KeptLines).SetEquals(new[] { "inside" }),
            "Sub-metre loop edge incorrectly classified outside points.");
    }

    private static void OpenLineSidesAreComplementary()
    {
        foreach (var coords in new[]
        {
            new[] { (-.002, 0d), (.002, 0d) },
            new[] { (0d, -.002), (0d, .002) },
            new[] { (-.002, -.002), (.002, .002) },
            new[] { (-.002, 0d), (-.001, .001), (.001, .001), (.002, 0d) }
        })
        {
            var geometry = Geometry(
                ("upper", new[] { (-.0002, .0005), (.0002, .0005) }),
                ("lower", new[] { (-.0002, -.0005), (.0002, -.0005) }),
                ("left", new[] { (-.0005, -.0002), (-.0005, .0002) }),
                ("right", new[] { (.0005, -.0002), (.0005, .0002) }));
            var projected = coords.Select(p =>
                geometry.Project(new KmlCoordinate(p.Item1, p.Item2, null))).ToList();
            var ok = KmlBoundaryClipService.TryPrepareOpenBoundary(
                projected, geometry.WorldBounds,
                KmlBoundaryClipService.WorldUnitsFromMetres(10d),
                out var prepared, out var error);
            Require(ok && prepared is not null, "Open divider refused: " + error);

            // At least two clearly opposite points near the middle of
            // the divider. Compare by classifier sets, not arbitrary A/B labels.
            var firstPoint = coords[0].Item1 != coords[^1].Item1
                ? new Point(0, .0008)
                : new Point(.0008, 0);
            var secondPoint = coords[0].Item1 != coords[^1].Item1
                ? new Point(0, -.0008)
                : new Point(-.0008, 0);

            var aOK = KmlBoundaryClipService.TryClassifyOpen(
                geometry.Features, prepared!, firstPoint, null,
                out var a, out _, out error);
            Require(aOK, "First side classification refused: " + error);
            var bOK = KmlBoundaryClipService.TryClassifyOpen(
                geometry.Features, prepared!, secondPoint, null,
                out var b, out _, out error);
            Require(bOK, "Second side classification refused: " + error);
            var aSet = a.KeptLines.ToHashSet();
            var bSet = b.KeptLines.ToHashSet();
            Require(aSet.All(item => !bSet.Contains(item)) &&
                    aSet.Count + bSet.Count == geometry.Features.Count,
                "Two clicked sides must be complementary (for off-boundary lines).");
        }
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

    private static void SplitThenClipPreservesLength()
    {
        var geometry = Geometry(
            ("through", new[] { (-.002, .0005), (.003, .0005) }));
        var initialLength = Length(geometry.Features);
        var loop = Loop(geometry, (0,0),(.001,0),(.001,.001),
            (0,.001),(0,0));
        var split = geometry.SplitWithStroke(loop);
        Require(split.LinesSplit > 0, "Expected line crossing to split.");
        var inside = KmlBoundaryClipService.ClassifyClosed(
            geometry.Features, new[] { loop }, true);
        Require(inside.KeptLines.Count > 0 && inside.DeletedLines.Count > 0,
            "Split results should have pieces on both sides.");
        var insideSet = inside.KeptLines.ToHashSet();
        var lower = geometry.Project(new KmlCoordinate(0, 0, null)).X;
        var upper = geometry.Project(new KmlCoordinate(.001, 0, null)).X;
        var tolerance = KmlBoundaryClipService.WorldUnitsFromMetres(1.01d);
        foreach (var feature in geometry.Features
                     .Where(f => insideSet.Contains(f.LineStringElement)))
        {
            foreach (var segment in feature.WorldPoints.Zip(
                         feature.WorldPoints.Skip(1),
                         (a, b) => new Point((a.X + b.X) / 2d,
                                            (a.Y + b.Y) / 2d)))
                Require(segment.X >= lower - tolerance &&
                        segment.X <= upper + tolerance,
                    "An inside-kept segment midpoint lies on the wrong side.");
        }
        Require(Math.Abs(Length(geometry.Features) - initialLength) <
            KmlBoundaryClipService.WorldUnitsFromMetres(.01),
            "Split should conserve line length within 1 cm.");
    }

    private static double Length(IEnumerable<KmlLineFeature> lines) =>
        lines.Sum(line => line.WorldPoints.Zip(line.WorldPoints.Skip(1),
            (a, b) => (a - b).Length).Sum());

    private static void SelectedScopeDoesNotTouchOthers()
    {
        var geometry = Geometry(
            ("selected", new[] { (.003, .003), (.004, .004) }),
            ("untouched", new[] { (.003, .001), (.004, .001) }));
        var marker = XName.Get("test-target", "urn:clip-tests");
        geometry.Features.Single(f => f.Name == "selected")
            .LineStringElement.SetAttributeValue(marker, "true");
        var before = geometry.Features.Single(f => f.Name == "untouched")
            .LineStringElement.ToString(SaveOptions.DisableFormatting);
        var loop = Loop(geometry, (0,0),(.001,0),(.001,.001),
            (0,.001),(0,0));
        var result = KmlBoundaryClipService.ClassifyClosed(
            geometry.Features, new[] { loop }, true, marker);
        Require(result.DeletedLines.Count == 1, "Wrong target count.");
        geometry.DeleteLines(result.DeletedLines);
        Require(geometry.Features.Single().Name == "untouched" &&
                geometry.Features.Single().LineStringElement
                    .ToString(SaveOptions.DisableFormatting) == before,
            "Unselected line changed after scoped deletion.");
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

    private static void LargeBoundaryClassificationBenchmark()
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
        var result = KmlBoundaryClipService.ClassifyClosed(
            geometry.Features, new[] { loop }, true);
        stopwatch.Stop();
        Require(result.KeptLines.Count + result.DeletedLines.Count ==
            geometry.Features.Count, "Large boundary did not classify every line.");
        Console.WriteLine(
            $"  2,000-edge loop / 1,500 lines: {stopwatch.ElapsedMilliseconds} ms " +
            "(target <500 ms on reference Windows machine)");
    }
}
