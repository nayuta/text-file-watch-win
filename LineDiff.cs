using System;
using System.Collections.Generic;

namespace TextFileWatch;

internal static class LineDiff
{
    public static bool[] ComputeChangedNewLines(string[] oldLines, string[] newLines)
    {
        if (newLines.Length == 0)
            return Array.Empty<bool>();

        var maxWork = oldLines.Length + newLines.Length;
        if (maxWork <= 0)
            return new bool[newLines.Length];

        if (maxWork > 40_000)
            return ComputeChangedByStableEnds(oldLines, newLines);

        return ComputeChangedMyers(oldLines, newLines);
    }

    private static bool[] ComputeChangedByStableEnds(string[] oldLines, string[] newLines)
    {
        var changed = new bool[newLines.Length];

        var prefix = 0;
        var maxPrefix = Math.Min(oldLines.Length, newLines.Length);
        while (prefix < maxPrefix && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
            prefix++;

        var suffix = 0;
        while (suffix < oldLines.Length - prefix &&
               suffix < newLines.Length - prefix &&
               string.Equals(oldLines[oldLines.Length - 1 - suffix], newLines[newLines.Length - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        for (var i = prefix; i < newLines.Length - suffix; i++)
            changed[i] = true;

        return changed;
    }

    private static bool[] ComputeChangedMyers(string[] oldLines, string[] newLines)
    {
        var n = oldLines.Length;
        var m = newLines.Length;
        var max = n + m;
        var offset = max;

        var v = new int[2 * max + 1];
        Array.Fill(v, -1);
        v[offset + 1] = 0;

        var trace = new List<int[]>(capacity: Math.Min(max + 1, 256));

        var completed = false;
        for (var d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (var k = -d; k <= d; k += 2)
            {
                var idx = offset + k;
                int x;
                if (k == -d || (k != d && v[idx - 1] < v[idx + 1]))
                    x = v[idx + 1];
                else
                    x = v[idx - 1] + 1;

                var y = x - k;
                while (x < n && y < m && string.Equals(oldLines[x], newLines[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[idx] = x;
                if (x >= n && y >= m)
                {
                    completed = true;
                    break;
                }
            }

            if (completed)
                break;
        }

        var changedNew = new bool[m];
        var x2 = n;
        var y2 = m;

        for (var d = trace.Count - 1; d > 0; d--)
        {
            var vPrev = trace[d];
            var k = x2 - y2;

            int prevK;
            if (k == -d || (k != d && vPrev[offset + k - 1] < vPrev[offset + k + 1]))
                prevK = k + 1;
            else
                prevK = k - 1;

            var prevX = vPrev[offset + prevK];
            var prevY = prevX - prevK;

            while (x2 > prevX && y2 > prevY)
            {
                x2--;
                y2--;
            }

            if (y2 == prevY)
            {
                x2--;
            }
            else
            {
                y2--;
                if (y2 >= 0 && y2 < m)
                    changedNew[y2] = true;
            }
        }

        return changedNew;
    }
}

