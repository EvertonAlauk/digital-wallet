/** Used when NEXT_PUBLIC_ACCOUNTS_PAGE_SIZE is missing or not a valid size. */
export const DEFAULT_ACCOUNTS_PAGE_SIZE = 5;

/** Reads a page size. Anything that is not a whole number between 1 and 100 falls back to the default. */
export function parsePageSize(value: string | undefined, fallback: number = DEFAULT_ACCOUNTS_PAGE_SIZE): number {
  if (value === undefined || value.trim() === "") {
    return fallback;
  }

  const size = Number(value);
  return Number.isInteger(size) && size >= 1 && size <= 100 ? size : fallback;
}

/**
 * Accounts per page on the home page and on the inactive accounts page. Read from NEXT_PUBLIC_ACCOUNTS_PAGE_SIZE.
 * In a static export the value is fixed at build time (npm run build:static).
 */
export const ACCOUNTS_PAGE_SIZE = parsePageSize(process.env.NEXT_PUBLIC_ACCOUNTS_PAGE_SIZE);

/** Number of pages needed for the given total. An empty list still has one (empty) page. */
export function pageCount(totalCount: number, pageSize: number = ACCOUNTS_PAGE_SIZE): number {
  return Math.max(1, Math.ceil(totalCount / pageSize));
}
