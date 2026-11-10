# Digital Wallet

A digital wallet API built to study mid and senior .NET development. The focus is concurrency, consistency,
idempotency, testing and observability.

## Architecture

```
Browser ──► ui (Next.js) ──► BFF (DigitalWallet.Bff) ──► API (DigitalWallet.Api) ──► PostgreSQL / RabbitMQ
                                    │
                                    └──► Keycloak (identity; only the BFF talks to it)
```

- **ui/**: Next.js frontend. It only talks to the BFF, on one origin. It never handles tokens.
- **src/DigitalWallet.Bff**: holds the tokens in server-side sessions and gives the browser an `HttpOnly` cookie.
  It also forwards `/api/*` to the API with the session's access token.
- **src/DigitalWallet.Api**: the resource server. It validates the JWT and applies the business rules.
- **infra/keycloak**: realm and client configuration, imported by Keycloak on start-up.

The decisions are recorded in [docs/adr](docs/adr).

## Requirements

- Docker (with Compose)
- .NET SDK 10
- Node.js 20 or newer, with npm

## Run the whole stack (development)

### 1. Configure the secrets

```bash
cp .env.example .env
```

Edit `.env` and set `BFF_CLIENT_SECRET` and `KEYCLOAK_ADMIN_PASSWORD`. The file is git-ignored.

### 2. Start the back end

```bash
docker compose up -d
```

This starts PostgreSQL, RabbitMQ, Keycloak, the API, the BFF and the frontend (`ui`). Wait until `docker compose ps` shows them as running, then open **http://localhost:3000**.

The frontend runs the Next.js development server in its container, with the source folder mounted, so edits in `ui/` reload in the browser.

Keycloak imports the realm from `infra/keycloak/realm-digital-wallet.json` on its first start. The development users are:

| Username | Password | Role |
|---|---|---|
| `alice` | `alice-dev-password` | user |
| `bob` | `bob-dev-password` | user |
| `admin` | `admin-dev-password` | admin |

These passwords are for local development only.

### 3. Run the frontend outside Docker (optional)

The frontend already runs in Compose. To run it on the host instead, stop its container first (`docker compose stop ui`):

```bash
cd ui
cp .env.example .env.local
npm install
npm run dev
```

In development, the Next.js server forwards `/bff/*` and `/api/*` to `BFF_URL` (`http://localhost:8082` on the host, or
`http://bff:8080` inside Compose), so the browser sees a single origin.

### Addresses

| Service | URL |
|---|---|
| Frontend (development) | http://localhost:3000 |
| BFF | http://localhost:8082 (Swagger in Development: `/swagger`) |
| API | http://localhost:8080 (Swagger in Development: `/swagger`) |
| Keycloak admin console | http://localhost:8081 (user `wallet-admin`) |
| RabbitMQ management | http://localhost:15672 (user `wallet`, password `wallet`) |

## Run the API without the frontend

```bash
dotnet run --project src/DigitalWallet.Api
```

The API needs PostgreSQL, RabbitMQ and Keycloak, so `docker compose up -d` is still required.

## Production: the BFF serves the frontend

The frontend builds to static files, and the BFF can serve them from the same origin:

```bash
cd ui
npm run build:static          # writes ui/out
cd ..
Ui__StaticFilesPath=$(pwd)/ui/out dotnet run --project src/DigitalWallet.Bff
```

With `Ui:StaticFilesPath` set, the BFF serves `ui/out` on its own address. The routes `/bff/*`, `/api/*` and `/swagger`
are handled by the BFF first. Every other path returns the frontend's `index.html`.

The frontend is served only when `Ui:StaticFilesPath` is set. It is disabled by default.

## Tests

```bash
dotnet test          # API and BFF, with no Docker and no real database
cd ui
npm test             # frontend tests (Vitest and Testing Library), with fetch mocked
npm run lint && npm run typecheck && npm run build
```

The .NET tests never use PostgreSQL, Docker, Testcontainers or a real Keycloak. They use in-memory fakes and locally
signed tokens.

## Repository layout

| Path | Contents |
|---|---|
| `src/DigitalWallet.Domain` | Entities and business rules |
| `src/DigitalWallet.Application` | Use cases and ports |
| `src/DigitalWallet.Infrastructure` | EF Core, RabbitMQ and other adapters |
| `src/DigitalWallet.Api` | Minimal API endpoints |
| `src/DigitalWallet.Bff` | Sessions, login, and the `/api` proxy |
| `ui` | Next.js frontend |
| `tests/` | Unit, integration and BFF tests |
| `infra/` | Keycloak realm configuration |
| `docs/adr` | Architecture decision records |
