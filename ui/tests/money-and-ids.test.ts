import { describe, expect, it } from "vitest";
import { isAccountId } from "@/lib/accounts/ids";
import { formatMoney, parseAmount } from "@/lib/accounts/money";
import { newIdempotencyKey } from "@/lib/api/idempotency";

describe("parseAmount", () => {
  it.each([
    ["10", 10],
    ["10.5", 10.5],
    ["10.50", 10.5],
    ["0.01", 0.01],
    ["  25  ", 25],
    ["3,75", 3.75],
  ])("accepts %s as %s", (input, expected) => {
    expect(parseAmount(input)).toEqual({ ok: true, value: expected });
  });

  it("rejects an empty amount", () => {
    expect(parseAmount("   ")).toEqual({ ok: false, message: "Enter an amount." });
  });

  it.each(["0", "0.00"])("rejects zero (%s)", (input) => {
    expect(parseAmount(input)).toEqual({ ok: false, message: "The amount must be greater than zero." });
  });

  it.each(["-5", "1.234", "abc", "1e3", "10.", ".5"])("rejects %s as not a valid amount", (input) => {
    expect(parseAmount(input).ok).toBe(false);
  });
});

describe("formatMoney", () => {
  it("formats the amount in the account's currency", () => {
    expect(formatMoney(1234.5, "BRL")).toBe("R$1,234.50");
  });
});

describe("isAccountId", () => {
  it("accepts a UUID, with surrounding spaces", () => {
    expect(isAccountId("  a1ce0000-0000-4000-8000-000000000001 ")).toBe(true);
  });

  it.each(["", "not-an-id", "a1ce0000-0000-4000-8000-00000000000"])("rejects %s", (value) => {
    expect(isAccountId(value)).toBe(false);
  });
});

describe("newIdempotencyKey", () => {
  it("creates a UUID v4 string", () => {
    const key = newIdempotencyKey();

    expect(key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  });

  it("creates a different key each time", () => {
    expect(newIdempotencyKey()).not.toBe(newIdempotencyKey());
  });
});
