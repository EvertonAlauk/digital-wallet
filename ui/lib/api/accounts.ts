import { apiFetch } from "@/lib/api/client";
import { newIdempotencyKey } from "@/lib/api/idempotency";
import type { Account, AccountList, Statement } from "@/lib/accounts/types";
import { NO_STATEMENT_FILTER, statementQuery, type StatementFilter } from "@/lib/accounts/statement-filter";

export type AccountFilter = "active" | "inactive";

/**
 * One page of the user's accounts, newest first. Without a filter, all accounts are listed.
 * offset is the number of accounts to skip, so page n starts at n * pageSize.
 */
export function listAccounts(filter: AccountFilter | undefined, offset: number, limit: number): Promise<AccountList> {
  const params = new URLSearchParams({ limit: String(limit), offset: String(offset) });
  if (filter) {
    params.set("status", filter);
  }

  return apiFetch<AccountList>(`/api/accounts?${params.toString()}`);
}

export interface CurrencyTotal {
  currency: string;
  amount: number;
}

/** Sum of the balances of all the user's accounts (active and inactive), per currency, and the account counts. */
export interface AccountSummary {
  totals: CurrencyTotal[];
  activeCount: number;
  inactiveCount: number;
}

export function getAccountSummary(): Promise<AccountSummary> {
  return apiFetch<AccountSummary>("/api/accounts/summary");
}

/** One account. The API answers 404 when the account does not exist or belongs to someone else. */
export function getAccount(id: string): Promise<Account> {
  return apiFetch<Account>(`/api/accounts/${encodeURIComponent(id)}`);
}

/** Opens an account for the signed-in user. The name is optional. */
export function createAccount(name: string | null): Promise<Account> {
  return apiFetch<Account>("/api/accounts", {
    method: "POST",
    body: name === null ? {} : { name },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** Changes the name. A null or blank name clears it. */
export function renameAccount(id: string, name: string | null): Promise<Account> {
  return apiFetch<Account>(`/api/accounts/${encodeURIComponent(id)}`, {
    method: "PATCH",
    body: { name },
  });
}

/** Stops movements on the account. Its balance and statement stay available. */
export function deactivateAccount(id: string): Promise<Account> {
  return apiFetch<Account>(`/api/accounts/${encodeURIComponent(id)}/deactivate`, { method: "POST" });
}

export function activateAccount(id: string): Promise<Account> {
  return apiFetch<Account>(`/api/accounts/${encodeURIComponent(id)}/activate`, { method: "POST" });
}

/**
 * One page of the statement, newest first. Pass nextCursor back to read the older entries. The filters apply to every
 * page, so the same filter must be sent with the cursor.
 */
export function getStatement(
  accountId: string,
  options: { cursor?: string | null; filter?: StatementFilter } = {},
): Promise<Statement> {
  const query = statementQuery(options.filter ?? NO_STATEMENT_FILTER, options.cursor);
  return apiFetch<Statement>(`/api/accounts/${encodeURIComponent(accountId)}/statement${query}`);
}
