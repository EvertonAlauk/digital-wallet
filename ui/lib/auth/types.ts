/** The user as the BFF describes it. It contains no token. */
export interface SessionUser {
  sub: string;
  username: string;
  name: string | null;
  email: string | null;
  roles: string[];
}

/** Name shown to the user: the full name when the identity provider has it, otherwise the username. */
export function displayName(user: SessionUser): string {
  return user.name?.trim() || user.username;
}
