namespace SortingAlgorithms.Models;

public enum SortActionKind : byte
{
    Compare = 1,
    Swap = 2,
    Write = 3,
    AuxiliaryWrite = 4,
    BatchWrite = 5
}

public readonly record struct SortAction(
    SortActionKind Kind,
    int FirstIndex,
    int SecondIndex,
    int PreviousValue,
    int Value);
