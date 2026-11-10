"use client";

import { useState, type FormEvent } from "react";
import { parseAmount } from "@/lib/accounts/money";
import styles from "./accounts.module.css";

interface MovementFormProps {
  /** Id prefix, so several forms on one page have unique labels. */
  id: string;
  title: string;
  submitLabel: string;
  disabled: boolean;
  onSubmit: (amount: number) => Promise<void>;
}

/** Asks for an amount and hands a validated number to onSubmit. Clears the field once the request is accepted. */
export function MovementForm({ id, title, submitLabel, disabled, onSubmit }: MovementFormProps) {
  const [amount, setAmount] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const parsed = parseAmount(amount);
    if (!parsed.ok) {
      setError(parsed.message);
      return;
    }

    setError(null);
    setSubmitting(true);
    try {
      await onSubmit(parsed.value);
      setAmount("");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The request failed. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className={styles.form} onSubmit={handleSubmit} noValidate aria-labelledby={`${id}-title`}>
      <h3 id={`${id}-title`} className={styles.sectionTitle}>
        {title}
      </h3>
      <div className={styles.row}>
        <div className={styles.field}>
          <label htmlFor={`${id}-amount`}>Amount (BRL)</label>
          <input
            id={`${id}-amount`}
            inputMode="decimal"
            placeholder="0.00"
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            aria-invalid={error ? true : undefined}
            disabled={disabled || submitting}
          />
        </div>
        <button type="submit" className={styles.button} disabled={disabled || submitting}>
          {submitting ? "Sending…" : submitLabel}
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
