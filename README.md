# Sort Lab — Sorting Algorithms Visualizer

An interactive sorting algorithms visualizer and benchmark built with ASP.NET Core. Watch sorting algorithms rearrange an array step by step, control playback, and compare operation counts.

**Live demo:** [sorting-algorithms-w8hg.onrender.com](https://sorting-algorithms-w8hg.onrender.com/)

## Features

- Visualize Bubble, Selection, Insertion, Merge, Quick, Heap, Shell, Counting, Radix, and Cocktail Shaker sort.
- Choose a random, sorted, reverse-ordered, or custom array and set its length.
- Play, pause, restart, step through, or seek across the recorded operations.
- Adjust playback speed and optionally hear notes whose pitch follows the values.
- Compare algorithm benchmarks by time, comparisons, swaps, and array writes.

## Hosting and performance

The live demo runs on Render's free tier. After inactivity, the service may need to wake up; the first request can take up to about a minute. Sorting time also varies with the selected algorithm and array length. Quadratic algorithms can take much longer on large arrays.

## Run locally

Requires the .NET 8 SDK.

```powershell
dotnet run --project SortingAlgorithms.csproj
```

Open the local URL printed by ASP.NET Core in your browser.

## Built with

- C# and ASP.NET Core on .NET 8
- HTML, CSS, and JavaScript
- Docker for deployment
