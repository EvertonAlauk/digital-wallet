using DigitalWallet.Application.Common;
using DigitalWallet.Application.Exceptions;
using DigitalWallet.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DigitalWallet.Api.Common;

/// <summary>
/// Maps application and domain exceptions to ProblemDetails responses. Unknown exceptions are not handled here,
/// so they still produce a 500 through the default pipeline.
/// </summary>
internal sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails details;

        switch (exception)
        {
            case AccountNotFoundException:
                details = Problem(StatusCodes.Status404NotFound, "Account not found", exception.Message);
                break;

            case ConcurrencyConflictException:
                details = Problem(
                    StatusCodes.Status409Conflict,
                    "Concurrent modification",
                    "The resource was changed by another request. Reload it and try again.");
                break;

            case DomainException:
                details = Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violation", exception.Message);
                break;

            case RequestValidationException validation:
                details = new HttpValidationProblemDetails(new Dictionary<string, string[]>(validation.Errors))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid request",
                    Detail = validation.Message,
                };
                break;

            // Malformed body or parameter (bad JSON, unparseable GUID, wrong type): a client error, not a server fault.
            case BadHttpRequestException badRequest:
                details = Problem(StatusCodes.Status400BadRequest, "Invalid request", badRequest.Message);
                break;

            default:
                return false;
        }

        // Written directly: the default ProblemDetails writer does not cover every exception that reaches here
        // (for example, malformed request bodies), and every error must use the same format.
        httpContext.Response.StatusCode = details.Status!.Value;
        // <object> makes the runtime type serialize, so HttpValidationProblemDetails keeps its "errors".
        await httpContext.Response.WriteAsJsonAsync<object>(
            details,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);

        return true;
    }

    private static ProblemDetails Problem(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };
}
