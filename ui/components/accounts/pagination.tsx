import { ACCOUNTS_PAGE_SIZE, pageCount } from "@/lib/accounts/paging";
import styles from "./accounts.module.css";

interface PaginationProps {
  /** Number of accounts before the current page (0 on the first page). */
  offset: number;
  totalCount: number;
  pageSize?: number;
  disabled: boolean;
  onChange: (offset: number) => void;
}

/** Previous and next buttons with the current page. */
export function Pagination({ offset, totalCount, pageSize = ACCOUNTS_PAGE_SIZE, disabled, onChange }: PaginationProps) {
  // Always shown, even with a single page, so the user can see where they are.
  const pages = pageCount(totalCount, pageSize);

  const current = Math.floor(offset / pageSize) + 1;

  return (
    <nav aria-label="Pagination" className={styles.pagination}>
      <button
        type="button"
        className={styles.secondaryButton}
        onClick={() => onChange(offset - pageSize)}
        disabled={disabled || current <= 1}
      >
        Previous
      </button>
      <span className={styles.muted}>
        Page {current} of {pages}
      </span>
      <button
        type="button"
        className={styles.secondaryButton}
        onClick={() => onChange(offset + pageSize)}
        disabled={disabled || current >= pages}
      >
        Next
      </button>
    </nav>
  );
}
