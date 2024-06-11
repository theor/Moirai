namespace Moirai.Parser;

public record struct FilePosition(int Line, int Column) : IComparable<FilePosition>
{
    public int CompareTo(FilePosition other)
    {
        var lineComparison = Line.CompareTo(other.Line);
        if (lineComparison != 0) return lineComparison;
        return Column.CompareTo(other.Column);
    }

    public static bool operator <(FilePosition left, FilePosition right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(FilePosition left, FilePosition right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(FilePosition left, FilePosition right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(FilePosition left, FilePosition right)
    {
        return left.CompareTo(right) >= 0;
    }
}
