using System.Collections.Concurrent;
using System.Text;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using SortingAlgorithms.Models;
using SortingAlgorithms.Services;
using SortingAlgorithms.Visualization;

namespace SortingAlgorithms;

public sealed class SortingApplicationWindow : GameWindow
{
    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec3 aColor;
        out vec3 vertexColor;
        void main() { gl_Position = vec4(aPosition, 0.0, 1.0); vertexColor = aColor; }
        """;
    private const string FragmentShaderSource = """
        #version 330 core
        in vec3 vertexColor;
        out vec4 fragmentColor;
        void main() { fragmentColor = vec4(vertexColor, 1.0); }
        """;

    private readonly SortingService _sortingService;
    private readonly ArrayGenerator _arrayGenerator;
    private readonly IReadOnlyList<Abstractions.ISortingAlgorithm> _algorithms;
    private readonly ConcurrentQueue<SortStep> _steps = new();
    private readonly List<SortStep> _timelineFrames = [];
    private readonly List<HitTarget> _hitTargets = [];
    private readonly List<SortResult> _benchmarkResults = [];
    private int[] _inputArray;
    private int[] _displayArray;
    private HashSet<int> _highlightedIndices = new();
    private int _selectedAlgorithm;
    private int _arrayLength = 2_000;
    private int _shaderProgram;
    private int _vertexArray;
    private int _vertexBuffer;
    private int _stepsPerSecond = 10;
    private double _stepPlaybackAccumulator;
    private string _stepRateText = "10";
    private bool _stepRateFocused;
    private int _currentFrameIndex;
    private bool _isSeekingTimeline;
    private long _runGeneration;
    private bool _isRunning;
    private bool _isPaused;
    private bool _isComplete;
    private bool _previousLeftMouseDown;
    private bool _customFieldFocused;
    private bool _lengthFieldFocused;
    private string _customText = "12, 4, 9, 1, 7, 3";
    private string _lengthText = "2000";
    private string _statusMessage = "Choose a mode and press Start.";
    private string _stepDescription = string.Empty;
    private SortMode _mode = SortMode.SortVisualizer;
    private DataMode _dataMode = DataMode.Random;
    private SortResult? _sortResult;
    private Task<SortResult>? _sortTask;
    private Task<IReadOnlyList<SortResult>>? _benchmarkTask;
    private CancellationTokenSource? _runCancellation;

    public SortingApplicationWindow(SortingService sortingService, ArrayGenerator arrayGenerator)
        : base(GameWindowSettings.Default, CreateWindowSettings())
    {
        _sortingService = sortingService;
        _arrayGenerator = arrayGenerator;
        _algorithms = sortingService.GetAlgorithms();
        var quickSort = _algorithms.ToList().FindIndex(item => item.Name == "Quick Sort");
        _selectedAlgorithm = Math.Max(0, quickSort);
        _inputArray = _arrayGenerator.Random(_arrayLength);
        _displayArray = _inputArray;
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        GL.ClearColor(0.025f, 0.04f, 0.065f, 1f);
        GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
        _shaderProgram = CreateShaderProgram();
        _vertexArray = GL.GenVertexArray();
        _vertexBuffer = GL.GenBuffer();
        GL.BindVertexArray(_vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        const int stride = 5 * sizeof(float);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        TextInput += HandleTextInput;
    }

    protected override void OnFramebufferResize(FramebufferResizeEventArgs args)
    {
        base.OnFramebufferResize(args);
        GL.Viewport(0, 0, args.Width, args.Height);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        if (KeyboardState.IsKeyPressed(Keys.Escape))
        {
            if (_customFieldFocused || _lengthFieldFocused || _stepRateFocused)
            {
                _customFieldFocused = false;
                _lengthFieldFocused = false;
                _stepRateFocused = false;
            }
            else { Close(); return; }
        }
        if (_lengthFieldFocused && KeyboardState.IsKeyPressed(Keys.Backspace) && _lengthText.Length > 0)
            _lengthText = _lengthText[..^1];
        if (_stepRateFocused && KeyboardState.IsKeyPressed(Keys.Backspace) && _stepRateText.Length > 0)
            _stepRateText = _stepRateText[..^1];
        if (_customFieldFocused && KeyboardState.IsKeyPressed(Keys.Backspace) && _customText.Length > 0)
        {
            _customText = _customText[..^1];
            RefreshInputPreview();
        }
        if (KeyboardState.IsKeyPressed(Keys.Enter))
        {
            CommitLengthInput();
            CommitStepRateInput();
            _customFieldFocused = false;
        }
        if (KeyboardState.IsKeyPressed(Keys.Space) && _mode == SortMode.SortVisualizer && _timelineFrames.Count > 0)
            TogglePlayback();
        if (KeyboardState.IsKeyPressed(Keys.R) && (_isRunning || _isComplete))
            StartSelectedOperation();
        if (_mode == SortMode.SortVisualizer && !_lengthFieldFocused && !_customFieldFocused && !_stepRateFocused)
        {
            if (KeyboardState.IsKeyPressed(Keys.Left)) SetTimelineFrame(_currentFrameIndex - 1);
            if (KeyboardState.IsKeyPressed(Keys.Right)) SetTimelineFrame(_currentFrameIndex + 1);
        }

        var down = MouseState.IsButtonDown(MouseButton.Left);
        var pointer = GetPointerPosition();
        if (down && !_previousLeftMouseDown) DispatchClick(pointer);
        if (down && _isSeekingTimeline) SeekTimeline(pointer.X);
        if (!down) _isSeekingTimeline = false;
        _previousLeftMouseDown = down;

        CaptureQueuedSteps();
        UpdateOperationState();
        if (_mode == SortMode.SortVisualizer && !_isPaused)
            UpdateTimelinePlayback(args.Time);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        _hitTargets.Clear();
        var vertices = new List<float>(120_000);
        DrawInterface(vertices);
        GL.UseProgram(_shaderProgram);
        GL.BindVertexArray(_vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.DynamicDraw);
        GL.DrawArrays(PrimitiveType.Triangles, 0, vertices.Count / 5);
        Context.SwapBuffers();
    }

    protected override void OnUnload()
    {
        TextInput -= HandleTextInput;
        StopCurrentOperation();
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteVertexArray(_vertexArray);
        GL.DeleteProgram(_shaderProgram);
        base.OnUnload();
    }

    private static NativeWindowSettings CreateWindowSettings() => new()
    {
        Title = "Sorting Lab",
        ClientSize = new Vector2i(1700, 1000),
        API = ContextAPI.OpenGL,
        APIVersion = new Version(3, 3),
        Profile = ContextProfile.Core,
    };

    private void DrawInterface(List<float> v)
    {
        AddRect(v, -1f, -1f, 1f, 1f, new Vector3(0.025f, 0.04f, 0.065f));
        AddRect(v, -1f, 0.82f, 1f, 1f, new Vector3(0.045f, 0.07f, 0.105f));
        AddText(v, "SORTING LAB", -0.66f, 0.91f, 0.005f, 0.014f, new Vector3(0.36f, 0.82f, 1f));
        AddText(v, "SORT  |  SCRUB  |  COMPARE", 0.52f, 0.91f, 0.0028f, 0.009f, new Vector3(0.52f, 0.62f, 0.73f));
        var tabs = new (string Label, SortMode Mode)[] { ("SORTING VISUALIZER", SortMode.SortVisualizer), ("BENCHMARK", SortMode.Benchmark) };
        for (var i = 0; i < tabs.Length; i++)
        {
            var left = -0.74f + (i * 0.76f);
            var bounds = new ButtonBounds(left, left + 0.72f, 0.69f, 0.78f);
            AddButton(v, bounds, tabs[i].Label, _mode == tabs[i].Mode ? new Vector3(0.12f, 0.43f, 0.62f) : new Vector3(0.075f, 0.13f, 0.19f), new HitTarget(bounds, UiAction.SelectMode, (int)tabs[i].Mode), 0.0032f, 0.009f);
        }
        AddRect(v, -0.97f, -0.95f, -0.20f, 0.65f, new Vector3(0.045f, 0.067f, 0.095f));
        AddRect(v, -0.16f, -0.95f, 0.97f, 0.65f, new Vector3(0.04f, 0.06f, 0.087f));
        DrawConfiguration(v);
        DrawResultsPanel(v);
    }

    private void DrawConfiguration(List<float> v)
    {
        AddText(v, "ALGORITHM", -0.585f, 0.585f, 0.003f, 0.009f, new Vector3(0.56f, 0.68f, 0.78f));
        for (var i = 0; i < _algorithms.Count; i++)
        {
            var col = i / 5;
            var row = i % 5;
            var left = col == 0 ? -0.94f : -0.565f;
            var top = 0.515f - (row * 0.086f);
            var bounds = new ButtonBounds(left, left + 0.355f, top - 0.071f, top);
            AddButton(v, bounds, _algorithms[i].Name, i == _selectedAlgorithm ? new Vector3(0.12f, 0.37f, 0.53f) : new Vector3(0.075f, 0.12f, 0.17f), new HitTarget(bounds, UiAction.SelectAlgorithm, i), 0.0023f, 0.006f, true);
        }
        AddText(v, "INPUT DATA", -0.585f, 0.045f, 0.003f, 0.009f, new Vector3(0.56f, 0.68f, 0.78f));
        var inputs = new (string Text, DataMode Mode, float L, float R)[] { ("RANDOM", DataMode.Random, -0.94f, -0.57f), ("SORTED", DataMode.Sorted, -0.55f, -0.22f), ("REVERSED", DataMode.Reversed, -0.94f, -0.57f), ("CUSTOM", DataMode.Custom, -0.55f, -0.22f) };
        for (var i = 0; i < inputs.Length; i++)
        {
            var item = inputs[i];
            var top = i < 2 ? 0.005f : -0.09f;
            var bounds = new ButtonBounds(item.L, item.R, top - 0.078f, top);
            AddButton(v, bounds, item.Text, item.Mode == _dataMode ? new Vector3(0.12f, 0.32f, 0.43f) : new Vector3(0.075f, 0.12f, 0.17f), new HitTarget(bounds, UiAction.SelectDataMode, (int)item.Mode), 0.0028f, 0.007f);
        }
        AddText(v, "ARRAY LENGTH (MEMORY LIMITED)", -0.585f, -0.235f, 0.0026f, 0.0075f, new Vector3(0.56f, 0.68f, 0.78f));
        var minus = new ButtonBounds(-0.94f, -0.80f, -0.38f, -0.29f);
        var length = new ButtonBounds(-0.78f, -0.39f, -0.38f, -0.29f);
        var plus = new ButtonBounds(-0.37f, -0.22f, -0.38f, -0.29f);
        AddButton(v, minus, GetLengthStep() == 1 ? "-1" : "-100", new Vector3(0.075f, 0.12f, 0.17f), new HitTarget(minus, UiAction.DecreaseLength), 0.0027f, 0.007f);
        AddButton(v, length, _lengthFieldFocused ? _lengthText + "_" : _arrayLength.ToString("N0"), new Vector3(0.075f, 0.12f, 0.17f), new HitTarget(length, UiAction.FocusLength), 0.003f, 0.008f);
        AddButton(v, plus, GetLengthStep() == 1 ? "+1" : "+100", new Vector3(0.075f, 0.12f, 0.17f), new HitTarget(plus, UiAction.IncreaseLength), 0.0027f, 0.007f);
        AddText(v, "CUSTOM VALUES (COMMA OR SPACE SEPARATED)", -0.585f, -0.445f, 0.0021f, 0.0065f, new Vector3(0.5f, 0.62f, 0.72f));
        var custom = new ButtonBounds(-0.94f, -0.22f, -0.62f, -0.48f);
        AddButton(v, custom, GetCustomPreview(), _customFieldFocused ? new Vector3(0.12f, 0.22f, 0.29f) : new Vector3(0.065f, 0.105f, 0.15f), new HitTarget(custom, UiAction.FocusCustom), 0.0021f, 0.0065f, true);
        var start = new ButtonBounds(-0.94f, -0.58f, -0.84f, -0.72f);
        var fresh = new ButtonBounds(-0.56f, -0.22f, -0.84f, -0.72f);
        AddButton(v, start, _isRunning ? "RUNNING" : _isComplete ? "RUN AGAIN" : "START", new Vector3(0.1f, 0.43f, 0.58f), new HitTarget(start, UiAction.Start), 0.0032f, 0.009f);
        AddButton(v, fresh, "NEW DATA", new Vector3(0.075f, 0.14f, 0.2f), new HitTarget(fresh, UiAction.NewData), 0.0028f, 0.008f);
        AddText(v, _statusMessage.ToUpperInvariant(), -0.585f, -0.9f, 0.0018f, 0.0055f, _statusMessage.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ? new Vector3(1f, 0.42f, 0.34f) : new Vector3(0.5f, 0.66f, 0.75f));
    }

    private void DrawResultsPanel(List<float> v)
    {
        if (_mode == SortMode.Benchmark && _benchmarkResults.Count > 0)
        {
            DrawBenchmarkTable(v);
            DrawRuntimeButtons(v);
            return;
        }

        var title = _isRunning ? (_isPaused ? "PAUSED SORT" : "SORTING")
            : _sortResult is not null ? $"{_sortResult.AlgorithmName.ToUpperInvariant()} COMPLETE" : "ARRAY PREVIEW";
        AddText(v, title, 0.405f, 0.565f, 0.0032f, 0.009f, new Vector3(0.82f, 0.9f, 0.97f));
        AddText(v, $"{_inputArray.Length:N0} VALUES  |  {_dataMode.ToString().ToUpperInvariant()} DATA  |  {_algorithms[_selectedAlgorithm].Name.ToUpperInvariant()}",
            0.405f, 0.485f, 0.0023f, 0.007f, new Vector3(0.48f, 0.62f, 0.72f));
        if (_sortResult is not null)
        {
            DrawStatisticCard(v, -0.09f, "COMPARISONS", _sortResult.Statistics.Comparisons.ToString("N0"));
            DrawStatisticCard(v, 0.18f, "SWAPS", _sortResult.Statistics.Swaps.ToString("N0"));
            DrawStatisticCard(v, 0.45f, "WRITES", _sortResult.Statistics.Writes.ToString("N0"));
            DrawStatisticCard(v, 0.72f, "TIME MS", _sortResult.Elapsed.TotalMilliseconds.ToString("F3"));
        }
        else if (_mode == SortMode.SortVisualizer && _stepDescription.Length > 0)
            AddText(v, _stepDescription, 0.405f, 0.37f, 0.0019f, 0.0058f, new Vector3(1f, 0.72f, 0.35f));
        else if (_isRunning)
            AddText(v, $"FRAME {_currentFrameIndex + 1:N0} / {_timelineFrames.Count:N0}  |  {_statusMessage.ToUpperInvariant()}", 0.405f, 0.37f, 0.0022f, 0.0065f, new Vector3(0.55f, 0.75f, 0.88f));

        DrawArray(v, _displayArray, _highlightedIndices, -0.10f, 0.92f, -0.69f, _sortResult is null ? 0.39f : 0.24f);
        DrawRuntimeButtons(v);
    }

    private void DrawStatisticCard(List<float> v, float centerX, string label, string value)
    {
        AddRect(v, centerX - 0.125f, 0.27f, centerX + 0.125f, 0.42f, new Vector3(0.065f, 0.105f, 0.15f));
        AddText(v, label, centerX, 0.385f, 0.0018f, 0.0055f, new Vector3(0.46f, 0.62f, 0.72f));
        AddText(v, value, centerX, 0.315f, 0.0023f, 0.007f, new Vector3(0.89f, 0.94f, 0.98f));
    }

    private void DrawBenchmarkTable(List<float> v)
    {
        AddText(v, "BENCHMARK RESULTS", 0.405f, 0.565f, 0.0031f, 0.009f, new Vector3(0.82f, 0.9f, 0.97f));
        AddText(v, $"SAME INPUT  |  {_inputArray.Length:N0} VALUES", 0.405f, 0.49f, 0.0024f, 0.007f, new Vector3(0.48f, 0.62f, 0.72f));
        var columns = new (string Label, float X)[] { ("ALGORITHM", -0.005f), ("MS", 0.33f), ("COMPS", 0.51f), ("SWAPS", 0.70f), ("WRITES", 0.88f) };
        foreach (var column in columns)
            AddText(v, column.Label, column.X, 0.405f, 0.0019f, 0.0058f, new Vector3(0.46f, 0.62f, 0.72f));
        for (var i = 0; i < _benchmarkResults.Count; i++)
        {
            var result = _benchmarkResults[i];
            var y = 0.325f - (i * 0.078f);
            AddRect(v, -0.12f, y - 0.031f, 0.93f, y + 0.031f, i % 2 == 0 ? new Vector3(0.055f, 0.082f, 0.115f) : new Vector3(0.045f, 0.068f, 0.095f));
            AddText(v, result.AlgorithmName.ToUpperInvariant(), -0.005f, y, 0.0019f, 0.0058f, new Vector3(0.83f, 0.9f, 0.96f));
            AddText(v, result.Elapsed.TotalMilliseconds.ToString("F3"), 0.33f, y, 0.0019f, 0.0058f, new Vector3(0.73f, 0.85f, 0.93f));
            AddText(v, result.Statistics.Comparisons.ToString("N0"), 0.51f, y, 0.0018f, 0.0056f, new Vector3(0.73f, 0.85f, 0.93f));
            AddText(v, result.Statistics.Swaps.ToString("N0"), 0.70f, y, 0.0018f, 0.0056f, new Vector3(0.73f, 0.85f, 0.93f));
            AddText(v, result.Statistics.Writes.ToString("N0"), 0.88f, y, 0.0018f, 0.0056f, new Vector3(0.73f, 0.85f, 0.93f));
        }
    }

    private void DrawRuntimeButtons(List<float> v)
    {
        if (_mode == SortMode.SortVisualizer && _timelineFrames.Count > 0)
        {
            DrawTimelineSlider(v);
            var play = new ButtonBounds(-0.14f, 0.02f, -0.92f, -0.82f);
            var previous = new ButtonBounds(0.035f, 0.15f, -0.92f, -0.82f);
            var next = new ButtonBounds(0.165f, 0.28f, -0.92f, -0.82f);
            var slower = new ButtonBounds(0.295f, 0.37f, -0.92f, -0.82f);
            var rate = new ButtonBounds(0.385f, 0.62f, -0.92f, -0.82f);
            var faster = new ButtonBounds(0.635f, 0.71f, -0.92f, -0.82f);
            var restart = new ButtonBounds(0.73f, 0.86f, -0.92f, -0.82f);
            var stop = new ButtonBounds(0.88f, 0.97f, -0.92f, -0.82f);
            AddButton(v, play, _isPaused ? "PLAY" : "PAUSE", new Vector3(0.1f, 0.34f, 0.47f), new HitTarget(play, UiAction.TogglePause), 0.0021f, 0.0065f);
            AddButton(v, previous, "PREV", new Vector3(0.075f, 0.14f, 0.2f), new HitTarget(previous, UiAction.PreviousStep), 0.0018f, 0.0058f);
            AddButton(v, next, "NEXT", new Vector3(0.1f, 0.43f, 0.58f), new HitTarget(next, UiAction.NextStep), 0.0018f, 0.0058f);
            AddButton(v, slower, "-", new Vector3(0.075f, 0.14f, 0.2f), new HitTarget(slower, UiAction.Slower), 0.003f, 0.008f);
            var rateLabel = _stepRateFocused ? _stepRateText + "_" : $"{_stepsPerSecond} STEPS/SEC";
            AddButton(v, rate, rateLabel, _stepRateFocused ? new Vector3(0.13f, 0.23f, 0.3f) : new Vector3(0.075f, 0.12f, 0.17f), new HitTarget(rate, UiAction.FocusStepRate), 0.0018f, 0.0058f);
            AddButton(v, faster, "+", new Vector3(0.075f, 0.14f, 0.2f), new HitTarget(faster, UiAction.Faster), 0.003f, 0.008f);
            AddButton(v, restart, "RESTART", new Vector3(0.075f, 0.14f, 0.2f), new HitTarget(restart, UiAction.Restart), 0.0018f, 0.0058f);
            if (_isRunning)
                AddButton(v, stop, "STOP", new Vector3(0.22f, 0.11f, 0.11f), new HitTarget(stop, UiAction.Stop), 0.0018f, 0.0058f);
        }
        else if (_mode == SortMode.Benchmark && _isRunning)
            AddSmallRuntimeButton(v, "STOP", 0.85f, UiAction.Stop);
        else if (_mode == SortMode.Benchmark && _isComplete)
            AddSmallRuntimeButton(v, "RESTART", 0.85f, UiAction.Restart);
    }

    private void AddSmallRuntimeButton(List<float> v, string label, float centerX, UiAction action)
    {
        var bounds = new ButtonBounds(centerX - 0.105f, centerX + 0.105f, -0.92f, -0.82f);
        AddButton(v, bounds, label, new Vector3(0.075f, 0.14f, 0.2f), new HitTarget(bounds, action), 0.0021f, 0.0065f);
    }
    private void DrawTimelineSlider(List<float> v)
    {
        var track = new ButtonBounds(-0.10f, 0.92f, -0.81f, -0.78f);
        AddRect(v, track.Left, track.Bottom, track.Right, track.Top, new Vector3(0.035f, 0.06f, 0.085f));

        var progress = _timelineFrames.Count <= 1 ? 0f : (float)_currentFrameIndex / (_timelineFrames.Count - 1);
        var thumbX = track.Left + ((track.Right - track.Left) * progress);
        AddRect(v, track.Left, track.Bottom + 0.006f, thumbX, track.Top - 0.006f, new Vector3(0.12f, 0.43f, 0.62f));
        AddRect(v, thumbX - 0.006f, track.Bottom - 0.006f, thumbX + 0.006f, track.Top + 0.006f, new Vector3(0.42f, 0.82f, 1f));
        AddText(v, $"FRAME {_currentFrameIndex + 1:N0} / {_timelineFrames.Count:N0}", 0.41f, -0.742f, 0.0018f, 0.0055f, new Vector3(0.58f, 0.73f, 0.82f));
        _hitTargets.Add(new HitTarget(track, UiAction.SeekTimeline));
    }

    private void DrawArray(List<float> v, int[] values, HashSet<int> highlighted, float left, float right, float bottom, float top)
    {
        AddRect(v, left, bottom - 0.012f, right, bottom, new Vector3(0.17f, 0.25f, 0.32f));
        if (values.Length == 0)
        {
            AddText(v, "ENTER CUSTOM VALUES", (left + right) / 2f, 0f, 0.0028f, 0.008f, new Vector3(0.6f, 0.7f, 0.76f));
            return;
        }

        var barCount = Math.Min(values.Length, Math.Max(1, (int)Math.Ceiling((right - left) * Math.Max(1, ClientSize.X) / 2d)));
        var sampledMinimum = int.MaxValue;
        var sampledMaximum = int.MinValue;
        for (var i = 0; i < barCount; i++)
        {
            var sampleIndex = (int)((((long)i * 2 + 1) * values.Length) / (2L * barCount));
            sampleIndex = Math.Min(sampleIndex, values.Length - 1);
            sampledMinimum = Math.Min(sampledMinimum, values[sampleIndex]);
            sampledMaximum = Math.Max(sampledMaximum, values[sampleIndex]);
        }

        var range = (double)sampledMaximum - sampledMinimum;
        var cell = (right - left) / barCount;
        var gap = Math.Min(0.003f, cell * 0.10f);
        for (var i = 0; i < barCount; i++)
        {
            var bucketStart = (long)i * values.Length / barCount;
            var bucketEnd = (long)(i + 1) * values.Length / barCount;
            var sampleIndex = (int)((((long)i * 2 + 1) * values.Length) / (2L * barCount));
            sampleIndex = Math.Min(sampleIndex, values.Length - 1);
            var fraction = range == 0 ? 0.5f : (float)(((double)values[sampleIndex] - sampledMinimum) / range);
            var barTop = bottom + 0.018f + ((top - bottom - 0.035f) * fraction);
            var isHighlighted = highlighted.Any(index => index >= bucketStart && index < bucketEnd);
            var color = isHighlighted ? new Vector3(1f, 0.58f, 0.22f) : new Vector3(0.12f + fraction * 0.13f, 0.55f + fraction * 0.24f, 0.82f + fraction * 0.14f);
            AddRect(v, left + i * cell + gap, bottom, left + (i + 1) * cell - gap, barTop, color);
        }
    }

    private void AddButton(List<float> v, ButtonBounds bounds, string text, Vector3 color, HitTarget target,
        float cellWidth = 0.0026f, float cellHeight = 0.0075f, bool leftAligned = false)
    {
        var finalColor = bounds.Contains(GetPointerPosition()) ? color + new Vector3(0.08f) : color;
        AddRect(v, bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, finalColor);
        if (leftAligned) AddTextLeft(v, text, bounds.Left + 0.018f, (bounds.Bottom + bounds.Top) / 2f, cellWidth, cellHeight, new Vector3(0.88f, 0.93f, 0.97f));
        else AddText(v, text, (bounds.Left + bounds.Right) / 2f, (bounds.Bottom + bounds.Top) / 2f, cellWidth, cellHeight, new Vector3(0.88f, 0.93f, 0.97f));
        _hitTargets.Add(target);
    }

    private void DispatchClick(Vector2 position)
    {
        for (var i = _hitTargets.Count - 1; i >= 0; i--)
        {
            var target = _hitTargets[i];
            if (target.Bounds.Contains(position))
            {
                if (_lengthFieldFocused && target.Action != UiAction.FocusLength) CommitLengthInput();
                if (_stepRateFocused && target.Action != UiAction.FocusStepRate) CommitStepRateInput();
                ApplyAction(target, position);
                return;
            }
        }
    }

    private void ApplyAction(HitTarget target, Vector2 position)
    {
        switch (target.Action)
        {
            case UiAction.SelectMode when !_isRunning:
                _mode = (SortMode)target.Value;
                ClearTimeline();
                if (_dataMode != DataMode.Custom)
                {
                    _lengthText = _arrayLength.ToString();
                    RefreshInputPreview();
                }
                _sortResult = null;
                _benchmarkResults.Clear();
                _statusMessage = "Choose settings and press Start.";
                break;
            case UiAction.SelectAlgorithm when !_isRunning:
                _selectedAlgorithm = target.Value;
                ClearTimeline();
                break;
            case UiAction.SelectDataMode when !_isRunning:
                _dataMode = (DataMode)target.Value;
                _customFieldFocused = _dataMode == DataMode.Custom;
                ClearTimeline();
                RefreshInputPreview();
                break;
            case UiAction.DecreaseLength when !_isRunning:
                ClearTimeline(); ChangeLength(-GetLengthStep()); break;
            case UiAction.IncreaseLength when !_isRunning:
                ClearTimeline(); ChangeLength(GetLengthStep()); break;
            case UiAction.FocusLength when !_isRunning:
                _lengthFieldFocused = true; _customFieldFocused = false; _stepRateFocused = false; _lengthText = string.Empty; break;
            case UiAction.FocusStepRate when _mode == SortMode.SortVisualizer && _timelineFrames.Count > 0:
                _stepRateFocused = true; _lengthFieldFocused = false; _customFieldFocused = false; _stepRateText = string.Empty; break;
            case UiAction.FocusCustom when !_isRunning:
                ClearTimeline(); _customFieldFocused = true; _lengthFieldFocused = false; _dataMode = DataMode.Custom; RefreshInputPreview(); break;
            case UiAction.NewData when !_isRunning:
                ClearTimeline(); RefreshInputPreview(forceRandom: true); break;
            case UiAction.Start when !_isRunning: StartSelectedOperation(); break;
            case UiAction.TogglePause when _mode == SortMode.SortVisualizer && _timelineFrames.Count > 0: TogglePlayback(); break;
            case UiAction.Restart when _isRunning || _isComplete || _timelineFrames.Count > 0: StartSelectedOperation(); break;
            case UiAction.Slower when _mode == SortMode.SortVisualizer && _timelineFrames.Count > 0:
                _stepsPerSecond = Math.Max(1, _stepsPerSecond - 1); break;
            case UiAction.Faster when _mode == SortMode.SortVisualizer && _timelineFrames.Count > 0:
                _stepsPerSecond = Math.Min(240, _stepsPerSecond + 1); break;
            case UiAction.PreviousStep when _mode == SortMode.SortVisualizer:
                SetTimelineFrame(_currentFrameIndex - 1); break;
            case UiAction.NextStep when _mode == SortMode.SortVisualizer:
                SetTimelineFrame(_currentFrameIndex + 1); break;
            case UiAction.SeekTimeline when _mode == SortMode.SortVisualizer:
                _isSeekingTimeline = true; SeekTimeline(position.X); break;
            case UiAction.Stop when _isRunning:
                StopCurrentOperation(); _isRunning = false; _isPaused = true; _isComplete = true; _statusMessage = "Stopped."; break;
        }
    }

    private void StartSelectedOperation()
    {
        if (!TryGetInputForCurrentMode(out var input)) return;
        StopCurrentOperation();
        Interlocked.Increment(ref _runGeneration);
        _steps.Clear();
        _timelineFrames.Clear();
        _currentFrameIndex = 0;
        _isSeekingTimeline = false;
        _stepDescription = string.Empty;
        _stepPlaybackAccumulator = 0;
        _highlightedIndices.Clear();
        _sortResult = null;
        _benchmarkResults.Clear();
        _inputArray = input;
        _displayArray = input;
        if (_mode == SortMode.SortVisualizer)
        {
            _timelineFrames.Add(new SortStep(input, [], "INITIAL ARRAY"));
            _stepDescription = "INITIAL ARRAY";
        }
        _isRunning = true;
        _isComplete = false;
        _isPaused = _mode == SortMode.SortVisualizer;
        _statusMessage = _mode == SortMode.Benchmark ? "Running all algorithms..." : $"Running {_algorithms[_selectedAlgorithm].Name}...";
        _runCancellation = new CancellationTokenSource();
        var token = _runCancellation.Token;
        var generation = Interlocked.Read(ref _runGeneration);
        var algorithm = _algorithms[_selectedAlgorithm].Name;
        if (_mode == SortMode.Benchmark)
        {
            _benchmarkTask = Task.Run<IReadOnlyList<SortResult>>(() =>
            {
                var results = new List<SortResult>(_algorithms.Count);
                foreach (var item in _algorithms)
                {
                    token.ThrowIfCancellationRequested();
                    results.Add(_sortingService.Sort(input, item.Name, cancellationToken: token));
                }
                return results;
            });
            _sortTask = null;
        }
        else
        {
            Action<SortStep>? recorder = _mode == SortMode.SortVisualizer
                ? step => { if (!token.IsCancellationRequested && generation == Interlocked.Read(ref _runGeneration)) _steps.Enqueue(step); }
                : null;
            var interval = GetStepSamplingInterval(input, algorithm);
            _sortTask = Task.Run(() => _sortingService.Sort(input, algorithm, recorder, token, interval));
            _benchmarkTask = null;
        }
    }

    private bool TryGetInputForCurrentMode(out int[] input)
    {
        try
        {
            if (_dataMode == DataMode.Custom)
            {
                if (!ArrayGenerator.TryParseCustom(_customText, out input))
                {
                    _statusMessage = "Error: enter valid integers separated by commas or spaces.";
                    return false;
                }

                _arrayLength = input.Length;
                _lengthText = input.Length.ToString();
                return true;
            }

            if (_arrayLength < 1 || _arrayLength > GetArrayLengthLimit())
            {
                _statusMessage = "Error: enter a supported positive array length.";
                input = [];
                return false;
            }

            input = _dataMode switch
            {
                DataMode.Random => _inputArray.Length == _arrayLength ? _inputArray : _arrayGenerator.Random(_arrayLength),
                DataMode.Sorted => _arrayGenerator.Sorted(_arrayLength),
                DataMode.Reversed => _arrayGenerator.Reversed(_arrayLength),
                _ => [],
            };
            return true;
        }
        catch (OutOfMemoryException)
        {
            _statusMessage = "Error: the requested array is too large for available memory.";
            input = [];
            return false;
        }
    }

    private void CaptureQueuedSteps()
    {
        while (_steps.TryDequeue(out var step))
            _timelineFrames.Add(step);
    }

    private void UpdateOperationState()
    {
        if (!_isRunning) return;
        if (_sortTask is { IsCompleted: true } && _steps.IsEmpty)
        {
            if (_sortTask.IsFaulted) _statusMessage = $"Error: {_sortTask.Exception?.GetBaseException().Message}";
            else if (_sortTask.IsCanceled) _statusMessage = "Sort cancelled.";
            else
            {
                _sortResult = _sortTask.Result;
                _timelineFrames.Add(new SortStep(_sortResult.SortedArray, [], "SORT COMPLETE"));
                _statusMessage = $"{_sortResult.AlgorithmName} completed in {_sortResult.Elapsed.TotalMilliseconds:F3} ms.";
            }
            _isRunning = false;
            _isComplete = true;
            DisposeCompletedCancellation();
        }
        else if (_benchmarkTask is { IsCompleted: true })
        {
            if (_benchmarkTask.IsFaulted) _statusMessage = $"Error: {_benchmarkTask.Exception?.GetBaseException().Message}";
            else if (_benchmarkTask.IsCanceled) _statusMessage = "Benchmark cancelled.";
            else { _benchmarkResults.AddRange(_benchmarkTask.Result); _statusMessage = "Benchmark complete."; }
            _isRunning = false;
            _isComplete = true;
            DisposeCompletedCancellation();
        }
    }

    private void UpdateTimelinePlayback(double elapsedSeconds)
    {
        if (_timelineFrames.Count <= 1) return;

        _stepPlaybackAccumulator = Math.Min(_stepPlaybackAccumulator + elapsedSeconds * _stepsPerSecond, _stepsPerSecond);
        var due = (int)_stepPlaybackAccumulator;
        if (due > 0)
        {
            var available = _timelineFrames.Count - 1 - _currentFrameIndex;
            var advanced = Math.Min(due, Math.Max(0, available));
            if (advanced > 0)
            {
                SetTimelineFrame(_currentFrameIndex + advanced);
                _stepPlaybackAccumulator -= advanced;
            }
            else
            {
                _stepPlaybackAccumulator = 0;
            }
        }

        if (_isComplete && _currentFrameIndex >= _timelineFrames.Count - 1)
        {
            _isPaused = true;
            _stepPlaybackAccumulator = 0;
        }
    }

    private void TogglePlayback()
    {
        if (_timelineFrames.Count == 0) return;
        if (_isPaused && _isComplete && _currentFrameIndex >= _timelineFrames.Count - 1)
            SetTimelineFrame(0);
        _isPaused = !_isPaused;
        _stepPlaybackAccumulator = 0;
    }

    private void SetTimelineFrame(int index)
    {
        if (_timelineFrames.Count == 0) return;
        _currentFrameIndex = Math.Clamp(index, 0, _timelineFrames.Count - 1);
        var frame = _timelineFrames[_currentFrameIndex];
        _displayArray = frame.ArrayState;
        _highlightedIndices = frame.HighlightedIndices.ToHashSet();
        _stepDescription = frame.Description.ToUpperInvariant();
    }

    private void SeekTimeline(float pointerX)
    {
        if (_timelineFrames.Count == 0 || !float.IsFinite(pointerX)) return;
        const float left = -0.10f;
        const float right = 0.92f;
        var progress = Math.Clamp((pointerX - left) / (right - left), 0f, 1f);
        var index = (int)Math.Round(progress * (_timelineFrames.Count - 1));
        SetTimelineFrame(index);
    }

    private void ClearTimeline()
    {
        _steps.Clear();
        _timelineFrames.Clear();
        _currentFrameIndex = 0;
        _displayArray = _inputArray;
        _highlightedIndices.Clear();
        _stepDescription = string.Empty;
        _stepPlaybackAccumulator = 0;
        _isSeekingTimeline = false;
        _sortResult = null;
        _isComplete = false;
        if (!_isRunning) _isPaused = true;
    }

    private void StopCurrentOperation()
    {
        var source = _runCancellation;
        if (source is null) return;
        source.Cancel();
        var task = (Task?)_sortTask ?? _benchmarkTask;
        if (task is null || task.IsCompleted) source.Dispose();
        else _ = task.ContinueWith(done => { _ = done.Exception; source.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _runCancellation = null; _sortTask = null; _benchmarkTask = null;
    }

    private void DisposeCompletedCancellation()
    {
        _runCancellation?.Dispose();
        _runCancellation = null;
    }

    private void ChangeLength(int delta)
    {
        _arrayLength = (int)Math.Clamp((long)_arrayLength + delta, 1L, GetArrayLengthLimit());
        _lengthText = _arrayLength.ToString();
        RefreshInputPreview();
    }

    private void CommitLengthInput()
    {
        if (!_lengthFieldFocused) return;
        if (int.TryParse(_lengthText, out var value) && value >= 1 && value <= GetArrayLengthLimit())
        {
            _arrayLength = value; _statusMessage = "Array length updated."; RefreshInputPreview();
        }
        else
        {
            _statusMessage = "Error: enter a supported positive array length."; _lengthText = _arrayLength.ToString();
        }
        _lengthFieldFocused = false;
    }

    private void CommitStepRateInput()
    {
        if (!_stepRateFocused) return;
        if (int.TryParse(_stepRateText, out var value) && value is >= 1 and <= 240)
        {
            _stepsPerSecond = value;
            _statusMessage = $"Playback set to {value} steps per second.";
        }
        else
        {
            _statusMessage = "Error: enter a playback rate from 1 to 240 steps per second.";
            _stepRateText = _stepsPerSecond.ToString();
        }
        _stepRateFocused = false;
    }
    private void RefreshInputPreview(bool forceRandom = false)
    {
        try
        {
            RefreshInputPreviewCore(forceRandom);
        }
        catch (OutOfMemoryException)
        {
            _inputArray = [];
            _displayArray = [];
            _sortResult = null;
            _statusMessage = "Error: this array does not fit in available memory.";
        }
    }

    private void RefreshInputPreviewCore(bool forceRandom)
    {
        if (_dataMode == DataMode.Custom)
        {
            if (ArrayGenerator.TryParseCustom(_customText, out var custom))
            {
                _inputArray = custom;
                _arrayLength = custom.Length;
                _lengthText = custom.Length.ToString();
                _statusMessage = $"Custom input: {custom.Length:N0} values.";
            }
            else
            {
                _inputArray = [];
                if (_customText.Length > 0) _statusMessage = "Error: custom input must contain valid integers.";
            }
        }
        else
        {
            if (_dataMode == DataMode.Random && forceRandom) _inputArray = _arrayGenerator.Random(_arrayLength);
            else _inputArray = _dataMode switch
            {
                DataMode.Random => _arrayGenerator.Random(_arrayLength),
                DataMode.Sorted => _arrayGenerator.Sorted(_arrayLength),
                DataMode.Reversed => _arrayGenerator.Reversed(_arrayLength),
                _ => [],
            };
        }
        _displayArray = _inputArray;
        _sortResult = null;
    }

    private string GetCustomPreview()
    {
        const int visible = 58;
        var preview = _customText.Length <= visible ? _customText : "..." + _customText[^visible..];
        return _customFieldFocused ? preview + "_" : preview;
    }

    private static int GetArrayLengthLimit() => Array.MaxLength;

    private int GetLengthStep() => 100;

    private static int GetStepSamplingInterval(int[] input, string algorithmName)
    {
        if (input.Length <= 128) return 1;

        var samplePairs = Math.Min(input.Length - 1, 4_096);
        var sorted = true;
        for (var i = 0; i < samplePairs; i++)
        {
            var index = (int)((long)i * (input.Length - 1) / samplePairs);
            if (input[index] <= input[index + 1]) continue;
            sorted = false;
            break;
        }

        var name = algorithmName.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var expected = name switch
        {
            "BUBBLESORT" or "COCKTAILSHAKERSORT" or "INSERTIONSORT" when sorted => input.Length * 2d,
            "BUBBLESORT" or "COCKTAILSHAKERSORT" or "SELECTIONSORT" or "INSERTIONSORT" => (double)input.Length * input.Length / 2d,
            "SHELLSORT" => input.Length * Math.Log2(input.Length) * 4d,
            "COUNTINGSORT" => input.Length * 3d,
            "RADIXSORT" => input.Length * 16d,
            _ => input.Length * Math.Log2(input.Length) * 3d,
        };

        const long snapshotBudgetBytes = 32L * 1024 * 1024;
        const int snapshotCountLimit = 750;
        var bytesPerSnapshot = (long)input.Length * sizeof(int);
        var snapshotCount = (int)Math.Clamp(snapshotBudgetBytes / bytesPerSnapshot, 1L, snapshotCountLimit);
        return (int)Math.Clamp(Math.Ceiling(expected / snapshotCount), 1d, int.MaxValue);
    }

    private void HandleTextInput(TextInputEventArgs args)
    {
        if (_lengthFieldFocused)
        {
            foreach (var c in args.AsString) if (char.IsDigit(c) && _lengthText.Length < Array.MaxLength.ToString().Length) _lengthText += c;
        }
        else if (_stepRateFocused)
        {
            foreach (var c in args.AsString) if (char.IsDigit(c) && _stepRateText.Length < 3) _stepRateText += c;
        }
        else if (_customFieldFocused)
        {
            var accepted = new StringBuilder(args.AsString.Length);
            foreach (var c in args.AsString)
                if (char.IsDigit(c) || c is '-' or '+' or ',' or ';' or ' ' or '\t') accepted.Append(c);
            if (accepted.Length > 0)
            {
                _customText = string.Concat(_customText, accepted.ToString());
                _inputArray = [];
                _displayArray = [];
                _statusMessage = "Custom values will be validated when you start.";
            }
        }
    }

    private Vector2 GetPointerPosition()
    {
        var size = ClientSize;
        if (size.X <= 0 || size.Y <= 0) return new Vector2(float.NaN, float.NaN);
        var pointer = MouseState.Position;
        return new Vector2(pointer.X / size.X * 2f - 1f, 1f - pointer.Y / size.Y * 2f);
    }

    private static int CreateShaderProgram()
    {
        var vertex = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        var fragment = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);
        var program = GL.CreateProgram();
        GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var linked);
        GL.DetachShader(program, vertex); GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex); GL.DeleteShader(fragment);
        if (linked == 0)
        {
            var message = GL.GetProgramInfoLog(program); GL.DeleteProgram(program);
            throw new InvalidOperationException($"OpenGL shader program could not link: {message}");
        }
        return program;
    }

    private static int CompileShader(ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var compiled);
        if (compiled == 0)
        {
            var message = GL.GetShaderInfoLog(shader); GL.DeleteShader(shader);
            throw new InvalidOperationException($"OpenGL shader could not compile: {message}");
        }
        return shader;
    }

    private static void AddText(List<float> v, string text, float centerX, float centerY, float cellWidth, float cellHeight, Vector3 color)
    {
        var advance = cellWidth * 6f;
        var width = text.Length == 0 ? 0f : (text.Length - 1) * advance + cellWidth * 5f;
        AddTextLeft(v, text, centerX - width / 2f, centerY, cellWidth, cellHeight, color);
    }

    private static void AddTextLeft(List<float> v, string text, float leftX, float centerY, float cellWidth, float cellHeight, Vector3 color)
    {
        var topY = centerY + cellHeight * 3.5f;
        foreach (var (character, characterIndex) in text.Select((value, index) => (value, index)))
        {
            if (!SortingVisualizerWindow.Font.TryGetValue(char.ToUpperInvariant(character), out var rows)) continue;
            for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] != '1') continue;
                var left = leftX + characterIndex * cellWidth * 6f + column * cellWidth;
                var top = topY - row * cellHeight;
                AddRect(v, left, top - cellHeight * 0.82f, left + cellWidth * 0.82f, top, color);
            }
        }
    }

    private static void AddRect(List<float> v, float left, float bottom, float right, float top, Vector3 color)
    {
        AddVertex(v, left, bottom, color); AddVertex(v, left, top, color); AddVertex(v, right, top, color);
        AddVertex(v, left, bottom, color); AddVertex(v, right, top, color); AddVertex(v, right, bottom, color);
    }

    private static void AddVertex(List<float> v, float x, float y, Vector3 color)
    {
        v.Add(x); v.Add(y); v.Add(color.X); v.Add(color.Y); v.Add(color.Z);
    }

    private void StopCurrentOperationOnClose() => StopCurrentOperation();

    private enum SortMode { SortVisualizer, Benchmark }
    private enum DataMode { Random, Sorted, Reversed, Custom }
    private enum UiAction { SelectMode, SelectAlgorithm, SelectDataMode, DecreaseLength, IncreaseLength, FocusLength, FocusStepRate, FocusCustom, NewData, Start, TogglePause, Restart, Slower, Faster, PreviousStep, NextStep, SeekTimeline, Stop }
    private readonly record struct ButtonBounds(float Left, float Right, float Bottom, float Top)
    {
        public bool Contains(Vector2 point) => point.X >= Left && point.X <= Right && point.Y >= Bottom && point.Y <= Top;
    }
    private readonly record struct HitTarget(ButtonBounds Bounds, UiAction Action, int Value = 0);
}








