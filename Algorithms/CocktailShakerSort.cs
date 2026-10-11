namespace SortingAlgorithms.Algorithms;

public sealed class CocktailShakerSort : SortingAlgorithmBase
{
    public override string Name => "Cocktail Shaker Sort";

    public override string TimeComplexity => "O(n²)";

    public override string SpaceComplexity => "O(1)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        var start = 0;
        var end = array.Length - 1;
        var swapped = true;

        while (swapped)
        {
            swapped = false;
            for (var index = start; index < end; index++)
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

            swapped = false;
            end--;

            for (var index = end; index > start; index--)
            {
                if (metrics.CompareAt(array, index - 1, index) <= 0)
                {
                    continue;
                }

                metrics.Swap(array, index - 1, index);
                swapped = true;
            }

            start++;
        }
    }
}
