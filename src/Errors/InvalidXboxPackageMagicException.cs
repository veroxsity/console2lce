namespace Console2Lce;

public sealed class InvalidXboxPackageMagicException : Console2LceException
{
    public InvalidXboxPackageMagicException()
        : base("Input does not start with a supported Xbox package magic.")
    {
    }
}
