namespace DigitalWallet.Application.Common;

/// <summary>Input does not satisfy the request contract (format, range, missing values). Not a business rule.</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(string field, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }

    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more request values are invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
