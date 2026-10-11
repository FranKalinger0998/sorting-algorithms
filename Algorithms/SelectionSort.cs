namespace SortingAlgorithms.Algorithms;

public sealed class SelectionSort : SortingAlgorithmBase
{
    public override string Name => "Selection Sort";

    public override string TimeComplexity => "O(n²)";

    public override string SpaceComplexity => "O(1)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        for (var index = 0; index < array.Length - 1; index++)
        {
            var minimum = index;
            for (var candidate = index + 1; candidate < array.Length; candidate++)
            {
                if (metrics.CompareAt(array, candidate, minimum) < 0)
                {
                    minimum = candidate;
                }
            }

            metrics.Swap(array, index, minimum);
        }
    }
}
