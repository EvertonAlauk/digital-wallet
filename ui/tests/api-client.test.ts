import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch, onUnauthorized, setRedirectHandler } from "@/lib/api/client";
import { jsonResponse, requestSentTo, routeFetch } from "./helpers";

afterEach(() => {
  setRedirectHandler(null);
  onUnauthorized(null);
});

describe("apiFetch", () => {
  it("sends the X-CSRF header and the same-origin credentials on a GET", async () => {
    const fetchMock = routeFetch({ "GET /api/accounts": () => jsonResponse(200, { items: [] }) });

    await apiFetch("/api/accounts");

    const sent = requestSentTo(fetchMock, "GET /api/accounts");
    expect(sent.headers.get("X-CSRF")).toBe("1");
    expect(sent.init.credentials).toBe("same-origin");
  });

  it("sends a JSON body with its content type on a POST", async () => {
    const fetchMock = routeFetch({ "POST /api/accounts": () => jsonResponse(201, { id: "a1" }) });

    await apiFetch("/api/accounts", { method: "POST", body: { currency: "BRL" } });

    const sent = requestSentTo(fetchMock, "POST /api/accounts");
    expect(sent.headers.get("X-CSRF")).toBe("1");
    expect(sent.headers.get("Content-Type")).toBe("application/json");
    expect(sent.body).toEqual({ currency: "BRL" });
  });

  it("never sends an Authorization header or a token", async () => {
    const fetchMock = routeFetch({ "GET /api/accounts": () => jsonResponse(200, {}) });

    await apiFetch("/api/accounts");

    const sent = requestSentTo(fetchMock, "GET /api/accounts");
    expect(sent.headers.has("Authorization")).toBe(false);
  });

  it("returns undefined for 204 No Content", async () => {
    routeFetch({ "POST /bff/logout": () => jsonResponse(204) });

    await expect(apiFetch<void>("/bff/logout", { method: "POST", body: {} })).resolves.toBeUndefined();
  });

  it("on 401, notifies the listener and sends the browser to the login page with the current path", async () => {
    routeFetch({ "GET /api/accounts": () => jsonResponse(401, { title: "Not signed in" }) });
    window.history.pushState({}, "", "/accounts/details");
    const listener = vi.fn();
    const redirect = vi.fn();
    onUnauthorized(listener);
    setRedirectHandler(redirect);

    await expect(apiFetch("/api/accounts")).rejects.toBeInstanceOf(ApiError);

    expect(listener).toHaveBeenCalledOnce();
    expect(redirect).toHaveBeenCalledWith("/login?returnUrl=%2Faccounts%2Fdetails");
  });

  it("does not redirect on 401 when handleUnauthorized is false", async () => {
    routeFetch({ "POST /bff/login": () => jsonResponse(401, { detail: "Invalid username or password" }) });
    const redirect = vi.fn();
    setRedirectHandler(redirect);

    await expect(apiFetch("/bff/login", { method: "POST", body: {}, handleUnauthorized: false })).rejects.toMatchObject({
      status: 401,
    });

    expect(redirect).not.toHaveBeenCalled();
  });

  it("uses the problem details detail as the error message", async () => {
    routeFetch({ "POST /bff/login": () => jsonResponse(429, { title: "Too many requests", detail: "Wait a moment" }) });

    const error = await apiFetch("/bff/login", { method: "POST", body: {}, handleUnauthorized: false }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 429, message: "Wait a moment" });
  });
});
