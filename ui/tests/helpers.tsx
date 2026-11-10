import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { vi } from "vitest";
import { AuthProvider } from "@/components/auth/auth-provider";
import type { SessionUser } from "@/lib/auth/types";

export const aliceUser: SessionUser = {
  sub: "a1ce0000-0000-4000-8000-000000000001",
  username: "alice",
  name: "Alice Customer",
  email: "alice@digital-wallet.test",
  roles: ["user"],
};

export function jsonResponse(status: number, body?: unknown): Response {
  if (status === 204) {
    return new Response(null, { status });
  }

  return new Response(JSON.stringify(body ?? {}), {
    status,
    headers: { "content-type": "application/json" },
  });
}

type RouteHandler = (init: RequestInit | undefined) => Response | Promise<Response>;

/**
 * Replaces fetch. Routes are keyed by "METHOD /path". A request without a route fails the test, so an unexpected call
 * shows up immediately.
 */
export function routeFetch(routes: Record<string, RouteHandler>) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const key = `${init?.method ?? "GET"} ${String(input)}`;
    const handler = routes[key];
    if (!handler) {
      throw new Error(`Unexpected request: ${key}`);
    }

    return handler(init);
  });

  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

export function renderWithAuth(ui: ReactElement) {
  return render(<AuthProvider>{ui}</AuthProvider>);
}

/** The headers and body of the first request to the given route. */
export function requestSentTo(fetchMock: ReturnType<typeof routeFetch>, key: string) {
  const call = fetchMock.mock.calls.find(([input, init]) => `${init?.method ?? "GET"} ${String(input)}` === key);
  if (!call) {
    throw new Error(`No request was sent to ${key}`);
  }

  const [input, init] = call;
  return {
    url: String(input),
    init: init ?? {},
    headers: new Headers(init?.headers),
    body: typeof init?.body === "string" ? (JSON.parse(init.body) as unknown) : undefined,
  };
}
