using System.Xml.Linq;

namespace KmlScopedEditor.Services;

/// <summary>
/// Remembers crossing-line repair offers within one geometry revision and
/// applied-guide sequence. Previously declined, ineffective, or still
/// unresolved repairs are not proposed repeatedly on every map click.
/// </summary>
public sealed class SideRepairPromptTracker
{
    private readonly HashSet<XElement> _offered = new();
    private long _revision = long.MinValue;
    private int _sequence = int.MinValue;

    public IReadOnlyList<XElement> TakeNewCrossings(
        long geometryRevision,
        int appliedSequence,
        IEnumerable<XElement> crossingLines)
    {
        MatchRevision(geometryRevision, appliedSequence);
        var newCrossings = new List<XElement>();
        foreach (var line in crossingLines)
        {
            if (_offered.Add(line))
                newCrossings.Add(line);
        }
        return newCrossings;
    }

    public void RememberUnresolved(
        long geometryRevision,
        int appliedSequence,
        IEnumerable<XElement> crossingLines)
    {
        MatchRevision(geometryRevision, appliedSequence);
        _offered.UnionWith(crossingLines);
    }

    public void Clear()
    {
        _revision = long.MinValue;
        _sequence = int.MinValue;
        _offered.Clear();
    }

    private void MatchRevision(long geometryRevision, int appliedSequence)
    {
        if (_revision == geometryRevision && _sequence == appliedSequence)
            return;

        _offered.Clear();
        _revision = geometryRevision;
        _sequence = appliedSequence;
    }
}
