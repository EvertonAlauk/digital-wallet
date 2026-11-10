import { formatMoney } from "@/lib/accounts/money";
import type { StatementItem } from "@/lib/accounts/types";
import styles from "./accounts.module.css";

interface StatementTableProps {
  items: StatementItem[];
  nextCursor: string | null;
  loadingMore: boolean;
  onLoadMore: () => void;
}

/**
 * Ledger entries, newest first. A positive amount is money in (credit) and a negative amount is money out (debit),
 * so transfers show their direction.
 */
export function StatementTable({ items, nextCursor, loadingMore, onLoadMore }: StatementTableProps) {
  if (items.length === 0) {
    return <p className={styles.muted}>No activity yet.</p>;
  }

  return (
    <div className={styles.form}>
      <table className={styles.table}>
        <caption className={styles.muted}>Statement, newest first</caption>
        <thead>
          <tr>
            <th scope="col">Date</th>
            <th scope="col">Type</th>
            <th scope="col">Direction</th>
            <th scope="col" className={styles.amount}>
              Amount
            </th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => {
            const credit = item.amount >= 0;
            return (
              <tr key={`${item.transactionId}-${item.createdAt}-${item.amount}`}>
                <td>{new Date(item.createdAt).toLocaleString("en-US")}</td>
                <td>{item.type}</td>
                <td className={credit ? styles.credit : styles.debit}>{credit ? "In" : "Out"}</td>
                <td className={`${styles.amount} ${credit ? styles.credit : styles.debit}`}>
                  {formatMoney(item.amount, item.currency)}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {nextCursor && (
        <button type="button" className={styles.secondaryButton} onClick={onLoadMore} disabled={loadingMore}>
          {loadingMore ? "Loading…" : "Load older entries"}
        </button>
      )}
    </div>
  );
}
