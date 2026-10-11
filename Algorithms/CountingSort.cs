using System.Collections.Generic;

namespace SortingAlgorithms.Algorithms;

public sealed class CountingSort : SortingAlgorithmBase
{
    private const int MaximumDenseRange = 1_000_000;

    public override string Name => "Counting Sort";

    public override string TimeComplexity => "O(n + range)";

    public override string SpaceComplexity => "O(range)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        if (array.Length < 2)
        {
            return;
        }

        var minimum = array[0];
        var maximum = array[0];
        for (var index = 1; index < array.Length; index++)
        {
            var value = array[index];
            if (metrics.Compare(value, minimum) < 0)
            {
                minimum = value;
            }

            if (metrics.Compare(value, maximum) > 0)
            {
                maximum = value;
            }
        }

        var range = (long)maximum - minimum + 1;
        if (range <= MaximumDenseRange)
        {
            SortDenseRange(array, minimum, (int)range, metrics);
            return;
        }

        // A dense count array could be enormous for values such as Int32.MinValue
        // and Int32.MaxValue. Count distinct values sparsely, then sort only keys.
        SortSparseRange(array, metrics);
    }

    private static void SortDenseRange(int[] array, int minimum, int range, SortMetrics metrics)
    {
        var counts = new int[range];
        foreach (var value in array)
        {
            var slot = (int)((long)value - minimum);
            counts[slot]++;
            metrics.RecordWrite();
        }

        var target = 0;
        for (var slot = 0; slot < counts.Length; slot++)
        {
            var remaining = counts[slot];
            while (remaining > 0)
            {
                metrics.Write(array, target++, (int)((long)slot + minimum));
                remaining--;
            }
        }
    }

    private static void SortSparseRange(int[] array, SortMetrics metrics)
    {
        var counts = new Dictionary<int, int>();
        foreach (var value in array)
        {
            counts.TryGetValue(value, out var currentCount);
            counts[value] = currentCount + 1;
        }

        var keys = counts.Keys.ToArray();
        Array.Sort(keys, Comparer<int>.Create(metrics.Compare));

        var target = 0;
        foreach (var key in keys)
        {
            for (var occurrence = 0; occurrence < counts[key]; occurrence++)
            {
                metrics.Write(array, target++, key);
            }
        }
    }
}
