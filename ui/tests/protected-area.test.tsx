import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import ProtectedLayout from "@/app/(protected)/layout";
import HomePage from "@/app/(protected)/page";
import { aliceUser, jsonResponse, renderWithAuth, requestSentTo, routeFetch } from "./helpers";

const navigation = vi.hoisted(() => ({
  router: { replace: vi.fn(), push: vi.fn() },
  pathname: "/",
}));

vi.mock("next/navigation", () => ({
  useRouter: () => navigation.router,
  usePathname: () => navigation.pathname,
  useSearchParams: () => new URLSearchParams(),
}));

function protectedArea() {
  return (
    <ProtectedLayout>
      <HomePage />
    </ProtectedLayout>
  );
}

beforeEach(() => {
  navigation.router.replace.mockReset();
  navigation.pathname = "/";
});

describe("protected area", () => {
  it("restores the session on load and shows the greeting with the user's name", async () => {
    const fetchMock = routeFetch({ "GET /api/accounts": () => jsonResponse(200, { items: [] }),
      "GET /bff/session": () => jsonResponse(200, { user: aliceUser }) });

    renderWithAuth(protectedArea());

    expect(await screen.findByRole("heading", { name: "Hello, Alice Customer" })).toBeInTheDocument();
    expect(requestSentTo(fetchMock, "GET /bff/session").headers.get("X-CSRF")).toBe("1");
    expect(navigation.router.replace).not.toHaveBeenCalled();
  });

  it("greets with the username when the identity provider has no name", async () => {
    routeFetch({ "GET /api/accounts": () => jsonResponse(200, { items: [] }),
      "GET /bff/session": () => jsonResponse(200, { user: { ...aliceUser, name: null } }) });

    renderWithAuth(protectedArea());

    expect(await screen.findByRole("heading", { name: "Hello, alice" })).toBeInTheDocument();
  });

  it("shows the accounts section on the home page", async () => {
    routeFetch({
      "GET /bff/session": () => jsonResponse(200, { user: aliceUser }),
      "GET /api/accounts": () => jsonResponse(200, { items: [] }),
    });

    renderWithAuth(protectedArea());

    expect(await screen.findByRole("heading", { name: "Your accounts" })).toBeInTheDocument();
  });

  it("sends a visitor without a session to the login page with the current page as returnUrl", async () => {
    navigation.pathname = "/accounts";
    routeFetch({ "GET /api/accounts": () => jsonResponse(200, { items: [] }),
      "GET /bff/session": () => jsonResponse(401, { title: "Not signed in" }) });

    renderWithAuth(protectedArea());

    await waitFor(() =>
      expect(navigation.router.replace).toHaveBeenCalledWith("/login?returnUrl=%2Faccounts"),
    );
    expect(screen.queryByRole("heading", { name: /Hello/ })).not.toBeInTheDocument();
  });

  it("signs out: calls the BFF, then goes to the login page without a returnUrl", async () => {
    const fetchMock = routeFetch({
      "GET /api/accounts": () => jsonResponse(200, { items: [] }),
      "GET /bff/session": () => jsonResponse(200, { user: aliceUser }),
      "POST /bff/logout": () => jsonResponse(204),
    });
    renderWithAuth(protectedArea());
    await screen.findByRole("heading", { name: /Hello/ });

    await userEvent.setup().click(screen.getByRole("button", { name: "Sign out" }));

    await waitFor(() => expect(navigation.router.replace).toHaveBeenCalledWith("/login"));
    expect(navigation.router.replace).toHaveBeenCalledTimes(1);
    expect(requestSentTo(fetchMock, "POST /bff/logout").headers.get("X-CSRF")).toBe("1");
  });

  it("stays signed in and shows an error when the BFF cannot end the session", async () => {
    routeFetch({
      "GET /api/accounts": () => jsonResponse(200, { items: [] }),
      "GET /bff/session": () => jsonResponse(200, { user: aliceUser }),
      "POST /bff/logout": () => jsonResponse(500, { title: "Server error" }),
    });
    renderWithAuth(protectedArea());
    await screen.findByRole("heading", { name: /Hello/ });

    await userEvent.setup().click(screen.getByRole("button", { name: "Sign out" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Could not sign out right now. Try again.");
    expect(screen.getByRole("heading", { name: /Hello/ })).toBeInTheDocument();
    expect(navigation.router.replace).not.toHaveBeenCalled();
  });
});

describe("cross-tab session sync", () => {
  class FakeBroadcastChannel {
    static instances: FakeBroadcastChannel[] = [];
    onmessage: ((event: MessageEvent<unknown>) => void) | null = null;

    constructor(readonly name: string) {
      FakeBroadcastChannel.instances.push(this);
    }

    postMessage() {}

    close() {}

    /** Simulates a message sent by another tab. */
    receive(data: unknown) {
      this.onmessage?.({ data } as MessageEvent<unknown>);
    }
  }

  beforeEach(() => {
    FakeBroadcastChannel.instances = [];
    vi.stubGlobal("BroadcastChannel", FakeBroadcastChannel);
  });

  afterEach(() => {
    FakeBroadcastChannel.instances = [];
  });

  it("signs this tab out when another tab signs out", async () => {
    routeFetch({ "GET /api/accounts": () => jsonResponse(200, { items: [] }),
      "GET /bff/session": () => jsonResponse(200, { user: aliceUser }) });
    renderWithAuth(protectedArea());
    await screen.findByRole("heading", { name: /Hello/ });

    FakeBroadcastChannel.instances[0].receive({ type: "logout" });

    await waitFor(() => expect(navigation.router.replace).toHaveBeenCalledWith("/login?returnUrl=%2F"));
  });

  it("asks the BFF again when another tab signs in, instead of trusting the message", async () => {
    let signedIn = false;
    const fetchMock = routeFetch({
      "GET /api/accounts": () => jsonResponse(200, { items: [] }),
      "GET /bff/session": () => (signedIn ? jsonResponse(200, { user: aliceUser }) : jsonResponse(401, {})),
    });
    renderWithAuth(<span>signed-out page</span>);
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    signedIn = true;
    FakeBroadcastChannel.instances[0].receive({ type: "login" });

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
  });

  it("ignores messages that are not session messages", async () => {
    const fetchMock = routeFetch({ "GET /api/accounts": () => jsonResponse(200, { items: [] }),
      "GET /bff/session": () => jsonResponse(200, { user: aliceUser }) });
    renderWithAuth(protectedArea());
    await screen.findByRole("heading", { name: /Hello/ });

    FakeBroadcastChannel.instances[0].receive({ type: "something-else" });
    FakeBroadcastChannel.instances[0].receive("not an object");

    const sessionRequests = fetchMock.mock.calls.filter(([input]) => String(input) === "/bff/session");
    expect(sessionRequests).toHaveLength(1);
    expect(navigation.router.replace).not.toHaveBeenCalled();
  });
});
