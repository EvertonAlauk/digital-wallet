using System.Security.Cryptography;
using System.Text;
using DigitalWallet.Application.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DigitalWallet.Api.Common.Idempotency;

/// <summary>
/// Replays or protects POST requests that carry an Idempotency-Key, for endpoints marked with
/// <see cref="IdempotencyEndpointExtensions.WithIdempotency{TBuilder}"/>.
/// <list type="bullet">
/// <item>New key: the request is processed. Only a 2xx response is stored, and it is replayed for later requests with the same key and payload.</item>
/// <item>Same key and payload, already completed: the stored response is returned without processing again.</item>
/// <item>Same key, different payload: 422.</item>
/// <item>Same key, still processing: 409, so the request is never processed twice.</item>
/// </list>
/// Failures are not stored: their key is released, so a retry is processed again.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    public async Task InvokeAsync(HttpContext context, IdempotencyService idempotency)
    {
        var isIdempotent = HttpMethods.IsPost(context.Request.Method)
            && context.GetEndpoint()?.Metadata.GetMetadata<IdempotentEndpointMetadata>() is not null;

        if (!isIdempotent || !context.Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            await next(context);
            return;
        }

        var rawKey = headerValues.Count == 1 ? headerValues[0] : null;
        if (string.IsNullOrWhiteSpace(rawKey) || rawKey.Trim().Length > IdempotencyService.MaxKeyLength)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Invalid Idempotency-Key",
                $"The Idempotency-Key header must be a single value of 1 to {IdempotencyService.MaxKeyLength} characters.");
            return;
        }

        var key = rawKey.Trim();
        var cancellationToken = context.RequestAborted;
        var requestHash = await ComputeRequestHashAsync(context.Request, cancellationToken);
        var decision = await idempotency.AcquireAsync(key, requestHash, cancellationToken);

        switch (decision.Kind)
        {
            case IdempotencyDecisionKind.Replay:
                await ReplayAsync(context, decision.Response!, cancellationToken);
                return;

            case IdempotencyDecisionKind.PayloadMismatch:
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status422UnprocessableEntity,
                    "Idempotency-Key reused",
                    "This Idempotency-Key was already used with a different request payload.");
                return;

            case IdempotencyDecisionKind.InProgress:
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Request in progress",
                    "A request with this Idempotency-Key is still being processed, or its outcome is unknown. " +
                    "Retry later or use a new key.");
                return;
        }

        await ProcessAndRecordAsync(context, key, idempotency, cancellationToken);
    }

    private async Task ProcessAndRecordAsync(
        HttpContext context,
        string key,
        IdempotencyService idempotency,
        CancellationToken cancellationToken)
    {
        // Capture the response body so it can be stored, then send it to the client unchanged.
        var originalBody = context.Response.Body;
        using var captured = new MemoryStream();
        context.Response.Body = captured;

        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            await idempotency.ReleaseAsync(key, cancellationToken);
            throw;
        }

        context.Response.Body = originalBody;
        var statusCode = context.Response.StatusCode;
        var body = captured.ToArray();

        if (statusCode is >= 200 and < 300)
        {
            var location = context.Response.Headers.Location.ToString();
            var stored = new StoredResponse(
                statusCode,
                location.Length > 0 ? location : null,
                context.Response.ContentType,
                body);

            await idempotency.CompleteAsync(key, stored, cancellationToken);
        }
        else
        {
            await idempotency.ReleaseAsync(key, cancellationToken);
        }

        if (body.Length > 0)
        {
            await originalBody.WriteAsync(body, cancellationToken);
        }
    }

    private static async Task ReplayAsync(HttpContext context, StoredResponse response, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = response.StatusCode;

        if (response.Location is not null)
        {
            context.Response.Headers.Location = response.Location;
        }

        if (response.ContentType is not null)
        {
            context.Response.ContentType = response.ContentType;
        }

        context.Response.Headers[ReplayedHeaderName] = "true";
        await context.Response.Body.WriteAsync(response.Body, cancellationToken);
    }

    // The hash covers the method, the path, the query and the body, so the same key cannot be reused for another request.
    private static async Task<string> ComputeRequestHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();

        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        request.Body.Position = 0;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method} {request.PathBase}{request.Path}{request.QueryString}\n"));
        hash.AppendData(body.ToArray());
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
        };

        return context.Response.WriteAsJsonAsync<object>(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
