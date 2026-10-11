namespace SortingAlgorithms.Algorithms;

public sealed class ShellSort : SortingAlgorithmBase
{
    private static readonly int[] Gaps = [701, 301, 132, 57, 23, 10, 4, 1];

    public override string Name => "Shell Sort";

    public override string TimeComplexity => "Gap-dependent (worst O(n²))";

    public override string SpaceComplexity => "O(1)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        foreach (var gap in Gaps)
        {
            if (gap >= array.Length)
            {
                continue;
            }

            for (var index = gap; index < array.Length; index++)
            {
                var value = array[index];
                var target = index;

                while (target >= gap && metrics.Compare(array[target - gap], value) > 0)
                {
                    metrics.Write(array, target, array[target - gap]);
                    target -= gap;
                }

                if (target != index)
                {
                    metrics.Write(array, target, value);
                }
            }
        }
    }
}
