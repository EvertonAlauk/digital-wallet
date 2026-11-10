import { describe, expect, it } from "vitest";
import { safeReturnUrl } from "@/lib/auth/return-url";
import { validateLoginFields } from "@/lib/auth/login-form-rules";

describe("safeReturnUrl", () => {
  it.each([
    ["/accounts", "/accounts"],
    ["/accounts/123?page=2", "/accounts/123?page=2"],
    ["/statement#june", "/statement#june"],
  ])("keeps the local path %s", (input, expected) => {
    expect(safeReturnUrl(input)).toBe(expected);
  });

  it.each([
    ["https://evil.example/phish", "absolute URL"],
    ["http://evil.example", "absolute URL with http"],
    ["//evil.example", "protocol-relative URL"],
    ["/\\evil.example", "backslash variant of a protocol-relative URL"],
    ["/\t/evil.example", "tab that browsers strip, which would make //evil.example"],
    ["javascript:alert(1)", "javascript scheme"],
    ["accounts", "relative path without a leading slash"],
    ["/login?returnUrl=/accounts", "the login page itself"],
    ["", "empty value"],
  ])("ignores %s (%s) and returns the home page", (input) => {
    expect(safeReturnUrl(input)).toBe("/");
  });

  it("returns the home page when there is no value", () => {
    expect(safeReturnUrl(null)).toBe("/");
    expect(safeReturnUrl(undefined)).toBe("/");
  });
});

describe("validateLoginFields", () => {
  it("requires both fields", () => {
    expect(validateLoginFields("  ", "")).toEqual({
      username: "Enter your username.",
      password: "Enter your password.",
    });
  });

  it("accepts filled fields", () => {
    expect(validateLoginFields("alice", "secret")).toEqual({});
  });
});
