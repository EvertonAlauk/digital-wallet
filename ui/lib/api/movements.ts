import { apiFetch } from "@/lib/api/client";
import { newIdempotencyKey } from "@/lib/api/idempotency";
import type { OperationAccepted } from "@/lib/accounts/types";

/**
 * Money movements return 202 Accepted. The balance changes later, so the caller follows the operation
 * (see waitForOperation) instead of assuming success.
 */
export function deposit(accountId: string, amount: number): Promise<OperationAccepted> {
  return apiFetch<OperationAccepted>(`/api/accounts/${encodeURIComponent(accountId)}/deposits`, {
    method: "POST",
    body: { amount },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function withdraw(accountId: string, amount: number): Promise<OperationAccepted> {
  return apiFetch<OperationAccepted>(`/api/accounts/${encodeURIComponent(accountId)}/withdrawals`, {
    method: "POST",
    body: { amount },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function transfer(
  sourceAccountId: string,
  destinationAccountId: string,
  amount: number,
): Promise<OperationAccepted> {
  return apiFetch<OperationAccepted>("/api/transfers", {
    method: "POST",
    body: { sourceAccountId, destinationAccountId, amount },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}
