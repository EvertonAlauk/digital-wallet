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
- src/DigitalWallet.Api: endpoints, DI, middleware
- tests/: UnitTests, IntegrationTests

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