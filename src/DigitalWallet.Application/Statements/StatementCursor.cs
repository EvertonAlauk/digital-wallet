using System.Globalization;
using System.Text;
using DigitalWallet.Application.Common;

namespace DigitalWallet.Application.Statements;

/// <summary>
/// Opaque cursor for statement pages. It wraps the sequence of the last entry of the previous page,
/// encoded as URL-safe base64 so it can be sent in a query string without escaping.
/// </summary>
public static class StatementCursor
{
    public static string Encode(long sequence)
    {
        var bytes = Encoding.UTF8.GetBytes(sequence.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static long Decode(string cursor)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');

            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence > 0)
            {
                return sequence;
            }
        }
        catch (FormatException)
        {
            // Fall through to the validation error below.
        }

        throw new RequestValidationException("cursor", "The cursor is invalid.");
    }
}
