#nullable enable
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveModularBackpacks.Attachments;

internal static class AttachmentPointRouting
{
    public static int PointForSelectionBox(IReadOnlyList<IAttachmentPoint> points, int selectionBoxIndex,
        int bodyBoxCount)
    {
        int selectableIndex = selectionBoxIndex - bodyBoxCount;
        if (selectableIndex < 0) return -1;

        foreach (var (point, index) in Enumerate(points))
        {
            if (point.IsVirtual) continue;
            if (selectableIndex-- == 0) return index;
        }
        return -1;
    }

    public static int OccupiedPointAt(IReadOnlyList<IAttachmentPoint> points,
        IReadOnlyList<ItemStack?> occupants, int realPointIndex)
    {
        if (!IsReal(points, realPointIndex)) return -1;
        if (Occupant(occupants, realPointIndex) != null) return realPointIndex;

        string code = points[realPointIndex].Code;
        foreach (var (point, index) in Enumerate(points))
            if (point.IsVirtual && Occupant(occupants, index) != null && Contains(point.MemberCodes, code))
                return index;
        return -1;
    }

    public static int AttachTargetAt(IReadOnlyList<IAttachmentPoint> points,
        IReadOnlyList<ItemStack?> occupants, int realPointIndex, IAttachment? candidate)
    {
        if (candidate == null || !IsFree(points, occupants, realPointIndex)) return -1;
        if (points[realPointIndex].Accepts(candidate)) return realPointIndex;

        string code = points[realPointIndex].Code;
        foreach (var (point, index) in Enumerate(points))
            if (point.IsVirtual && Contains(point.MemberCodes, code)
                && IsAvailableVirtual(points, occupants, index) && point.Accepts(candidate))
                return index;
        return -1;
    }

    public static IReadOnlyList<IAttachmentPoint> AvailablePointsAt(IReadOnlyList<IAttachmentPoint> points,
        IReadOnlyList<ItemStack?> occupants, int realPointIndex)
    {
        var available = new List<IAttachmentPoint>();
        if (!IsFree(points, occupants, realPointIndex)) return available;

        available.Add(points[realPointIndex]);
        string code = points[realPointIndex].Code;
        foreach (var (point, index) in Enumerate(points))
            if (point.IsVirtual && Contains(point.MemberCodes, code)
                && IsAvailableVirtual(points, occupants, index))
                available.Add(point);
        return available;
    }

    private static bool IsAvailableVirtual(IReadOnlyList<IAttachmentPoint> points,
        IReadOnlyList<ItemStack?> occupants, int virtualPointIndex)
    {
        var point = points[virtualPointIndex];
        if (!point.IsVirtual || Occupant(occupants, virtualPointIndex) != null) return false;
        foreach (string memberCode in point.MemberCodes)
        {
            int memberIndex = IndexOf(points, memberCode);
            if (!IsFree(points, occupants, memberIndex)) return false;
        }
        return true;
    }

    private static bool IsFree(IReadOnlyList<IAttachmentPoint> points,
        IReadOnlyList<ItemStack?> occupants, int realPointIndex)
        => IsReal(points, realPointIndex) && OccupiedPointAt(points, occupants, realPointIndex) < 0;

    private static bool IsReal(IReadOnlyList<IAttachmentPoint> points, int index)
        => index >= 0 && index < points.Count && !points[index].IsVirtual;

    private static int IndexOf(IReadOnlyList<IAttachmentPoint> points, string code)
    {
        for (int i = 0; i < points.Count; i++)
            if (points[i].Code == code) return i;
        return -1;
    }

    private static ItemStack? Occupant(IReadOnlyList<ItemStack?> occupants, int index)
        => index >= 0 && index < occupants.Count ? occupants[index] : null;

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        foreach (string candidate in values)
            if (candidate == value) return true;
        return false;
    }

    private static IEnumerable<(IAttachmentPoint point, int index)> Enumerate(
        IReadOnlyList<IAttachmentPoint> points)
    {
        for (int i = 0; i < points.Count; i++) yield return (points[i], i);
    }
}
