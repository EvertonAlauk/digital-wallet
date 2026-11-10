# ADR 0010: BFF sessions with server-side tokens and an optional same-origin frontend

## Context

[ADR 0009](0009-authentication-keycloak-direct-grant.md) makes the BFF the only component that talks to Keycloak. The first BFF implementation still returned the access and refresh tokens to the frontend, which had to keep them and send them to the API. A token stored in the browser can be read by any script that runs on the page, so one cross-site scripting bug is enough to steal it. A stolen refresh token keeps working until it is rotated.

## Decision

- **Tokens stay on the server.** The BFF keeps the access and refresh tokens in a session ticket. The browser receives only the cookie `__Host-wallet` (HttpOnly, Secure, SameSite=Strict, Path=/). The cookie holds an encrypted reference to the session, not a token.
- **Session store behind `ITicketStore`.** The first implementation is in memory (`InMemoryTicketStore`). A Redis store replaces it without changing the endpoints.
- **New session on every login.** The session that the browser had before the login is ended, and the new login gets a new key. The cookie handler reuses the store key of the session a request arrives with, so the BFF rotates the key itself: a middleware runs before authentication, ends the previous session and hides its cookie from the handler.
- **Endpoints:**
  - `POST /bff/login` takes `{ username, password }`, runs the password grant with `scope=openid`, and returns `200 { user: { sub, username, name, email, roles } }`. Wrong credentials and locked accounts both get `401` with the same message, `Usuário ou senha inválidos`.
  - `GET /bff/session` returns the user when the session is valid, and `401` otherwise.
  - `POST /bff/logout` ends the Keycloak session, removes the server-side session and the cookie, and returns `204`. It is idempotent.
- **CSRF.** Every POST requires the header `X-CSRF: 1` and a JSON body. A page on another site cannot send a custom header without a CORS preflight, and the BFF allows no cross-origin requests.
- **Rate limits per client address.** `/bff/login` allows 5 requests per minute (`RateLimit:LoginPermitLimit`), and the other `/bff` endpoints allow 60 (`RateLimit:GeneralPermitLimit`). Rejections return ProblemDetails with `429`.
- **Refresh with single-flight.** When the access token expires within 60 seconds, the BFF refreshes it. Concurrent requests of the same session share one refresh, through a gate per session. Keycloak rotates the refresh token on each refresh, so a second refresh with the old token would be refused and end the session. A failed refresh ends the session, and the caller gets `401`.
- **Identity behind `IIdentityService`.** `KeycloakIdentityService` is the only implementation. Its HTTP client has a 10-second timeout. A timeout, a connection error or a malformed response is reported as "unavailable", so Keycloak's error details never reach the response.
- **Logging.** The BFF writes no logs of its own. Passwords and tokens are never logged, and `IdentityTokens` prints only its expiry, so an accidental `ToString` cannot leak a token.
- **Same-origin frontend (optional).** The BFF serves the frontend build from `Ui:StaticFilesPath`, which is empty and disabled by default. With the frontend on the BFF's origin, the cookie stays first-party and no CORS configuration is needed. The BFF routes are matched before the fallback to `index.html`.
- **API proxy (`/api/*`) with YARP** (`Yarp.ReverseProxy` 2.3.0, approved). Each request needs a valid session and the `X-CSRF: 1` header. The proxy takes the access token from `SessionTokenProvider`, refreshing it first when needed, removes the session cookie and any `Authorization` header the browser sent, and adds `Authorization: Bearer <access token>`. When the session has ended, it returns `401` and deletes the cookie. Routes: `/api/admin/*` keeps its `/api` prefix, because the API serves admin routes under it. Every other `/api/*` route drops the `/api` prefix, because the API serves them at the root (`/accounts`, `/transfers`, ...). The API address comes from `Api:BaseUrl`.

## Consequences

- The frontend never sees a token, and a script on the page cannot read one.
- The refresh rules are in one place, `SessionTokenProvider`, and are tested without HTTP.
- The BFF does not log credentials, so a log leak does not expose them.

### Limits of a single instance

The BFF as built runs in one process. Scaling it out needs changes first:

- **Sessions live in process memory.** A restart signs every user out. Several instances do not share sessions. A Redis `ITicketStore` fixes both.
- **The refresh gate is in process.** With two instances, both can refresh the same session at the same moment. The second refresh uses a refresh token that Keycloak has already rotated, so Keycloak refuses it and the session ends. Before scaling out, the refresh needs a distributed lock per session (for example, Redis `SET key NX PX`), or the load balancer must send each session to one instance (sticky sessions by cookie).
- **Data Protection keys encrypt the cookie.** Each instance needs the same keys, persisted in Redis or a shared volume. Without that, a cookie issued by one instance is invalid on another.
- **Rate limits are counted per instance.** A limiter in Redis is needed for the limit to hold across instances.
- **Gates of abandoned sessions stay in memory.** A gate is removed when its session ends. A session that expires without being read again keeps its gate until the process restarts. This is small, but it grows with the number of sessions.

## Verification

The BFF test suite (61 tests) runs without Keycloak and without Docker. It uses a fake identity service, and a fake HTTP handler for the Keycloak client. It covers:

- login with success and failure, with no token in the body, and the cookie attributes;
- a new session on each login, and an old cookie that no longer works;
- `/bff/session` with and without a valid cookie, and with a forged cookie;
- idempotent logout, and the call to Keycloak;
- missing CSRF header and non-JSON body;
- login rate limit;
- refresh close to expiry, a refused refresh, a refresh with the identity provider down, and twenty concurrent requests that cause one refresh;
- a logout that runs during a refresh and is not undone by it;
- the frontend served from the configured folder, and disabled by default.

The proxy tests (11) use a stub API in memory, connected to the YARP forwarder. They cover:

- the Bearer token of the session reaching the API, and the cookie and the browser's `Authorization` header not reaching it;
- the path rewrite (`/api/accounts` to `/accounts`), the kept `/api/admin` prefix, and query strings;
- `401` without a session or with a forged cookie, and `403` without the CSRF header, each with nothing forwarded;
- a token close to expiry, refreshed before forwarding;
- a refused refresh, which returns `401`, deletes the cookie and ends the session;
- ten concurrent API requests of one session close to expiry, which cause one refresh.
