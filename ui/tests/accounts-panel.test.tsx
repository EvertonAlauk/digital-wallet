import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { AccountsPanel } from "@/components/accounts/accounts-panel";
import { jsonResponse, renderWithAuth, requestSentTo, routeFetch } from "./helpers";

const savings = {
  id: "5a710000-0000-4000-8000-000000000001",
  ownerId: "a1ce0000-0000-4000-8000-000000000001",
  name: "Savings",
  status: "Active",
  balance: 150,
  currency: "BRL",
  createdAt: "2026-01-02T10:00:00Z",
};

const untitled = { ...savings, id: "5a710000-0000-4000-8000-000000000002", name: null, balance: 0 };

describe("AccountsPanel", () => {
  it("lists the user's accounts with their names and balances, each linking to its page", async () => {
    routeFetch({ "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 0, inactiveCount: 0 }), "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [savings, untitled] }) });

    renderWithAuth(<AccountsPanel />);

    const list = await screen.findByRole("list", { name: "Active accounts" });
    const items = within(list).getAllByRole("link");
    expect(items[0]).toHaveAttribute("href", `/account?id=${savings.id}`);
    expect(items[0]).toHaveTextContent("Savings");
    expect(items[0]).toHaveTextContent("150.00");
    expect(items[1]).toHaveTextContent("Untitled account");
  });

  it("shows an empty state for a user with no accounts", async () => {
    routeFetch({ "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 0, inactiveCount: 0 }), "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [] }) });

    renderWithAuth(<AccountsPanel />);

    expect(await screen.findByText("You have no active accounts. Create one to get started.")).toBeInTheDocument();
  });

  it("shows an error when the accounts cannot be loaded", async () => {
    routeFetch({ "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 0, inactiveCount: 0 }), "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(500, { title: "Server error" }) });

    renderWithAuth(<AccountsPanel />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load your accounts.");
  });

  it("opens an account with the typed name, sends an Idempotency-Key, and refreshes the list", async () => {
    let accounts = [savings];
    const fetchMock = routeFetch({
      "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 0, inactiveCount: 0 }),
      "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: accounts }),
      "POST /api/accounts": () => {
        accounts = [{ ...untitled, name: "Groceries" }, ...accounts];
        return jsonResponse(201, accounts[0]);
      },
    });
    renderWithAuth(<AccountsPanel />);
    await screen.findByText("Savings");

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Account name (optional)"), "Groceries");
    await user.click(screen.getByRole("button", { name: "Create account" }));

    await screen.findByText("Groceries");
    const sent = requestSentTo(fetchMock, "POST /api/accounts");
    expect(sent.body).toEqual({ name: "Groceries" });
    expect(sent.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/);
    expect(sent.headers.get("X-CSRF")).toBe("1");
  });

  it("creates an account without a name when the field is left blank", async () => {
    const fetchMock = routeFetch({
      "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 0, inactiveCount: 0 }),
      "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [] }),
      "POST /api/accounts": () => jsonResponse(201, untitled),
    });
    renderWithAuth(<AccountsPanel />);
    await screen.findByText("You have no active accounts. Create one to get started.");

    await userEvent.setup().click(screen.getByRole("button", { name: "Create account" }));

    await waitFor(() => expect(fetchMock.mock.calls.some(([input]) => String(input) === "/api/accounts")).toBe(true));
    const sent = requestSentTo(fetchMock, "POST /api/accounts");
    expect(sent.body).toEqual({});
  });

  it("refuses a name longer than 60 characters without calling the API", async () => {
    const fetchMock = routeFetch({ "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 0, inactiveCount: 0 }), "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [] }) });
    renderWithAuth(<AccountsPanel />);
    await screen.findByText("You have no active accounts. Create one to get started.");

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Account name (optional)"), "x".repeat(61));
    await user.click(screen.getByRole("button", { name: "Create account" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The name can have at most 60 characters.");
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
  });

  it("shows the API's reason when opening the account fails", async () => {
    routeFetch({
      "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 0, inactiveCount: 0 }),
      "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [] }),
      "POST /api/accounts": () => jsonResponse(422, { title: "Business rule violation", detail: "Name has a control character." }),
    });
    renderWithAuth(<AccountsPanel />);
    await screen.findByText("You have no active accounts. Create one to get started.");

    await userEvent.setup().click(screen.getByRole("button", { name: "Create account" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Name has a control character.");
  });
});
