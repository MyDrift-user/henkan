namespace Henkan.Core.Templating;

/// <summary>Raised when an argument or path template is malformed.</summary>
public sealed class TemplateException : Exception
{
    public TemplateException(string message)
        : base(message)
    {
    }

    public TemplateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
