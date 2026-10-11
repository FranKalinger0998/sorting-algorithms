namespace SortingAlgorithms.Algorithms;

public sealed class HeapSort : SortingAlgorithmBase
{
    public override string Name => "Heap Sort";

    public override string TimeComplexity => "O(n log n)";

    public override string SpaceComplexity => "O(1)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        for (var root = (array.Length / 2) - 1; root >= 0; root--)
        {
            SiftDown(array, root, array.Length, metrics);
        }

        for (var end = array.Length - 1; end > 0; end--)
        {
            metrics.Swap(array, 0, end);
            SiftDown(array, 0, end, metrics);
        }
    }

    private static void SiftDown(int[] array, int root, int length, SortMetrics metrics)
    {
        while (true)
        {
            var largest = root;
            var left = (2 * root) + 1;
            var right = left + 1;

            if (left < length && metrics.CompareAt(array, left, largest) > 0)
            {
                largest = left;
            }

            if (right < length && metrics.CompareAt(array, right, largest) > 0)
            {
                largest = right;
            }

            if (largest == root)
            {
                return;
            }

            metrics.Swap(array, root, largest);
            root = largest;
        }
    }
}
