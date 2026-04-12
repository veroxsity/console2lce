namespace Console2Lce;

public sealed class SavegameDatDecompressionFailedException : Console2LceException
{
    public SavegameDatDecompressionFailedException(string message)
        : base(message)
    {
    }

    public SavegameDatDecompressionFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
