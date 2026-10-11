namespace SortingAlgorithms.Models;

public sealed record SortResult(
    string AlgorithmName,
    int[] SortedArray,
    SortStatistics Statistics,
    TimeSpan Elapsed);
