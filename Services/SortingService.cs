using System.Diagnostics;
using SortingAlgorithms.Abstractions;
using SortingAlgorithms.Models;

namespace SortingAlgorithms.Services;

public sealed class SortingService
{
    private readonly IReadOnlyList<ISortingAlgorithm> _algorithms;
    private readonly IReadOnlyDictionary<string, ISortingAlgorithm> _algorithmsByName;

    public SortingService(IEnumerable<ISortingAlgorithm> algorithms)
    {
        ArgumentNullException.ThrowIfNull(algorithms);

        var registeredAlgorithms = algorithms.ToArray();
        if (registeredAlgorithms.Length == 0)
        {
            throw new ArgumentException("At least one sorting algorithm must be registered.", nameof(algorithms));
        }

        _algorithmsByName = registeredAlgorithms.ToDictionary(
            algorithm => algorithm.Name,
            StringComparer.OrdinalIgnoreCase);
        _algorithms = registeredAlgorithms;
    }

    public IReadOnlyList<ISortingAlgorithm> GetAlgorithms() => _algorithms;

    public SortResult Sort(
        int[] array,
        string algorithmName,
        Action<SortStep>? stepRecorder = null,
        CancellationToken cancellationToken = default,
        int stepSamplingInterval = 1,
        Action<SortAction>? actionRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmName);
        ArgumentOutOfRangeException.ThrowIfLessThan(stepSamplingInterval, 1);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_algorithmsByName.TryGetValue(algorithmName, out var algorithm))
        {
            throw new ArgumentException($"Unknown sorting algorithm: {algorithmName}", nameof(algorithmName));
        }

        var resultArray = (int[])array.Clone();
        var stopwatch = Stopwatch.StartNew();
        Action<SortStep>? timedStepRecorder = stepRecorder is null
            ? null
            : step =>
            {
                stopwatch.Stop();
                try
                {
                    stepRecorder(step);
                }
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        stopwatch.Start();
                    }
                }
            };
        Action<SortAction>? timedActionRecorder = actionRecorder is null
            ? null
            : action =>
            {
                stopwatch.Stop();
                try
                {
                    actionRecorder(action);
                }
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        stopwatch.Start();
                    }
                }
            };
        var statistics = timedStepRecorder is null && timedActionRecorder is null && !cancellationToken.CanBeCanceled
            ? algorithm.Sort(resultArray)
            : algorithm.Sort(resultArray, timedStepRecorder, cancellationToken, stepSamplingInterval, timedActionRecorder);
        stopwatch.Stop();

        return new SortResult(algorithm.Name, resultArray, statistics, stopwatch.Elapsed);
    }
}
