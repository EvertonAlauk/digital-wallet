import { describe, expect, it } from "vitest";
import { DEFAULT_ACCOUNTS_PAGE_SIZE, pageCount, parsePageSize } from "@/lib/accounts/paging";

describe("parsePageSize", () => {
  it("defaults to 5 when the variable is missing or empty", () => {
    expect(DEFAULT_ACCOUNTS_PAGE_SIZE).toBe(5);
    expect(parsePageSize(undefined)).toBe(5);
    expect(parsePageSize("  ")).toBe(5);
  });

  it.each(["1", "5", "25", "100"])("accepts %s", (value) => {
    expect(parsePageSize(value)).toBe(Number(value));
  });

  it.each(["0", "101", "-3", "2.5", "many"])("falls back to the default for %s", (value) => {
    expect(parsePageSize(value)).toBe(5);
  });
});

describe("pageCount", () => {
  it("is at least one page, even for an empty list", () => {
    expect(pageCount(0, 5)).toBe(1);
  });

  it("rounds up to whole pages", () => {
    expect(pageCount(11, 5)).toBe(3);
    expect(pageCount(10, 5)).toBe(2);
  });
});
