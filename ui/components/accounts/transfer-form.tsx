"use client";

import { useState, type FormEvent } from "react";
import { isAccountId } from "@/lib/accounts/ids";
import { parseAmount } from "@/lib/accounts/money";
import styles from "./accounts.module.css";

interface TransferFormProps {
  disabled: boolean;
  onSubmit: (destinationAccountId: string, amount: number) => Promise<void>;
}

/** Sends money from this account to another account, identified by its id. */
export function TransferForm({ disabled, onSubmit }: TransferFormProps) {
  const [destination, setDestination] = useState("");
  const [amount, setAmount] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!isAccountId(destination)) {
      setError("Enter the destination account id. The other person can find it on their account page.");
      return;
    }

    const parsed = parseAmount(amount);
    if (!parsed.ok) {
      setError(parsed.message);
      return;
    }

    setError(null);
    setSubmitting(true);
    try {
      await onSubmit(destination.trim(), parsed.value);
      setDestination("");
      setAmount("");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The transfer failed. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className={styles.form} onSubmit={handleSubmit} noValidate aria-labelledby="transfer-title">
      <h3 id="transfer-title" className={styles.sectionTitle}>
        Transfer to another account
      </h3>
      <div className={styles.row}>
        <div className={styles.field}>
          <label htmlFor="transfer-destination">Destination account id</label>
          <input
            id="transfer-destination"
            value={destination}
            onChange={(event) => setDestination(event.target.value)}
            aria-invalid={error ? true : undefined}
            disabled={disabled || submitting}
          />
        </div>
        <div className={styles.field}>
          <label htmlFor="transfer-amount">Amount (BRL)</label>
          <input
            id="transfer-amount"
            inputMode="decimal"
            placeholder="0.00"
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            disabled={disabled || submitting}
          />
        </div>
        <button type="submit" className={styles.button} disabled={disabled || submitting}>
          {submitting ? "Sending…" : "Send transfer"}
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
