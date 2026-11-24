using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

/// <summary>
/// Resolves local KML style references consistently for editing, inspection,
/// and icon inventories. Duplicate IDs are invalid KML: for documents made by
/// merging files, recover only a single selector in the nearest owning Document.
/// Never interpret a file/URL fragment as a local reference.
/// </summary>
public sealed class KmlStyleReferenceResolver
{
    private static readonly XNamespace KmlNs = "http://www.opengis.net/kml/2.2";

    private readonly Dictionary<string, List<XElement>> _selectors =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _duplicateIds = new(StringComparer.Ordinal);

    public KmlStyleReferenceResolver(KmlDocumentContext context)
    {
        var idElements = context.Document.Descendants()
            .Where(element => element.Attribute("id") is not null)
            .ToList();

        foreach (var group in idElements.GroupBy(
                     element => (string)element.Attribute("id")!,
                     StringComparer.Ordinal))
        {
            if (group.Count() > 1)
                _duplicateIds.Add(group.Key);
        }

        foreach (var selector in idElements.Where(element =>
                     element.Name == KmlNs + "Style" ||
                     element.Name == KmlNs + "StyleMap"))
        {
            var id = (string)selector.Attribute("id")!;
            if (!_selectors.TryGetValue(id, out var candidates))
            {
                candidates = new List<XElement>();
                _selectors.Add(id, candidates);
            }

            candidates.Add(selector);
        }
    }

    public IReadOnlyCollection<string> DuplicateIds => _duplicateIds;

    public StyleReferenceResult Resolve(XElement referencingElement, string? styleUrl)
    {
        if (string.IsNullOrWhiteSpace(styleUrl))
            return new(StyleReferenceStatus.None);

        var reference = styleUrl.Trim();

        // A local fragment is strictly "#id". Relative URLs, absolute URLs,
        // and same-package file references are external to this KML document.
        if (!reference.StartsWith('#') || reference.Length < 2 ||
            reference.IndexOf('#', 1) >= 0)
        {
            return new(StyleReferenceStatus.External, null, reference,
                "External style reference is not edited.");
        }

        var id = reference[1..];
        if (!_selectors.TryGetValue(id, out var candidates) || candidates.Count == 0)
        {
            return new(StyleReferenceStatus.Missing, null, reference,
                "The local style reference could not be resolved.");
        }

        if (candidates.Count == 1 && !_duplicateIds.Contains(id))
            return Resolved(candidates[0], reference, recovered: false);

        // A style ID also duplicated by a non-style KML object is ambiguous.
        if (candidates.Count == 1)
        {
            return new(StyleReferenceStatus.Ambiguous, null, reference,
                "The style ID is also used by another KML object.");
        }

        foreach (var document in referencingElement.AncestorsAndSelf()
                     .Where(element => element.Name == KmlNs + "Document"))
        {
            var local = candidates.Where(candidate =>
                ReferenceEquals(OwningDocument(candidate), document)).ToList();

            if (local.Count == 1)
                return Resolved(local[0], reference, recovered: true);

            if (local.Count > 1)
            {
                return new(StyleReferenceStatus.Ambiguous, null, reference,
                    "Multiple styles with this ID exist in the same Document.");
            }
        }

        return new(StyleReferenceStatus.Ambiguous, null, reference,
            "Duplicate style IDs could not be resolved to one enclosing Document.");
    }

    /// <summary>
    /// Check every normal/highlight and nested pair before changing any map.
    /// A partial edit to a map with external or ambiguous pair references
    /// would silently produce an inconsistent appearance.
    /// </summary>
    public StyleReferenceResult ValidateStyleMap(XElement styleMap)
    {
        var ancestors = new HashSet<XElement>();
        return ValidateMap(styleMap, ancestors);
    }

    private StyleReferenceResult ValidateMap(
        XElement styleMap, HashSet<XElement> ancestors)
    {
        if (!ancestors.Add(styleMap))
        {
            return new(StyleReferenceStatus.Cyclic, null, null,
                "Cyclic StyleMap references are not edited.");
        }

        try
        {
            var pairs = styleMap.Elements(KmlNs + "Pair").ToList();
            if (pairs.Count == 0)
            {
                return new(StyleReferenceStatus.Missing, null, null,
                    "StyleMap contains no pairs.");
            }

            foreach (var pair in pairs)
            {
                var pairUrl = pair.Element(KmlNs + "styleUrl")?.Value;
                if (!string.IsNullOrWhiteSpace(pairUrl))
                {
                    var resolved = Resolve(pair, pairUrl);
                    if (!resolved.IsResolved)
                        return resolved;

                    if (resolved.Selector!.Name == KmlNs + "StyleMap")
                    {
                        var nested = ValidateMap(resolved.Selector, ancestors);
                        if (!nested.IsResolved)
                            return nested;
                    }
                }
                else if (pair.Element(KmlNs + "Style") is null)
                {
                    return new(StyleReferenceStatus.Missing, null, null,
                        "StyleMap pair contains no local style.");
                }
            }

            return Resolved(styleMap, null, recovered: false);
        }
        finally
        {
            ancestors.Remove(styleMap);
        }
    }

    private static XElement? OwningDocument(XElement element) =>
        element.Ancestors().FirstOrDefault(parent =>
            parent.Name == KmlNs + "Document");

    private static StyleReferenceResult Resolved(
        XElement selector, string? reference, bool recovered)
    {
        // Inline Styles on Placemarks or inside StyleMap Pairs must not be
        // cloned by inserting a sibling Style at that location. Shared style
        // declarations in Documents and Folders can safely be cloned there.
        if (selector.Parent?.Name != KmlNs + "Document" &&
            selector.Parent?.Name != KmlNs + "Folder")
        {
            return new(StyleReferenceStatus.Ambiguous, null, reference,
                "Style selector is embedded inside a feature or Pair and cannot safely be cloned.");
        }

        return new(selector.Name == KmlNs + "Style"
                ? StyleReferenceStatus.Style
                : StyleReferenceStatus.StyleMap,
            selector,
            reference,
            recovered
                ? "Duplicate ID resolved uniquely within the enclosing Document."
                : null,
            recovered);
    }
}

public enum StyleReferenceStatus
{
    None,
    Style,
    StyleMap,
    Missing,
    External,
    Ambiguous,
    Cyclic
}

public sealed record StyleReferenceResult(
    StyleReferenceStatus Status,
    XElement? Selector = null,
    string? Reference = null,
    string? Message = null,
    bool RecoveredDuplicate = false)
{
    public bool IsResolved =>
        Status is StyleReferenceStatus.Style or StyleReferenceStatus.StyleMap;

    public bool MustSkip =>
        Status is StyleReferenceStatus.External or
            StyleReferenceStatus.Ambiguous or
            StyleReferenceStatus.Cyclic or
            StyleReferenceStatus.Missing;
}
