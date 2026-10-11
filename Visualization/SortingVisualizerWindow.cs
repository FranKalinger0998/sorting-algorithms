using System.Collections.Concurrent;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SortingAlgorithms.Models;
using SortingAlgorithms.Services;

namespace SortingAlgorithms.Visualization;

public sealed class SortingVisualizerWindow : GameWindow
{
    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec3 aColor;
        out vec3 vertexColor;
        void main()
        {
            gl_Position = vec4(aPosition, 0.0, 1.0);
            vertexColor = aColor;
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec3 vertexColor;
        out vec4 fragmentColor;
        void main()
        {
            fragmentColor = vec4(vertexColor, 1.0);
        }
        """;

    private static readonly ButtonBounds PauseButton = new(-0.96f, -0.71f, 0.68f, 0.83f);
    private static readonly ButtonBounds RestartButton = new(-0.68f, -0.39f, 0.68f, 0.83f);
    private static readonly ButtonBounds SlowerButton = new(-0.36f, -0.12f, 0.68f, 0.83f);
    private static readonly ButtonBounds FasterButton = new(-0.09f, 0.15f, 0.68f, 0.83f);
    private static readonly ButtonBounds CloseButton = new(0.69f, 0.96f, 0.68f, 0.83f);
    internal static readonly IReadOnlyDictionary<char, string[]> Font = CreateFont();

    private readonly SortingService _sortingService;
    private readonly string _algorithmName;
    private readonly int[] _input;
    private readonly ConcurrentQueue<SortStep> _steps = new();

    private int[] _displayValues;
    private HashSet<int> _highlightedIndices = new();
    private int _shaderProgram;
    private int _vertexArray;
    private int _vertexBuffer;
    private Task<SortResult>? _sortTask;
    private CancellationTokenSource? _runCancellation;
    private long _runGeneration;
    private long _stepCount;
    private int _stepsPerFrame = 8;
    private bool _isPaused;
    private bool _isComplete;
    private bool _previousLeftMouseDown;
    private string _status = "Sorting";

    public SortingVisualizerWindow(SortingService sortingService, string algorithmName, int[] input)
        : base(GameWindowSettings.Default, CreateWindowSettings(algorithmName, input.Length))
    {
        _sortingService = sortingService;
        _algorithmName = algorithmName;
        _input = (int[])input.Clone();
        _displayValues = (int[])input.Clone();
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(0.035f, 0.055f, 0.09f, 1f);
        GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
        _shaderProgram = CreateShaderProgram();
        _vertexArray = GL.GenVertexArray();
        _vertexBuffer = GL.GenBuffer();

        GL.BindVertexArray(_vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        var stride = 5 * sizeof(float);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        StartSort();
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
            Close();
            return;
        }

        if (KeyboardState.IsKeyPressed(Keys.Space))
        {
            TogglePause();
        }

        if (KeyboardState.IsKeyPressed(Keys.R))
        {
            RestartSort();
        }

        if (KeyboardState.IsKeyPressed(Keys.Right))
        {
            IncreaseSpeed();
        }

        if (KeyboardState.IsKeyPressed(Keys.Left))
        {
            DecreaseSpeed();
        }

        var leftMouseDown = MouseState.IsButtonDown(MouseButton.Left);
        if (leftMouseDown && !_previousLeftMouseDown)
        {
            HandleButtonClick(GetPointerPosition());
        }

        _previousLeftMouseDown = leftMouseDown;

        if (!_isPaused)
        {
            for (var index = 0; index < _stepsPerFrame && _steps.TryDequeue(out var step); index++)
            {
                _displayValues = step.ArrayState;
                _highlightedIndices = step.HighlightedIndices.ToHashSet();
                _stepCount++;
            }
        }

        if (!_isComplete && _sortTask is { IsCompleted: true } && _steps.IsEmpty)
        {
            if (_sortTask.IsFaulted)
            {
                _status = $"Failed: {_sortTask.Exception?.GetBaseException().Message}";
            }
            else if (_sortTask.IsCanceled)
            {
                _status = "Stopped";
            }
            else
            {
                var result = _sortTask.Result;
                _displayValues = result.SortedArray;
                _highlightedIndices.Clear();
                _status = $"Complete in {result.Elapsed.TotalMilliseconds:F3} ms";
            }

            _isComplete = true;
            RefreshTitle();
        }
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        GL.Clear(ClearBufferMask.ColorBufferBit);
        var vertices = CreateBarVertices(_displayValues, _highlightedIndices);
        AddControls(vertices);
        GL.UseProgram(_shaderProgram);
        GL.BindVertexArray(_vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.DynamicDraw);
        GL.DrawArrays(PrimitiveType.Triangles, 0, vertices.Count / 5);
        Context.SwapBuffers();
    }

    protected override void OnUnload()
    {
        StopCurrentSort();
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteVertexArray(_vertexArray);
        GL.DeleteProgram(_shaderProgram);
        base.OnUnload();
    }

    private static int CreateShaderProgram()
    {
        var vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        var fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);
        var program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var linked);

        GL.DetachShader(program, vertexShader);
        GL.DetachShader(program, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        if (linked == 0)
        {
            var message = GL.GetProgramInfoLog(program);
            GL.DeleteProgram(program);
            throw new InvalidOperationException($"OpenGL shader program could not link: {message}");
        }

        return program;
    }

    private static int CompileShader(ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var compiled);
        if (compiled == 0)
        {
            var message = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"OpenGL shader could not compile: {message}");
        }

        return shader;
    }
    private static NativeWindowSettings CreateWindowSettings(string algorithmName, int valueCount)
    {
        return new NativeWindowSettings
        {
            Title = $"{algorithmName} — {valueCount:N0} bars",
            ClientSize = new Vector2i(1600, 900),
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
        };
    }

    private void StartSort()
    {
        _runGeneration++;
        var generation = _runGeneration;
        _runCancellation = new CancellationTokenSource();
        var token = _runCancellation.Token;
        var samplingInterval = GetStepSamplingInterval();

        _sortTask = Task.Run(() => _sortingService.Sort(
            _input,
            _algorithmName,
            step =>
            {
                if (generation == _runGeneration && !token.IsCancellationRequested)
                {
                    _steps.Enqueue(step);
                }
            },
            token,
            samplingInterval));
        RefreshTitle();
    }

    private void RestartSort()
    {
        _runGeneration++;
        StopCurrentSort();
        _steps.Clear();
        _displayValues = (int[])_input.Clone();
        _highlightedIndices.Clear();
        _stepCount = 0;
        _isPaused = false;
        _isComplete = false;
        _status = "Sorting";

        var generation = _runGeneration;
        _runCancellation = new CancellationTokenSource();
        var token = _runCancellation.Token;
        var samplingInterval = GetStepSamplingInterval();
        _sortTask = Task.Run(() => _sortingService.Sort(
            _input,
            _algorithmName,
            step =>
            {
                if (generation == _runGeneration && !token.IsCancellationRequested)
                {
                    _steps.Enqueue(step);
                }
            },
            token,
            samplingInterval));
        RefreshTitle();
    }

    private void StopCurrentSort()
    {
        var cancellation = _runCancellation;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        var task = _sortTask;
        if (task is null || task.IsCompleted)
        {
            cancellation.Dispose();
        }
        else
        {
            _ = task.ContinueWith(
                completedTask =>
                {
                    _ = completedTask.Exception;
                    cancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        _runCancellation = null;
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        RefreshTitle();
    }

    private void IncreaseSpeed()
    {
        _stepsPerFrame = Math.Min(_stepsPerFrame + 4, 128);
        RefreshTitle();
    }

    private void DecreaseSpeed()
    {
        _stepsPerFrame = Math.Max(_stepsPerFrame - 4, 1);
        RefreshTitle();
    }

    private int GetStepSamplingInterval()
    {
        var length = _input.Length;
        if (length <= 128)
        {
            return 1;
        }

        var normalizedName = _algorithmName.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var sortedInput = _input.Zip(_input.Skip(1), (left, right) => left <= right).All(isOrdered => isOrdered);
        var estimatedOperations = normalizedName switch
        {
            "BUBBLESORT" or "COCKTAILSHAKERSORT" or "INSERTIONSORT" when sortedInput => length * 2d,
            "BUBBLESORT" or "COCKTAILSHAKERSORT" or "SELECTIONSORT" or "INSERTIONSORT" => (double)length * length / 2d,
            "SHELLSORT" => length * Math.Log2(length) * 4d,
            "COUNTINGSORT" => length * 3d,
            "RADIXSORT" => length * 16d,
            _ => length * Math.Log2(length) * 3d,
        };

        return Math.Max(1, (int)Math.Ceiling(estimatedOperations / 750d));
    }

    private void HandleButtonClick(Vector2 pointer)
    {
        if (PauseButton.Contains(pointer))
        {
            TogglePause();
        }
        else if (RestartButton.Contains(pointer))
        {
            RestartSort();
        }
        else if (SlowerButton.Contains(pointer))
        {
            DecreaseSpeed();
        }
        else if (FasterButton.Contains(pointer))
        {
            IncreaseSpeed();
        }
        else if (CloseButton.Contains(pointer))
        {
            Close();
        }
    }

    private Vector2 GetPointerPosition()
    {
        var windowSize = ClientSize;
        if (windowSize.X <= 0 || windowSize.Y <= 0)
        {
            return new Vector2(float.NaN, float.NaN);
        }

        var pointer = MouseState.Position;
        return new Vector2(
            (pointer.X / windowSize.X * 2f) - 1f,
            1f - (pointer.Y / windowSize.Y * 2f));
    }

    private void AddControls(List<float> vertices)
    {
        AddRectangle(vertices, -1f, 0.63f, 1f, 1f, new Vector3(0.055f, 0.085f, 0.13f));

        var stateText = _isComplete ? "COMPLETE" : _isPaused ? "PAUSED" : "SORTING";
        var header = $"{_algorithmName} | {stateText} | STEP {_stepCount:N0}";
        AddText(vertices, header.ToUpperInvariant(), 0f, 0.925f, 0.0035f, 0.0095f, new Vector3(0.86f, 0.92f, 0.98f));

        DrawButton(vertices, PauseButton, _isPaused ? "PLAY" : "PAUSE", IsHovered(PauseButton));
        DrawButton(vertices, RestartButton, "RESTART", IsHovered(RestartButton));
        DrawButton(vertices, SlowerButton, "SLOWER", IsHovered(SlowerButton));
        DrawButton(vertices, FasterButton, "FASTER", IsHovered(FasterButton));
        DrawButton(vertices, CloseButton, "CLOSE", IsHovered(CloseButton));
        AddText(vertices, $"SPEED X{_stepsPerFrame}", 0.42f, 0.755f, 0.0035f, 0.0095f, new Vector3(0.67f, 0.78f, 0.9f));
    }

    private void DrawButton(List<float> vertices, ButtonBounds bounds, string text, bool hovered)
    {
        var color = hovered
            ? new Vector3(0.18f, 0.43f, 0.64f)
            : new Vector3(0.11f, 0.23f, 0.36f);
        AddRectangle(vertices, bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, color);
        AddText(vertices, text, (bounds.Left + bounds.Right) / 2f, (bounds.Bottom + bounds.Top) / 2f,
            0.0035f, 0.0095f, new Vector3(0.93f, 0.96f, 1f));
    }

    private bool IsHovered(ButtonBounds bounds) => bounds.Contains(GetPointerPosition());

    private static List<float> CreateBarVertices(int[] values, HashSet<int> highlightedIndices)
    {
        var vertices = new List<float>(values.Length * 30 + 24_000);
        if (values.Length == 0)
        {
            return vertices;
        }

        var minimum = values.Min();
        var maximum = values.Max();
        var range = (double)maximum - minimum;
        var cellWidth = 2f / values.Length;
        var gap = Math.Min(0.004f, cellWidth * 0.12f);

        for (var index = 0; index < values.Length; index++)
        {
            var fraction = range == 0d ? 0.5f : (float)(((double)values[index] - minimum) / range);
            var left = -1f + (index * cellWidth) + gap;
            var right = -1f + ((index + 1) * cellWidth) - gap;
            const float baseline = -0.94f;
            var top = baseline + 0.025f + (fraction * 1.48f);
            var color = highlightedIndices.Contains(index)
                ? new Vector3(1f, 0.58f, 0.22f)
                : new Vector3(0.12f + (fraction * 0.12f), 0.62f + (fraction * 0.2f), 0.9f);

            AddRectangle(vertices, left, baseline, right, top, color);
        }

        return vertices;
    }

    private static void AddText(List<float> vertices, string text, float centerX, float centerY, float cellWidth, float cellHeight, Vector3 color)
    {
        var characterAdvance = cellWidth * 6f;
        var textWidth = text.Length == 0 ? 0f : (text.Length - 1) * characterAdvance + (cellWidth * 5f);
        var startX = centerX - (textWidth / 2f);
        var topY = centerY + (cellHeight * 3.5f);

        for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
        {
            var character = char.ToUpperInvariant(text[characterIndex]);
            if (!Font.TryGetValue(character, out var rows))
            {
                continue;
            }

            for (var row = 0; row < rows.Length; row++)
            {
                for (var column = 0; column < rows[row].Length; column++)
                {
                    if (rows[row][column] != '1')
                    {
                        continue;
                    }

                    var left = startX + (characterIndex * characterAdvance) + (column * cellWidth);
                    var top = topY - (row * cellHeight);
                    AddRectangle(vertices, left, top - (cellHeight * 0.82f), left + (cellWidth * 0.82f), top, color);
                }
            }
        }
    }

    private static void AddRectangle(List<float> vertices, float left, float bottom, float right, float top, Vector3 color)
    {
        AddVertex(vertices, left, bottom, color);
        AddVertex(vertices, left, top, color);
        AddVertex(vertices, right, top, color);
        AddVertex(vertices, left, bottom, color);
        AddVertex(vertices, right, top, color);
        AddVertex(vertices, right, bottom, color);
    }

    private static void AddVertex(List<float> vertices, float x, float y, Vector3 color)
    {
        vertices.Add(x);
        vertices.Add(y);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
    }

    private static Dictionary<char, string[]> CreateFont() => new()
    {
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['C'] = ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01111", "10000", "10000", "10111", "10001", "10001", "01111"],
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['I'] = ["11111", "00100", "00100", "00100", "00100", "00100", "11111"],
        ['J'] = ["00111", "00010", "00010", "00010", "10010", "10010", "01100"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
        ['N'] = ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
        ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
        ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
        ['W'] = ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
        ['X'] = ["10001", "01010", "00100", "00100", "00100", "01010", "10001"],
        ['Y'] = ["10001", "01010", "00100", "00100", "00100", "00100", "00100"],
        ['Z'] = ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "10000", "11110", "00001", "00001", "11110"],
        ['6'] = ["01110", "10000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00001", "01110"],
        [','] = ["00000", "00000", "00000", "00000", "00110", "00100", "01000"],
        ['.'] = ["00000", "00000", "00000", "00000", "00000", "00110", "00110"],
        ['|'] = ["00100", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    };

    private void RefreshTitle()
    {
        var playback = _isPaused ? "Paused" : $"{_stepsPerFrame} steps/frame";
        Title = $"{_algorithmName} | {_status} | {playback} | Space pause, R restart, Left/Right speed, Esc close";
    }

    private readonly record struct ButtonBounds(float Left, float Right, float Bottom, float Top)
    {
        public bool Contains(Vector2 point) =>
            point.X >= Left && point.X <= Right && point.Y >= Bottom && point.Y <= Top;
    }
}


