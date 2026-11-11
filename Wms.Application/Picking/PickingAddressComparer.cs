namespace Wms.Application.Picking;

public readonly record struct PickingAddress(Guid LocationId, string Code);

public sealed class PickingAddressComparer : IComparer<PickingAddress>
{
    public static PickingAddressComparer Instance { get; } = new();

    public int Compare(PickingAddress left, PickingAddress right)
    {
        var leftStandard = IsStandard(left.Code);
        var rightStandard = IsStandard(right.Code);
        if (leftStandard != rightStandard)
            return leftStandard ? -1 : 1;
        if (leftStandard)
        {
            foreach (var part in new[] { 0, 1, 2, 3, 5, 4 })
            {
                var comparison = left.Code[part].CompareTo(right.Code[part]);
                if (comparison != 0) return comparison;
            }
        }
        var codeComparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
        return codeComparison != 0 ? codeComparison : left.LocationId.CompareTo(right.LocationId);
    }

    // A missing neighbor contributes no leg; the first and last point need no
    // invented warehouse origin or destination.
    public static int AdditionalTravel(int candidateRank,
        int? previousRank, int? nextRank)
    {
        var via = (previousRank is int previous ? Math.Abs(previous - candidateRank) : 0)
            + (nextRank is int next ? Math.Abs(candidateRank - next) : 0);
        var direct = previousRank is int from && nextRank is int to
            ? Math.Abs(from - to) : 0;
        return via - direct;
    }

    public static bool IsStandard(string? code) => code is { Length: 6 }
        && char.IsAsciiDigit(code[0]) && char.IsAsciiLetterUpper(code[1])
        && char.IsAsciiDigit(code[2]) && char.IsAsciiDigit(code[3])
        && char.IsAsciiLetterUpper(code[4]) && char.IsAsciiDigit(code[5]);
}
