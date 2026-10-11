using SortingAlgorithms.Abstractions;
using SortingAlgorithms.Models;

namespace SortingAlgorithms.Algorithms;

public abstract class SortingAlgorithmBase : ISortingAlgorithm
{
    public abstract string Name { get; }

    public abstract string TimeComplexity { get; }

    public abstract string SpaceComplexity { get; }

    public SortStatistics Sort(
        int[] array,
        Action<SortStep>? stepRecorder = null,
        CancellationToken cancellationToken = default,
        int stepSamplingInterval = 1,
        Action<SortAction>? actionRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfLessThan(stepSamplingInterval, 1);

        var metrics = new SortMetrics(array, stepRecorder, cancellationToken, stepSamplingInterval, actionRecorder);
        SortCore(array, metrics);
        return new SortStatistics(metrics.Comparisons, metrics.Swaps, metrics.Writes);
    }

    protected abstract void SortCore(int[] array, SortMetrics metrics);
}

public sealed class SortMetrics
{
    private readonly int[] _observedArray;
    private readonly Action<SortStep>? _stepRecorder;
    private readonly Action<SortAction>? _actionRecorder;
    private readonly CancellationToken _cancellationToken;
    private readonly int _stepSamplingInterval;
    private long _recordedOperations;

    public SortMetrics(
        int[] observedArray,
        Action<SortStep>? stepRecorder,
        CancellationToken cancellationToken = default,
        int stepSamplingInterval = 1,
        Action<SortAction>? actionRecorder = null)
    {
        _observedArray = observedArray;
        _stepRecorder = stepRecorder;
        _actionRecorder = actionRecorder;
        _cancellationToken = cancellationToken;
        _stepSamplingInterval = stepSamplingInterval;
    }

    public long Comparisons { get; private set; }

    public long Swaps { get; private set; }

    public long Writes { get; private set; }

    public int Compare(int left, int right)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        Comparisons++;
        var result = left.CompareTo(right);
        RecordOperation($"Compare {left} with {right}.", -1, -1, 0,
            new SortAction(SortActionKind.Compare, -1, -1, left, right));
        return result;
    }

    public int CompareAt(int[] array, int leftIndex, int rightIndex)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        Comparisons++;
        var left = array[leftIndex];
        var right = array[rightIndex];
        var result = left.CompareTo(right);
        RecordOperation($"Compare {left} at index {leftIndex} with {right} at index {rightIndex}.",
            leftIndex, rightIndex, 2,
            new SortAction(SortActionKind.Compare, leftIndex, rightIndex, left, right));
        return result;
    }

    public void Swap(int[] array, int leftIndex, int rightIndex)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (leftIndex == rightIndex)
        {
            return;
        }

        var left = array[leftIndex];
        var right = array[rightIndex];
        (array[leftIndex], array[rightIndex]) = (right, left);
        Swaps++;
        Writes += 2;
        RecordOperation($"Swap the values at indices {leftIndex} and {rightIndex}.",
            leftIndex, rightIndex, 2,
            new SortAction(SortActionKind.Swap, leftIndex, rightIndex, left, right));
    }

    public void Write(int[] array, int index, int value)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var previousValue = array[index];
        array[index] = value;
        Writes++;
        var isObservedArray = ReferenceEquals(array, _observedArray);
        var destination = isObservedArray ? $"index {index}" : "an auxiliary array";
        RecordOperation($"Write {value} to {destination}.",
            isObservedArray ? index : -1, -1, isObservedArray ? 1 : 0,
            new SortAction(
                isObservedArray ? SortActionKind.Write : SortActionKind.AuxiliaryWrite,
                isObservedArray ? index : -1,
                -1,
                previousValue,
                value));
    }

    public void RecordWrite()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        Writes++;
        RecordOperation("Record an auxiliary write.", -1, -1, 0,
            new SortAction(SortActionKind.AuxiliaryWrite, -1, -1, 0, 1));
    }

    public void RecordWrites(long count)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        Writes += count;
        if (count > 0)
        {
            var recordedCount = (int)Math.Min(count, int.MaxValue);
            RecordOperation($"Record {count} auxiliary writes.", -1, -1, 0,
                new SortAction(SortActionKind.BatchWrite, -1, -1, 0, recordedCount));
        }
    }

    private void RecordOperation(
        string description,
        int firstHighlight,
        int secondHighlight,
        int highlightCount,
        SortAction action)
    {
        if (_stepRecorder is null && _actionRecorder is null)
        {
            return;
        }

        if (++_recordedOperations % _stepSamplingInterval != 0)
        {
            return;
        }

        if (_stepRecorder is not null)
        {
            var highlights = highlightCount switch
            {
                0 => [],
                1 => new[] { firstHighlight },
                _ => new[] { firstHighlight, secondHighlight }
            };
            _stepRecorder(new SortStep((int[])_observedArray.Clone(), highlights, description));
        }

        _actionRecorder?.Invoke(action);
    }
}
