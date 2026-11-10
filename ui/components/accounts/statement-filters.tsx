"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api/client";
import {
  NO_STATEMENT_FILTER,
  STATEMENT_TYPES,
  statementFilterError,
  type StatementFilter,
} from "@/lib/accounts/statement-filter";
import styles from "./accounts.module.css";

interface StatementFiltersProps {
  /** The filter the statement is showing now. */
  applied: StatementFilter;
  /** Loads the first page with the given filter. Rejects when the request fails. */
  onApply: (filter: StatementFilter) => Promise<void>;
}

/** Narrows the statement by kind and by a range of days. Nothing is requested until the user applies it. */
export function StatementFilters({ applied, onApply }: StatementFiltersProps) {
  const [draft, setDraft] = useState<StatementFilter>(applied);
  const [error, setError] = useState<string | null>(null);
  const [applying, setApplying] = useState(false);

  async function apply(filter: StatementFilter) {
    const problem = statementFilterError(filter);
    if (problem) {
      setError(problem);
      return;
    }

    setError(null);
    setApplying(true);
    try {
      await onApply(filter);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "Could not load the statement. Try again.");
    } finally {
      setApplying(false);
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void apply(draft);
  }

  function clear() {
    setDraft(NO_STATEMENT_FILTER);
    void apply(NO_STATEMENT_FILTER);
  }

  return (
    <form className={styles.filters} onSubmit={handleSubmit} noValidate aria-label="Statement filters">
      <div className={styles.field}>
        <label htmlFor="statement-type">Type</label>
        <select
          id="statement-type"
          value={draft.type}
          onChange={(event) => setDraft({ ...draft, type: event.target.value })}
          disabled={applying}
        >
          <option value="">All types</option>
          {STATEMENT_TYPES.map((type) => (
            <option key={type} value={type}>
              {type}
            </option>
          ))}
        </select>
      </div>

      <div className={styles.field}>
        <label htmlFor="statement-from">From</label>
        <input
          id="statement-from"
          type="date"
          value={draft.from}
          onChange={(event) => setDraft({ ...draft, from: event.target.value })}
          disabled={applying}
        />
      </div>

      <div className={styles.field}>
        <label htmlFor="statement-to">To</label>
        <input
          id="statement-to"
          type="date"
          value={draft.to}
          onChange={(event) => setDraft({ ...draft, to: event.target.value })}
          disabled={applying}
        />
      </div>

      <div className={styles.row}>
        <button type="submit" className={styles.button} disabled={applying}>
          {applying ? "Applying…" : "Apply filters"}
        </button>
        <button type="button" className={styles.secondaryButton} onClick={clear} disabled={applying}>
          Clear
        </button>
      </div>

      {error && (
        <p role="alert" className={styles.error}>
          {error}
        </p>
      )}
    </form>
  );
}
