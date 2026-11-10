using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DigitalWallet.Api.Auth;

/// <summary>
/// Describes the authentication in the OpenAPI document, so Swagger UI shows the Authorize button and marks the
/// operations that need a token. The rules mirror the runtime ones: every endpoint requires a user unless it allows
/// anonymous access (see <see cref="AuthenticationSetup"/>).
/// </summary>
public static class OpenApiSecuritySetup
{
    public const string BearerSchemeName = "Bearer";

    public static OpenApiOptions AddBearerAuthentication(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token issued by Keycloak. Get it from POST /auth/login on the BFF, then paste it here (without the word 'Bearer').",
            };

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, cancellationToken) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                return Task.CompletedTask;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                // The document is needed so the reference is written by name ("Bearer"), not as an empty object.
                [new OpenApiSecuritySchemeReference(BearerSchemeName, context.Document)] = [],
            });

            operation.Responses ??= new OpenApiResponses();
            operation.Responses["401"] = new OpenApiResponse { Description = "The token is missing, expired or invalid." };

            // Endpoints with a role or policy (for example the admin routes) can also be refused with 403.
            var requiresRole = metadata.OfType<IAuthorizeData>()
                .Any(data => !string.IsNullOrEmpty(data.Policy) || !string.IsNullOrEmpty(data.Roles));
            if (requiresRole)
            {
                operation.Responses["403"] = new OpenApiResponse { Description = "The user does not have the required role." };
            }

            return Task.CompletedTask;
        });

        return options;
    }
}
