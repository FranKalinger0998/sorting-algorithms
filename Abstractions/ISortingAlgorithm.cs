using SortingAlgorithms.Models;

namespace SortingAlgorithms.Abstractions;

public interface ISortingAlgorithm
{
    string Name { get; }

    string TimeComplexity { get; }

    string SpaceComplexity { get; }

    SortStatistics Sort(
        int[] array,
        Action<SortStep>? stepRecorder = null,
        CancellationToken cancellationToken = default,
        int stepSamplingInterval = 1,
        Action<SortAction>? actionRecorder = null);
}
