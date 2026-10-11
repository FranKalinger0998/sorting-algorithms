using System.IO.Compression;
using System.Text;
using SortingAlgorithms.Abstractions;
using SortingAlgorithms.Algorithms;
using SortingAlgorithms.Models;
using SortingAlgorithms.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ISortingAlgorithm, BubbleSort>();
builder.Services.AddSingleton<ISortingAlgorithm, SelectionSort>();
builder.Services.AddSingleton<ISortingAlgorithm, InsertionSort>();
builder.Services.AddSingleton<ISortingAlgorithm, MergeSort>();
builder.Services.AddSingleton<ISortingAlgorithm, QuickSort>();
builder.Services.AddSingleton<ISortingAlgorithm, HeapSort>();
builder.Services.AddSingleton<ISortingAlgorithm, ShellSort>();
builder.Services.AddSingleton<ISortingAlgorithm, CountingSort>();
builder.Services.AddSingleton<ISortingAlgorithm, RadixSort>();
builder.Services.AddSingleton<ISortingAlgorithm, CocktailShakerSort>();
builder.Services.AddSingleton<SortingService>();
builder.Services.AddSingleton<ArrayGenerator>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/algorithms", (SortingService sorting) =>
    sorting.GetAlgorithms().Select(algorithm => new
    {
        algorithm.Name,
        algorithm.TimeComplexity,
        algorithm.SpaceComplexity
    }));

app.MapPost("/api/sort", async (SortRequest request, SortingService sorting, CancellationToken cancellationToken) =>
{
    if (request.Values is null || request.Values.Length == 0)
    {
        return Results.BadRequest(new { error = "Enter at least one array value." });
    }

    if (string.IsNullOrWhiteSpace(request.AlgorithmName))
    {
        return Results.BadRequest(new { error = "Choose a sorting algorithm." });
    }

    var algorithm = sorting.GetAlgorithms().FirstOrDefault(candidate =>
        string.Equals(candidate.Name, request.AlgorithmName, StringComparison.OrdinalIgnoreCase));
    if (algorithm is null)
    {
        return Results.BadRequest(new { error = $"Unknown sorting algorithm: {request.AlgorithmName}" });
    }

    using var traceStream = new MemoryStream();
    long stepCount = 0;

    try
    {
        SortResult result;
        using (var compressor = new GZipStream(traceStream, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(compressor, Encoding.UTF8, leaveOpen: true))
        {
            result = await Task.Run(() => sorting.Sort(
                request.Values,
                algorithm.Name,
                cancellationToken: cancellationToken,
                actionRecorder: action =>
                {
                    WriteTraceAction(writer, action);
                    stepCount++;
                }), cancellationToken);
        }

        return Results.Ok(new
        {
            result.AlgorithmName,
            elapsedMs = result.Elapsed.TotalMilliseconds,
            statistics = result.Statistics,
            stepCount,
            trace = Convert.ToBase64String(traceStream.ToArray()),
            sortedArray = result.SortedArray
        });
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }
});

app.MapPost("/api/benchmark", async (BenchmarkRequest request, SortingService sorting, CancellationToken cancellationToken) =>
{
    if (request.Values is null || request.Values.Length == 0)
    {
        return Results.BadRequest(new { error = "Enter at least one array value." });
    }

    try
    {
        var rows = await Task.Run(() =>
        {
            var results = new List<object>();
            foreach (var algorithm in sorting.GetAlgorithms())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = sorting.Sort(request.Values, algorithm.Name, cancellationToken: cancellationToken);
                results.Add(new
                {
                    result.AlgorithmName,
                    elapsedMs = result.Elapsed.TotalMilliseconds,
                    statistics = result.Statistics,
                    algorithm.TimeComplexity,
                    algorithm.SpaceComplexity
                });
            }

            return results;
        }, cancellationToken);

        return Results.Ok(new { count = request.Values.Length, results = rows });
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }
});

app.MapFallbackToFile("index.html");
app.Run();

static void WriteTraceAction(BinaryWriter writer, SortAction action)
{
    writer.Write((byte)action.Kind);
    writer.Write((byte)0);
    writer.Write((short)0);
    writer.Write(action.FirstIndex);
    writer.Write(action.SecondIndex);
    writer.Write(action.PreviousValue);
    writer.Write(action.Value);
}

sealed record SortRequest(string AlgorithmName, int[] Values);
sealed record BenchmarkRequest(int[] Values);
