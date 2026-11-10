import { apiFetch } from "@/lib/api/client";
import { newIdempotencyKey } from "@/lib/api/idempotency";
import type { Account, AccountList, Statement } from "@/lib/accounts/types";

/** Accounts of the signed-in user, newest first. */
export function listAccounts(): Promise<AccountList> {
  return apiFetch<AccountList>("/api/accounts");
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

/** One page of the statement, newest first. Pass nextCursor back to read the older entries. */
export function getStatement(accountId: string, cursor?: string | null): Promise<Statement> {
  const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : "";
  return apiFetch<Statement>(`/api/accounts/${encodeURIComponent(accountId)}/statement${query}`);
}
