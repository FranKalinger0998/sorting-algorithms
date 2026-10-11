namespace SortingAlgorithms.Services;

public sealed class ArrayGenerator
{
    public int[] Random(int length)
    {
        ValidateLength(length);
        return Enumerable.Range(0, length)
            .Select(_ => System.Random.Shared.Next(-1_000, 1_001))
            .ToArray();
    }

    public int[] Sorted(int length)
    {
        ValidateLength(length);
        return Enumerable.Range(0, length).ToArray();
    }

    public int[] Reversed(int length)
    {
        ValidateLength(length);
        return Enumerable.Range(0, length).Select(index => length - index - 1).ToArray();
    }

    public static bool TryParseCustom(string input, out int[] values)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tokenCount = 0;
        var insideToken = false;
        foreach (var character in input)
        {
            if (IsSeparator(character)) insideToken = false;
            else if (!insideToken)
            {
                tokenCount++;
                insideToken = true;
            }
        }

        if (tokenCount == 0)
        {
            values = [];
            return false;
        }

        values = new int[tokenCount];
        var tokenStart = -1;
        var valueIndex = 0;
        for (var index = 0; index <= input.Length; index++)
        {
            var isSeparator = index == input.Length || IsSeparator(input[index]);
            if (!isSeparator && tokenStart < 0) tokenStart = index;
            else if (isSeparator && tokenStart >= 0)
            {
                if (!int.TryParse(input.AsSpan(tokenStart, index - tokenStart), out values[valueIndex++]))
                {
                    values = [];
                    return false;
                }
                tokenStart = -1;
            }
        }

        return true;
    }

    private static bool IsSeparator(char character) => character is ',' or ';' or ' ' or '\t' or '\r' or '\n';

    private static void ValidateLength(int length)
    {
        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Array length must be at least 1.");
        }
    }
}

