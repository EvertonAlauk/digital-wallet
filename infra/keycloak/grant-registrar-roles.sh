#!/usr/bin/env bash
# Gives the registrar's service account the realm-management roles it needs to create users (manage-users, view-users and view-realm).
#
# The realm import creates the service account but does not apply its client roles, so this script does it through the
# Admin API. It is idempotent: running it again changes nothing. Run it once after Keycloak has started:
#
#   bash infra/keycloak/grant-registrar-roles.sh
#
# It reads KEYCLOAK_ADMIN_PASSWORD from the .env file in the repository root (or the environment) and never prints it.
set -euo pipefail

KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8081}"
REALM="digital-wallet"
SERVICE_ACCOUNT="service-account-digital-wallet-registrar"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

if [ -z "${KEYCLOAK_ADMIN_PASSWORD:-}" ] && [ -f "$ROOT/.env" ]; then
  KEYCLOAK_ADMIN_PASSWORD="$(grep '^KEYCLOAK_ADMIN_PASSWORD=' "$ROOT/.env" | cut -d= -f2-)"
fi
: "${KEYCLOAK_ADMIN_PASSWORD:?Set KEYCLOAK_ADMIN_PASSWORD or add it to .env}"

# Reads one string field from a JSON response, without a JSON tool.
field() { sed -n "s/.*\"$2\":\"\([^\"]*\)\".*/\1/p" <<<"$1" | head -n 1; }

TOKEN_JSON="$(curl -s -X POST "$KEYCLOAK_URL/realms/master/protocol/openid-connect/token" \
  -d grant_type=password -d client_id=admin-cli -d username=wallet-admin \
  --data-urlencode "password=$KEYCLOAK_ADMIN_PASSWORD")"
TOKEN="$(field "$TOKEN_JSON" access_token)"
if [ -z "$TOKEN" ]; then
  echo "Could not get an admin token. Check KEYCLOAK_ADMIN_PASSWORD and that Keycloak is running." >&2
  exit 1
fi
AUTH=(-H "Authorization: Bearer $TOKEN")
API="$KEYCLOAK_URL/admin/realms/$REALM"

USER_JSON="$(curl -s "${AUTH[@]}" "$API/users?username=$SERVICE_ACCOUNT&exact=true")"
USER_ID="$(field "$USER_JSON" id)"
if [ -z "$USER_ID" ]; then
  echo "Service account $SERVICE_ACCOUNT not found. Is the realm imported?" >&2
  exit 1
fi

RM_JSON="$(curl -s "${AUTH[@]}" "$API/clients?clientId=realm-management")"
RM_ID="$(field "$RM_JSON" id)"

ROLES="["
FIRST=1
for ROLE in manage-users view-users view-realm; do
  ROLE_JSON="$(curl -s "${AUTH[@]}" "$API/clients/$RM_ID/roles/$ROLE")"
  ROLE_ID="$(field "$ROLE_JSON" id)"
  if [ -z "$ROLE_ID" ]; then
    echo "realm-management role $ROLE not found." >&2
    exit 1
  fi
  [ $FIRST -eq 0 ] && ROLES+=","
  ROLES+="{\"id\":\"$ROLE_ID\",\"name\":\"$ROLE\"}"
  FIRST=0
done
ROLES+="]"

STATUS="$(curl -s -o /dev/null -w '%{http_code}' -X POST "${AUTH[@]}" -H "Content-Type: application/json" \
  -d "$ROLES" "$API/users/$USER_ID/role-mappings/clients/$RM_ID")"
if [ "$STATUS" != "204" ]; then
  echo "Granting the roles failed with HTTP $STATUS." >&2
  exit 1
fi

# The client must carry its full scope, or its tokens leave the roles out. Only the service account's own roles
# (manage-users, view-users and view-realm) are added, so this does not widen what the client can do.
CLIENT_JSON="$(curl -s "${AUTH[@]}" "$API/clients?clientId=digital-wallet-registrar")"
CLIENT_ID="$(field "$CLIENT_JSON" id)"
if [ -n "$CLIENT_ID" ] && grep -q '"fullScopeAllowed":false' <<<"$CLIENT_JSON"; then
  UPDATED="${CLIENT_JSON/\"fullScopeAllowed\":false/\"fullScopeAllowed\":true}"
  UPDATED="${UPDATED#[}"
  UPDATED="${UPDATED%]}"
  STATUS="$(curl -s -o /dev/null -w '%{http_code}' -X PUT "${AUTH[@]}" -H "Content-Type: application/json" \
    -d "$UPDATED" "$API/clients/$CLIENT_ID")"
  [ "$STATUS" = "204" ] || { echo "Updating the client failed with HTTP $STATUS." >&2; exit 1; }
fi

echo "Granted manage-users, view-users and view-realm to $SERVICE_ACCOUNT."
