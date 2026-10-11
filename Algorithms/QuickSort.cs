namespace SortingAlgorithms.Algorithms;

public sealed class QuickSort : SortingAlgorithmBase
{
    public override string Name => "Quick Sort";

    public override string TimeComplexity => "O(n log n), worst O(n²)";

    public override string SpaceComplexity => "O(log n) average";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        if (array.Length > 1)
        {
            SortRange(array, 0, array.Length - 1, metrics);
        }
    }

    private static void SortRange(int[] array, int low, int high, SortMetrics metrics)
    {
        while (low < high)
        {
            var pivot = array[low + ((high - low) / 2)];
            var lower = low;
            var current = low;
            var upper = high;

            while (current <= upper)
            {
                var comparison = metrics.Compare(array[current], pivot);
                if (comparison < 0)
                {
                    metrics.Swap(array, lower++, current++);
                }
                else if (comparison > 0)
                {
                    metrics.Swap(array, current, upper--);
                }
                else
                {
                    current++;
                }
            }

            // Recurse into the smaller partition and loop over the larger one to
            // keep stack usage bounded even for unfavourable input.
            if (lower - low < high - upper)
            {
                SortRange(array, low, lower - 1, metrics);
                low = upper + 1;
            }
            else
            {
                SortRange(array, upper + 1, high, metrics);
                high = lower - 1;
            }
        }
    }
}
