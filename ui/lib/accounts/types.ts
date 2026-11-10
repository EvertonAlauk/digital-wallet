/** Account as the API returns it. The balance is a decimal number in the account's currency. */
export interface Account {
  id: string;
  ownerId: string;
  name: string | null;
  status: string;
  balance: number;
  currency: string;
  createdAt: string;
}

/** One page of accounts. totalCount counts every account that matches the filter, across all pages. */
export interface AccountList {
  items: Account[];
  totalCount: number;
  limit: number;
  offset: number;
}

/** Outcome of a request that the API accepts and processes in the background. */
export type OperationStatus = "Accepted" | "Completed" | "Rejected";

export interface Operation {
  id: string;
  accountId: string;
  kind: string;
  status: OperationStatus;
  failureReason: string | null;
  transactionId: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface OperationAccepted {
  operationId: string;
  status: string;
}

/** One ledger entry. A positive amount is a credit to the account, a negative amount is a debit. */
export interface StatementItem {
  transactionId: string;
  type: string;
  amount: number;
  currency: string;
  createdAt: string;
}

export interface Statement {
  items: StatementItem[];
  nextCursor: string | null;
}
