namespace TanqiuTft.Library;

public sealed class LineupLibraryException : Exception
{
    public LineupLibraryException(string message)
        : base(message)
    {
    }

    public LineupLibraryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
