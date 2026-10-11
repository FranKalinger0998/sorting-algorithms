namespace SortingAlgorithms.Algorithms;

public sealed class MergeSort : SortingAlgorithmBase
{
    public override string Name => "Merge Sort";

    public override string TimeComplexity => "O(n log n)";

    public override string SpaceComplexity => "O(n)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        if (array.Length < 2)
        {
            return;
        }

        var buffer = new int[array.Length];
        SortRange(array, buffer, 0, array.Length - 1, metrics);
    }

    private static void SortRange(int[] array, int[] buffer, int start, int end, SortMetrics metrics)
    {
        if (start >= end)
        {
            return;
        }

        var middle = start + ((end - start) / 2);
        SortRange(array, buffer, start, middle, metrics);
        SortRange(array, buffer, middle + 1, end, metrics);

        var left = start;
        var right = middle + 1;
        var target = start;

        while (left <= middle && right <= end)
        {
            var value = metrics.CompareAt(array, left, right) <= 0
                ? array[left++]
                : array[right++];
            metrics.Write(buffer, target++, value);
        }

        while (left <= middle)
        {
            metrics.Write(buffer, target++, array[left++]);
        }

        while (right <= end)
        {
            metrics.Write(buffer, target++, array[right++]);
        }

        for (var index = start; index <= end; index++)
        {
            metrics.Write(array, index, buffer[index]);
        }
    }
}
