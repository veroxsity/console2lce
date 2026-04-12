namespace Console2Lce;

public abstract class Console2LceException : Exception
{
    protected Console2LceException(string message)
        : base(message)
    {
    }

    protected Console2LceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
