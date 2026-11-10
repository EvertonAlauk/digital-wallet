const AMOUNT_PATTERN = /^\d+(\.\d{1,2})?$/;

export type ParsedAmount = { ok: true; value: number } | { ok: false; message: string };

/**
 * Reads an amount typed by the user. Accepts a positive number with at most two decimals, such as "10" or "10.50".
 * The API receives it as a number, so the rounding and the limits stay on the server.
 */
export function parseAmount(input: string): ParsedAmount {
  const text = input.trim().replace(",", ".");
  if (text === "") {
    return { ok: false, message: "Enter an amount." };
  }

  if (!AMOUNT_PATTERN.test(text)) {
    return { ok: false, message: "Use a number with at most two decimal places, such as 10 or 10.50." };
  }

  const value = Number(text);
  if (value <= 0) {
    return { ok: false, message: "The amount must be greater than zero." };
  }

  return { ok: true, value };
}

export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount);
}
