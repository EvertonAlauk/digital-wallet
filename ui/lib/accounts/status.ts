import type { Account } from "@/lib/accounts/types";

/**
 * The API stores an inactive account as "Blocked": it refuses every movement, while its balance and statement stay
 * readable. The user sees it as "Inactive".
 */
export function isInactive(account: Pick<Account, "status">): boolean {
  return account.status === "Blocked";
}

export function accountStatusLabel(account: Pick<Account, "status">): string {
  return isInactive(account) ? "Inactive" : "Active";
}
