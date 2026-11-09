using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Scheduling;

/// <summary>
/// MaxInRow: at most <c>Count</c> items of the "to" category directly after each other (items of another type, such as
/// a jingle between two songs, do not break the row).
/// NotAfter: an item of the "to" category may not directly follow an item of the "from" category (empty = any).
/// </summary>
public enum RuleKind { MaxInRow = 0, NotAfter = 1 }

/// <summary>Suppress = leave this clock position out; Defer = try it again after the next item.</summary>
public enum RuleAction { Suppress = 0, Defer = 1 }

/// <summary>A planning rule that looks at what was planned just before an item.</summary>
public sealed record ScriptRule(long Id, string Name, bool Enabled, RuleKind Kind, long? FromCategoryId, long? ToCategoryId,
    int Count, RuleAction Action)
{
    /// <summary>
    /// True when <paramref name="candidate"/> may not follow <paramref name="before"/> (oldest first, the last entry is
    /// the item directly before the candidate).
    /// </summary>
    public bool Blocks(LogItem candidate, IReadOnlyList<LogItem> before)
    {
        if (!Enabled) return false;
        long? cat = candidate.Media?.CategoryId;
        if (ToCategoryId is long to && cat != to) return false;

        switch (Kind)
        {
            case RuleKind.NotAfter:
                if (before.Count == 0) return false;
                var prev = before[^1];
                return FromCategoryId is not long from || prev.Media?.CategoryId == from;

            case RuleKind.MaxInRow:
                int row = 0;
                for (int i = before.Count - 1; i >= 0; i--)
                {
                    var b = before[i];
                    if (b.Type != candidate.Type) continue;      // a jingle between two songs does not break the row
                    if (b.Media?.CategoryId != cat) break;
                    row++;
                }
                return row >= Math.Max(1, Count);

            default:
                return false;
        }
    }

    public string Describe(Func<long?, string> category) => Kind switch
    {
        RuleKind.MaxInRow => $"Maximaal {Math.Max(1, Count)}× {category(ToCategoryId)} op rij",
        _ => $"{category(ToCategoryId)} niet direct na {category(FromCategoryId)}",
    } + (Action == RuleAction.Defer ? " (uitstellen)" : " (overslaan)");

    /// <summary>The first rule that blocks the candidate, or null.</summary>
    public static ScriptRule? FirstBlocking(IReadOnlyList<ScriptRule> rules, LogItem candidate, IReadOnlyList<LogItem> before) =>
        rules.FirstOrDefault(r => r.Blocks(candidate, before));
}
