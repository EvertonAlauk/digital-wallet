# ADR 0011: User registration through the BFF with the Keycloak Admin API

## Context

Users need to create their own account. [ADR 0009](0009-authentication-keycloak-direct-grant.md) keeps the login screen in
our own frontend, and Keycloak only validates credentials. Keycloak can also show its own registration page, but that page
would sit outside our screens, and it would not share the BFF's rules and error messages.

## Decision

- **The frontend creates accounts through `POST /bff/register`**, on our own registration page. The BFF checks the form,
  then creates the user in Keycloak through its **Admin REST API**.
- **A dedicated client, `digital-wallet-registrar`, does the creation.** It uses client credentials (service account).
  Its service account has only `manage-users` and `view-users` from `realm-management`. The BFF's login client
  (`digital-wallet-bff`) does not gain these permissions.
- **Keycloak's own registration page stays off** (`registrationAllowed: false`).
- **The account is created enabled, with the `user` realm role.** If the role cannot be granted, the account is
  deleted, so a half-created user never remains.
- **The email is the username**, stored in lower case. The realm already sets `editUsernameAllowed: false`.
- **The registration does not sign the user in.** The frontend sends them to the login page.
- **Rate limit:** `POST /bff/register` shares the login's limit (5 per minute per client). It requires `X-CSRF: 1`.
- **The application still stores no passwords.** The password goes to Keycloak in the request and is not logged or returned.

### Trade-offs accepted

- **Email verification is off.** There is no mail server in this project, so `emailVerified` is `false` and no
  verification step is required. Anyone can register an address they do not own. This is acceptable for a study
  project; a production system needs a verification step before the account is used for money.
- **A 409 reveals that an email is registered.** Hiding it would need the same response for both cases, and then the
  user would not learn that they should sign in. We chose the clear message, and the rate limit limits probing.
- **The registrar secret is a powerful credential.** Anyone with it can create users. It lives only in the git-ignored
  `.env` (`REGISTRAR_CLIENT_SECRET`) and in the BFF's environment, never in the repository.
- **Password rules are a floor in the BFF** (8 to 128 characters). Keycloak's own policy can be stricter; its refusal
  is shown as "the identity provider did not accept these details".

## Consequences

- The BFF gains one more Keycloak client and one Admin API call sequence: token, create user, read role, grant role.
  If a step after the creation fails, the BFF removes the new user.
- The realm import creates the registrar client and its service account. An existing Keycloak must be recreated
  (`docker compose up -d --force-recreate keycloak`) to import them, because a realm is imported only once.
- Tests use a fake Admin API handler. No Keycloak is needed.

## Verification

The BFF test suite covers the rules, the request sequence, the role grant, the rollback when the role fails, each
Keycloak failure mode (token refused, permission lacking, email taken, password refused, unreachable, timeout), the
endpoint responses, CSRF, the content type, and that the password is never returned or printed.
