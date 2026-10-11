namespace SortingAlgorithms.Algorithms;

public sealed class InsertionSort : SortingAlgorithmBase
{
    public override string Name => "Insertion Sort";

    public override string TimeComplexity => "O(n²), best O(n)";

    public override string SpaceComplexity => "O(1)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        for (var index = 1; index < array.Length; index++)
        {
            var value = array[index];
            var insertionIndex = index;

            while (insertionIndex > 0 && metrics.Compare(array[insertionIndex - 1], value) > 0)
            {
                metrics.Write(array, insertionIndex, array[insertionIndex - 1]);
                insertionIndex--;
            }

            if (insertionIndex != index)
            {
                metrics.Write(array, insertionIndex, value);
            }
        }
    }
}
