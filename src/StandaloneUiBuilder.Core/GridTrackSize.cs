using System.Globalization;

namespace StandaloneUiBuilder.Core;

/// <summary>
/// The size of one Grid row or column: a fixed number of DIPs ("100"), or a proportional share
/// of the space left after the fixed ones ("*", "2*"). Written the way WPF writes them.
/// </summary>
public readonly record struct GridTrackSize(double Value, bool IsProportional)
{
    public const int MaxFixed = 10000;
    public const int MaxWeight = 1000;

    public static GridTrackSize Share { get; } = new(1, true);

    public static bool TryParse(string? text, out GridTrackSize size)
    {
        size = default;
        var trimmed = (text ?? "").Trim();
        if (trimmed.EndsWith('*'))
        {
            var weightText = trimmed[..^1].Trim();
            var weight = 1.0;
            if (weightText.Length > 0 && !double.TryParse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture, out weight))
            {
                return false;
            }

            if (weight is <= 0 or > MaxWeight || double.IsNaN(weight))
            {
                return false;
            }

            size = new GridTrackSize(weight, true);
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fixedSize) && fixedSize is >= 1 and <= MaxFixed)
        {
            size = new GridTrackSize(fixedSize, false);
            return true;
        }

        return false;
    }

    /// <summary>"100", "*" or "2*": the same text WPF uses.</summary>
    public override string ToString() => IsProportional
        ? (Value == 1 ? "*" : Value.ToString("0.###", CultureInfo.InvariantCulture) + "*")
        : Value.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Where each boundary between rows (or columns) falls, from 0 to <paramref name="total"/>.
    /// Fixed tracks get their size; proportional tracks share what is left by weight. Edges
    /// are rounded from exact positions so tracks tile with no gaps. When the fixed tracks do
    /// not fit, proportional tracks get nothing and the overflow is clipped.
    /// </summary>
    public static int[] Edges(IReadOnlyList<GridTrackSize> sizes, int total)
    {
        var fixedTotal = sizes.Where(s => !s.IsProportional).Sum(s => s.Value);
        var weightTotal = sizes.Where(s => s.IsProportional).Sum(s => s.Value);
        var remaining = Math.Max(0, total - fixedTotal);

        var edges = new int[sizes.Count + 1];
        var position = 0.0;
        for (var i = 0; i < sizes.Count; i++)
        {
            var size = sizes[i];
            position += size.IsProportional ? (weightTotal > 0 ? remaining * size.Value / weightTotal : 0) : size.Value;
            edges[i + 1] = (int)Math.Round(position, MidpointRounding.AwayFromZero);
        }

        return edges;
    }

    /// <summary>
    /// The sizes of a Grid's rows or columns: the stored list when present and the right
    /// length, otherwise equal shares.
    /// </summary>
    public static IReadOnlyList<GridTrackSize> Resolve(IReadOnlyList<string>? stored, int count)
    {
        if (stored is { } list && list.Count == count)
        {
            var parsed = new List<GridTrackSize>(count);
            foreach (var text in list)
            {
                parsed.Add(TryParse(text, out var size) ? size : Share);
            }

            return parsed;
        }

        return Enumerable.Repeat(Share, count).ToList();
    }
}
