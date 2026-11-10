# Digital Wallet

Digital wallet API built to study mid/senior-level .NET development.
Focus: concurrency, consistency, idempotency, testing and observability.

Any code, comment, files names should to be in english, both backend and frontend (src and ui folders)

## Stack
- .NET (latest installed version), ASP.NET Core Minimal APIs
- PostgreSQL + EF Core (Npgsql) — used by the application only
- xUnit, FluentAssertions
- Docker Compose for local dependencies

## Architecture
- src/DigitalWallet.Domain: entities and business rules, NO external dependencies
- src/DigitalWallet.Application: use cases and ports (repository, unit of work, idempotency store, outbox, clock interfaces)
- src/DigitalWallet.Infrastructure: EF Core implementations of the ports, messaging
- src/DigitalWallet.Api: endpoints, DI, middleware. JWT resource server, all /api/* routes require a Bearer token
- src/DigitalWallet.Bff: the BFF and the only entry point for the browser — login/logout/session, server-side session, token refresh, YARP proxy of /api/* to the API
- ui/: Next.js (App Router, TypeScript) frontend
- tests/: UnitTests, IntegrationTests

## Authentication
- Keycloak is the identity backend only; the login screen is our own (ui/).
  The BFF uses Direct Access Grants (ROPC); Standard Flow is disabled.
  ROPC is an accepted decision for this study project (see ADR 0009). Authorization Code + PKCE is
  the planned path when MFA or social login is needed; it is not implemented yet.
- The application never stores passwords and has no users table for credentials.
  Account ownership uses the Keycloak "sub" claim.
- Tokens live ONLY in the BFF (server-side session store). The browser only
  holds an opaque HttpOnly session cookie. The ui never handles tokens.
- BFF public routes: POST /bff/login, GET /bff/session, POST /bff/logout.
  /api/* in the BFF requires a session and is proxied to the API with a Bearer token.
- Token refresh happens in the BFF, single-flight per session.
- All POSTs and /api/* calls require the header "X-CSRF: 1".
- Tests never use Keycloak: use fakes of IIdentityService and locally generated
  test tokens.

## Testing strategy
- Tests NEVER use PostgreSQL, Docker, Testcontainers or a real database.
- UnitTests: Domain and Application use cases, using in-memory fakes of the ports.

## Non-negotiable business rules
- Money is always `decimal`, never double/float. Default currency: BRL.
- The LEDGER (entries) is the source of truth. Every movement creates double-entry records (debit + credit that sum to zero).
- The balance on Account is a cache, updated in the SAME transaction as the ledger entries and protected by a concurrency token.
- Ledger entries are immutable: never UPDATE/DELETE. Corrections = reversals.
- Balance can never go negative.

## Conventions
- Nullable enabled, warnings as errors.
- Async for all I/O, always passing the CancellationToken through.
- API errors use the ProblemDetails format.
- All new code comes with tests.
- Infrastructure exceptions (e.g. DbUpdateConcurrencyException) are translated into Application exceptions (e.g. ConcurrencyConflictException); use cases never depend on EF Core types.
- Do not add NuGet packages without asking me first.
- When making a relevant architectural decision, record an ADR in docs/adr/, always in English.

## Commands
- Build: `dotnet build`
- Tests: `dotnet test` (no Docker required)
- Dependencies: `docker compose up -d`
- Run API: `dotnet run --project src/DigitalWallet.Api`