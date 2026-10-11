namespace SortingAlgorithms.Algorithms;

public sealed class RadixSort : SortingAlgorithmBase
{
    private const int Base = 256;
    private const int PassCount = sizeof(int);

    public override string Name => "Radix Sort";

    public override string TimeComplexity => "O(d(n + b))";

    public override string SpaceComplexity => "O(n + b)";

    protected override void SortCore(int[] array, SortMetrics metrics)
    {
        if (array.Length < 2)
        {
            return;
        }

        var buffer = new int[array.Length];
        var source = array;
        var target = buffer;

        for (var pass = 0; pass < PassCount; pass++)
        {
            var counts = new int[Base];
            var shift = pass * 8;

            foreach (var value in source)
            {
                var key = unchecked((uint)(value ^ int.MinValue));
                var bucket = (int)((key >> shift) & 0xFF);
                counts[bucket]++;
                metrics.RecordWrite();
            }

            var offset = 0;
            for (var bucket = 0; bucket < counts.Length; bucket++)
            {
                var frequency = counts[bucket];
                counts[bucket] = offset;
                offset += frequency;
                metrics.RecordWrite();
            }

            foreach (var value in source)
            {
                var key = unchecked((uint)(value ^ int.MinValue));
                var bucket = (int)((key >> shift) & 0xFF);
                metrics.Write(target, counts[bucket]++, value);
                metrics.RecordWrite();
            }

            (source, target) = (target, source);
        }

        if (!ReferenceEquals(source, array))
        {
            Array.Copy(source, array, array.Length);
            metrics.RecordWrites(array.Length);
        }
    }
}
