namespace SortingAlgorithms.Algorithms;

public sealed class BubbleSort : SortingAlgorithmBase
{
    public override string Name => "Bubble Sort";

    public override string TimeComplexity => "O(n²)";

    public override string SpaceComplexity => "O(1)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        for (var end = array.Length - 1; end > 0; end--)
        {
            var swapped = false;
            for (var index = 0; index < end; index++)
            {
                if (metrics.CompareAt(array, index, index + 1) <= 0)
                {
                    continue;
                }

                metrics.Swap(array, index, index + 1);
                swapped = true;
            }

            if (!swapped)
            {
                break;
            }
        }
    }
}
