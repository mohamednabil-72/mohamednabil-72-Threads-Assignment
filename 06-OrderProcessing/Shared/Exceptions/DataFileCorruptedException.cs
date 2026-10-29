namespace OrderProcessing.Shared.Exceptions;

public sealed class DataFileCorruptedException : Exception
{
    public string FileName { get; }

    public DataFileCorruptedException(string fileName, string reason, Exception? inner = null)
        : base($"Data file '{fileName}' is corrupted: {reason}", inner)
    {
        FileName = fileName;
    }
}
