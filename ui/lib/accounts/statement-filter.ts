/** Kinds of ledger entry the statement can show. Matches the API's transaction types. */
export const STATEMENT_TYPES = ["Deposit", "Withdrawal", "Transfer", "Reversal"] as const;

/**
 * Filters applied to the statement. An empty field does not restrict the result. Dates are calendar days (yyyy-MM-dd)
 * and both ends are included.
 */
export interface StatementFilter {
  type: string;
  from: string;
  to: string;
}

export const NO_STATEMENT_FILTER: StatementFilter = { type: "", from: "", to: "" };

export function hasStatementFilter(filter: StatementFilter): boolean {
  return filter.type !== "" || filter.from !== "" || filter.to !== "";
}

/** Returns the reason the filter cannot be used, or null when it can. */
export function statementFilterError(filter: StatementFilter): string | null {
  if (filter.from !== "" && filter.to !== "" && filter.from > filter.to) {
    return "The start date cannot be after the end date.";
  }

  return null;
}

/** Query string for the statement request: filters first, then the cursor. Empty when nothing applies. */
export function statementQuery(filter: StatementFilter, cursor?: string | null): string {
  const params = new URLSearchParams();
  if (filter.type !== "") {
    params.set("type", filter.type);
  }

  if (filter.from !== "") {
    params.set("from", filter.from);
  }

  if (filter.to !== "") {
    params.set("to", filter.to);
  }

  if (cursor) {
    params.set("cursor", cursor);
  }

  const text = params.toString();
  return text === "" ? "" : `?${text}`;
}
