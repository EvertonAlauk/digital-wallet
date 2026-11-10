# ADR 0009: Authentication with an own login screen and Keycloak as identity backend (Direct Access Grants)

## Context

The wallet needs users to sign in, and the API must know who is calling and which account they own. Options:

1. **Keycloak login page with Authorization Code + PKCE** (the standard OAuth 2.1 flow for a browser app). The user signs in on Keycloak's page, and the frontend receives a code that the backend exchanges for tokens.
2. **Own login screen, Keycloak as identity backend through Resource Owner Password Credentials (ROPC, Direct Access Grants).** The frontend shows its own login form, sends the credentials to the BFF, and the BFF exchanges them with Keycloak for tokens.
3. **Own user table and password hashes in the API.** Rejected: the API would own credentials, which is exactly what the identity provider is for.

## Decision

Use option 2.

- The frontend has its own login screen. It never talks to Keycloak directly.
- `DigitalWallet.Bff` is the only component that calls Keycloak. It sends the username and password to the token endpoint with `grant_type=password`, and it refreshes tokens with `grant_type=refresh_token`.
- The Keycloak client `digital-wallet-bff` is confidential, has Direct Access Grants enabled, and has the Standard Flow disabled. Its client secret is not in the code: it comes from the `.env` file (git-ignored), or from user-secrets or environment variables in other environments.
- `DigitalWallet.Api` is a pure resource server. It has no cookies, no session and no user table. It validates each JWT: authority (issuer metadata), issuer, audience (`digital-wallet-api`), signature and lifetime.
- The access token lives 5 minutes. Refresh tokens rotate on each use (`revokeRefreshToken: true`, `refreshTokenMaxReuse: 0`), so a reused refresh token is refused. The SSO session ends after 30 minutes idle.
- Brute-force detection is enabled on the realm.
- Realm roles are `user` and `admin`. The API reads them from `realm_access.roles`.
- The API is secure by default: a fallback policy requires an authenticated user for every endpoint, and only the explicitly anonymous routes (health check and API documentation) are open.
- Ownership uses the `sub` claim (BOLA protection). A customer sees only their own accounts, operations and statements. The `admin` role reads any account through `/api/admin/*`, and admins cannot move money on behalf of others.
- Rate limiting is per user (per IP for anonymous requests).

### Trade-offs accepted

- **ROPC is outside OAuth 2.1.** It is deprecated because the client handles the user's password. The BFF sees the password in transit. Acceptable here: the BFF is the only component that handles it, it runs over the internal network, and the project is for study.
- **No MFA, no SSO between applications, no social login.** Keycloak could provide all three, but the login screen is our own, so none of them is wired in.
- **The password reaches the BFF.** A compromised BFF exposes passwords, not only tokens. Mitigation: the BFF is small, has no other responsibility, and keeps no logs of request bodies.
- **Keycloak cannot show its own consent or login pages.** Every login goes through our screen, which is intended.

### Future direction

When the wallet needs MFA or social login, the BFF should switch to Authorization Code + PKCE, and the ROPC grant should be removed. The API does not change: it validates the same kind of token either way.

## Consequences

- The API has no password handling and no user data beyond the `sub` claim.
- The issuer is fixed by `KC_HOSTNAME`, so a token's `iss` matches the API's configuration whatever address the caller used. The API fetches its signing keys from the internal Keycloak address.
- Tests do not need Keycloak. They sign tokens with a test key, and the same validation rules apply.

## Verification

Done against the real Keycloak image (see the implementation notes and the integration tests):

- The client secret from `.env` is accepted, and a wrong secret is refused with `unauthorized_client`.
- Tokens carry `iss`, `aud = digital-wallet-api`, `sub`, a 300-second lifetime, and the right `realm_access.roles` (`admin` gets `admin`, `alice` gets only `user`).
- A refresh token is rotated, and the old one is refused with `invalid_grant`.

Live run against `docker compose` (Keycloak 26.4, API, BFF), with real tokens from the BFF:

- Without a token, the API returns 401 with `application/problem+json`.
- Alice creates an account (201), and its `ownerId` equals alice's `sub` claim.
- Alice deposits 100 (202). After background processing the balance is 100.00 and the operation is `Completed`.
- Bob gets 404 when he reads or deposits into alice's account. He also gets 404 when he reads alice's operation. Alice gets 200 for the same operation.
- Alice gets 403 on `/api/admin/accounts/{id}`. The admin gets 200 on the same route.
- The admin gets 404 when depositing into alice's account, so the admin role does not move other people's money.
- The BFF rotates the refresh token, and reuse of the old one returns 401.
- A token with `alg: none` or a foreign signature returns 401.

Automated tests (no Keycloak, no Docker): the BFF tests use a fake Keycloak handler, and the API tests sign tokens with a test key. Together they cover 401 without a token, 403 without the role, BOLA, admin reads, wrong audience, wrong issuer, expired token, untrusted signing key, and a check that every route requires authentication except `/health` and the OpenAPI document.

## Implementation notes

- **API token validation** (`src/DigitalWallet.Api/Auth/AuthenticationSetup.cs`): `ValidIssuer` is `Auth:Issuer`, falling back to `Auth:Authority`. Audience must be `digital-wallet-api`. Lifetime is required, with 30 seconds of clock skew. The algorithm is RS256 only. `MapInboundClaims` is off, so claim names are the raw Keycloak names.
- **Roles**: `RealmRoleClaimsTransformation` reads `realm_access.roles` and adds them as `ClaimTypes.Role` once per request.
- **Admin**: only `/api/admin/*` requires the `Admin` policy. It exposes reads only. Money movements stay under the customer routes, which check ownership, so an admin gets 404 there.
- **Ownership**: `AccountOwnership` compares the account owner with the `sub` claim and throws `AccountNotFoundException` for non-owners. That maps to 404, so the API does not reveal which accounts exist.
- **Rate limit**: fixed window per user (`sub`), or per IP for anonymous requests. The default is 100 requests per window, set by `RateLimit:PermitLimit`. The BFF login and refresh endpoints have their own limit of 10 requests per minute.
- **Issuer and backchannel**: `KC_HOSTNAME` fixes the issuer to `http://localhost:8081/realms/digital-wallet`, so the `iss` claim matches the API configuration. `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true` makes the discovery document use the request host for `jwks_uri` and the token endpoint. Without it, the API container tried to fetch keys from `localhost:8081`, which does not reach Keycloak inside the Docker network, and every token was refused with 401.
- **Realm fixtures**: the passwords `alice-dev-password`, `bob-dev-password` and `admin-dev-password` are in `infra/keycloak/realm-digital-wallet.json` for local development only. They must not be used in any other environment.
- **Secrets**: the client secret is in `.env`, which is git-ignored. `.env.example` has placeholders only.

## Production notes

- Serve every endpoint over HTTPS. The local setup uses HTTP and `RequireHttpsMetadata=false`, which is for development only.
- Replace the dev passwords and the realm import with a managed Keycloak configuration. Use a secret store for `BFF_CLIENT_SECRET`.
- Keep the BFF logs free of request bodies, because they contain passwords.
- Brute-force detection is enabled on the realm. It was not exercised in this verification run, so a lockout after repeated wrong passwords is still unverified.

## Update: how the BFF delivers the session

The first BFF returned the tokens to the frontend. [ADR 0010](0010-bff-sessions-and-same-origin-frontend.md) replaces that: the tokens now stay in a server-side session, the browser gets only an opaque `__Host-wallet` cookie, and the BFF endpoints are `/bff/login`, `/bff/session` and `/bff/logout`. The decisions in this ADR about Keycloak, the grant type, the API and the trade-offs still apply.
