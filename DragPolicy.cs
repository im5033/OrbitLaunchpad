using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Orbit;

internal enum DragAction { None, Swap, Group }
internal readonly record struct DragTarget(string Id, Point Center, double Size);
internal readonly record struct DragHit(string? Id, DragAction Action);

internal static class DragPolicy
{
    public const double StartDistance = 14;
    public static Point IconCenter(Point pointer, Point grabOffset, double tileWidth, double iconSize) =>
        new(pointer.X - grabOffset.X + tileWidth / 2, pointer.Y - grabOffset.Y + iconSize / 2);

    public static DragHit Hit(Point center, IEnumerable<DragTarget> candidates, bool canGroup, string? stickyGroup)
    {
        var targets = candidates.ToList();
        if (canGroup && stickyGroup != null)
        {
            var sticky = targets.Find(x => x.Id == stickyGroup);
            if (sticky.Id != null && (sticky.Center - center).Length <= sticky.Size * .85)
                return new DragHit(sticky.Id, DragAction.Group);
        }
        var nearest = targets.OrderBy(x => (x.Center - center).Length).FirstOrDefault();
        if (nearest.Id == null) return default;
        double distance = (nearest.Center - center).Length;
        if (canGroup && distance <= nearest.Size * .62) return new DragHit(nearest.Id, DragAction.Group);
        if (distance <= nearest.Size * 1.12) return new DragHit(nearest.Id, DragAction.Swap);
        return default;
    }
}

internal sealed class MenuDismissGesture
{
    private bool _consumeRelease;
    public void DismissedWithLeftButton() => _consumeRelease = true;
    public void MenuOpened() => _consumeRelease = false;
    public bool AwaitingRelease => _consumeRelease;
    public bool ConsumeRelease()
    {
        bool consume = _consumeRelease; _consumeRelease = false; return consume;
    }
}
