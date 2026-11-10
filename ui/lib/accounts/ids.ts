const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** True for an account id. Checked before a request, so a mistyped id is reported without a round trip. */
export function isAccountId(value: string): boolean {
  return UUID_PATTERN.test(value.trim());
}

export const MAX_ACCOUNT_NAME_LENGTH = 60;
