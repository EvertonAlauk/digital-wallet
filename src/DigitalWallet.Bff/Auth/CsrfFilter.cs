using Microsoft.AspNetCore.Http;

namespace DigitalWallet.Bff.Auth;

/// <summary>
/// Protects POST endpoints against cross-site requests. A page on another site cannot add a custom header to a request
/// without a CORS preflight, and the BFF allows no cross-origin requests. A JSON body is also required, because a
/// cross-site form cannot send one. Requests that fail this check never reach the endpoint.
/// </summary>
public sealed class CsrfFilter : IEndpointFilter
{
    public const string HeaderName = "X-CSRF";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsPost(request.Method))
        {
            if (!request.Headers.TryGetValue(HeaderName, out var header) || (string?)header != "1")
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "CSRF check failed",
                    detail: "The X-CSRF: 1 header is required.");
            }

            if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    title: "Unsupported media type",
                    detail: "The request body must be JSON.");
            }
        }

        return await next(context);
    }
}
