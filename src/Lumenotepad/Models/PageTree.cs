using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lumenotepad.Models;

public static class PageTree
{
    public const int MaxLevel = 2;

    public static int SubtreeEnd(IList<Page> pages, int index)
    {
        int level = pages[index].Level;
        int i = index + 1;
        while (i < pages.Count && pages[i].Level > level) i++;
        return i;
    }

    public static bool CanIndent(IList<Page> pages, int index)
    {
        if (index <= 0 || index >= pages.Count) return false;
        if (pages[index - 1].Level < pages[index].Level) return false;
        int end = SubtreeEnd(pages, index);
        for (int i = index; i < end; i++) if (pages[i].Level >= MaxLevel) return false;
        return true;
    }

    public static bool CanOutdent(IList<Page> pages, int index) =>
        index >= 0 && index < pages.Count && pages[index].Level > 0;

    public static bool CanAddSubPage(IList<Page> pages, int index) =>
        index >= 0 && index < pages.Count && pages[index].Level < MaxLevel;

    public static void Indent(IList<Page> pages, int index)
    {
        if (CanIndent(pages, index)) Shift(pages, index, +1);
    }

    public static void Outdent(IList<Page> pages, int index)
    {
        if (CanOutdent(pages, index)) Shift(pages, index, -1);
    }

    private static void Shift(IList<Page> pages, int index, int delta)
    {
        int end = SubtreeEnd(pages, index);
        for (int i = index; i < end; i++) pages[i].Level += delta;
        Refresh(pages);
    }

    public static int InsertSubPage(IList<Page> pages, int parentIndex, Page page)
    {
        page.Level = Math.Min(MaxLevel, pages[parentIndex].Level + 1);
        pages[parentIndex].Collapsed = false;
        int at = SubtreeEnd(pages, parentIndex);
        pages.Insert(at, page);
        Refresh(pages);
        return at;
    }

    public static int InsertAfter(IList<Page> pages, int index, Page page)
    {
        page.Level = pages[index].Level;
        int at = SubtreeEnd(pages, index);
        pages.Insert(at, page);
        Refresh(pages);
        return at;
    }

    public static void RemovePromoting(IList<Page> pages, int index)
    {
        int end = SubtreeEnd(pages, index);
        for (int i = index + 1; i < end; i++) pages[i].Level--;
        pages.RemoveAt(index);
        Refresh(pages);
    }

    public static List<(int Start, int End)> Groups(IList<Page> pages)
    {
        var groups = new List<(int, int)>();
        int i = 0;
        while (i < pages.Count)
        {
            int end = i + 1;
            while (end < pages.Count && pages[end].Level > 0) end++;
            groups.Add((i, end));
            i = end;
        }
        return groups;
    }

    public static void MoveGroup(ObservableCollection<Page> pages, int from, int to)
    {
        var groups = Groups(pages);
        if (from == to || from < 0 || to < 0 || from >= groups.Count || to >= groups.Count) return;
        var (s, e) = groups[from];
        int k = e - s;
        int dest = to < from ? groups[to].Start : groups[to].End - k;
        if (dest < s)
            for (int i = 0; i < k; i++) pages.Move(s + i, dest + i);
        else
            for (int i = 0; i < k; i++) pages.Move(s, dest + k - 1);
        Refresh(pages);
    }

    public static void Refresh(IList<Page> pages)
    {
        int? foldLevel = null;
        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            if (foldLevel is { } fl && p.Level <= fl) foldLevel = null;
            p.IsFoldedAway = foldLevel is not null;
            p.HasSubPages = i + 1 < pages.Count && pages[i + 1].Level > p.Level;
            if (foldLevel is null && p.Collapsed && p.HasSubPages) foldLevel = p.Level;
            int mask = 0;
            bool last = true;
            for (int k = 1; k <= p.Level; k++)
            {
                bool more = false;
                for (int j = i + 1; j < pages.Count; j++)
                {
                    if (pages[j].Level < k) break;
                    if (pages[j].Level == k) { more = true; break; }
                }
                if (k == p.Level) last = !more;
                else if (more) mask |= 1 << k;
            }
            p.IsLastSibling = last;
            p.GuideMask = mask;
        }
    }

    public static void Normalize(IList<Page> pages)
    {
        int prev = -1;
        foreach (var p in pages)
        {
            int max = Math.Min(MaxLevel, prev + 1);
            p.Level = Math.Clamp(p.Level, 0, max);
            prev = p.Level;
        }
        Refresh(pages);
    }

    public static Page VisibleAncestor(IList<Page> pages, Page page)
    {
        int i = pages.IndexOf(page);
        if (i < 0) return page;
        var cur = page;
        for (int j = i - 1; j >= 0 && cur.IsFoldedAway; j--)
            if (pages[j].Level < cur.Level) cur = pages[j];
        return cur;
    }
}
