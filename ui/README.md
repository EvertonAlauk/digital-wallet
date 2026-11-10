# Digital Wallet frontend

Next.js (App Router, TypeScript, ESLint). The frontend talks only to the BFF (`src/DigitalWallet.Bff`), on one origin.
It never reads, stores or sends a token: the session is an `HttpOnly` cookie that the BFF manages.

## Scripts

| Command | What it does |
|---|---|
| `npm run dev` | Development server on http://localhost:3000. `/bff/*` and `/api/*` are forwarded to `BFF_URL`. |
| `npm run build` | Production build with the Next.js server. |
| `npm run build:static` | Static export to `out/`, which the BFF serves in production. |
| `npm run lint` | ESLint. |
| `npm run typecheck` | TypeScript without emitting files. |

## Configuration

Copy `.env.example` to `.env.local`:

- `BFF_URL`: address of the BFF (default `http://localhost:8082`). It is read on the server only.

## Structure

- `app/login`: sign-in page.
- `app/(protected)`: pages that need a session, with the client-side route guard and the header with the sign-out button.
- `components/auth/auth-provider.tsx`: `AuthProvider` and `useAuth()`.
- `lib/api/client.ts`: the HTTP client. Sends `X-CSRF: 1` and `credentials: "same-origin"`. On 401, it sends the browser to the login page.
- `lib/auth`: session calls, the `returnUrl` check, login rules, and the cross-tab channel.

Do not add `app/api`: that path belongs to the BFF proxy.

## Security note

The route guard only improves the experience. The real protection is in the BFF and the API, which reject every request
without a valid session.
