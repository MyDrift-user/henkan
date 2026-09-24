namespace Henkan.Core.Expressions;

/// <summary>Raised when a condition expression cannot be parsed or evaluated.</summary>
public sealed class ExpressionException : Exception
{
    public ExpressionException(string message)
        : base(message)
    {
    }

    public ExpressionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
