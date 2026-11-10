import { apiFetch } from "@/lib/api/client";
import type { Operation } from "@/lib/accounts/types";

export function getOperation(id: string): Promise<Operation> {
  return apiFetch<Operation>(`/api/operations/${encodeURIComponent(id)}`);
}

export interface WaitOptions {
  intervalMs?: number;
  maxAttempts?: number;
}

/**
 * Polls an operation until it leaves the Accepted state. Returns the final operation, or the last one seen when the
 * attempts run out (the operation may still complete later).
 */
export async function waitForOperation(id: string, options: WaitOptions = {}): Promise<Operation> {
  const { intervalMs = 500, maxAttempts = 40 } = options;

  let operation = await getOperation(id);
  for (let attempt = 1; operation.status === "Accepted" && attempt < maxAttempts; attempt++) {
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
    operation = await getOperation(id);
  }

  return operation;
}
