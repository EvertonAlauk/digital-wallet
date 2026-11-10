# ADR 0001: Layered solution structure

## Context

The digital wallet must separate business rules, use cases, infrastructure (PostgreSQL, messaging) and
the HTTP layer, so that consistency rules (ledger, balance, idempotency) can be tested without depending
on a database or HTTP.

## Decision

The solution is split into four projects under `src/` and two under `tests/`:

| Project | References | Responsibility |
|---|---|---|
| `DigitalWallet.Domain` | none | Entities and business rules |
| `DigitalWallet.Application` | Domain | Use cases and interfaces (ports) |
| `DigitalWallet.Infrastructure` | Application | EF Core, messaging, external services |
| `DigitalWallet.Api` | Application, Infrastructure | Endpoints, DI, middleware |
| `DigitalWallet.UnitTests` | Domain, Application | Unit tests |

Dependencies always point inward: Domain does not reference any other project.

Shared build properties (`Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`) are centralized in
`Directory.Build.props`.

## Consequences

- Domain rules are testable without infrastructure.
- Application defines the interfaces, and Infrastructure implements them (dependency inversion).
- More project files than a monolithic solution, which is accepted for the study goal.
- The Api depends on Infrastructure only to register DI; logic must not leak into it.
