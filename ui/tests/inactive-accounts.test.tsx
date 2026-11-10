import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import InactiveAccountsPage from "@/app/(protected)/inactive-accounts/page";
import { AccountsPanel } from "@/components/accounts/accounts-panel";
import { jsonResponse, renderWithAuth, routeFetch } from "./helpers";

const inactive = {
  id: "5a710000-0000-4000-8000-000000000002",
  ownerId: "a1ce0000-0000-4000-8000-000000000001",
  name: "Old card",
  status: "Blocked",
  balance: 42.5,
  currency: "BRL",
  createdAt: "2026-01-01T10:00:00Z",
};

const active = {
  id: "5a710000-0000-4000-8000-000000000001",
  ownerId: "a1ce0000-0000-4000-8000-000000000001",
  name: "Savings",
  status: "Active",
  balance: 100,
  currency: "BRL",
  createdAt: "2026-01-02T10:00:00Z",
};

describe("inactive accounts page", () => {
  it("lists only the inactive accounts, with their balances", async () => {
    routeFetch({ "GET /api/accounts?limit=10&offset=0&status=inactive": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [inactive] }) });

    renderWithAuth(<InactiveAccountsPage />);

    const list = await screen.findByRole("list", { name: "Inactive accounts" });
    expect(within(list).getByText("Old card")).toBeInTheDocument();
    expect(within(list).getByText("R$42.50")).toBeInTheDocument();
    expect(within(list).queryByText("Savings")).not.toBeInTheDocument();
  });

  it("activates an account from the list and removes it from the list", async () => {
    let remaining = [inactive];
    const calls: string[] = [];
    routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=inactive": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: remaining }),
      [`POST /api/accounts/${inactive.id}/activate`]: () => {
        calls.push("activate");
        remaining = [];
        return jsonResponse(200, { ...inactive, status: "Active" });
      },
    });
    renderWithAuth(<InactiveAccountsPage />);
    await screen.findByRole("list", { name: "Inactive accounts" });

    await userEvent.setup().click(screen.getByRole("button", { name: "Activate" }));

    expect(await screen.findByText("You have no inactive accounts.")).toBeInTheDocument();
    expect(calls).toEqual(["activate"]);
  });

  it("shows an empty state when there are no inactive accounts", async () => {
    routeFetch({ "GET /api/accounts?limit=10&offset=0&status=inactive": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [] }) });

    renderWithAuth(<InactiveAccountsPage />);

    expect(await screen.findByText("You have no inactive accounts.")).toBeInTheDocument();
  });

  it("has a back link to the home page", async () => {
    routeFetch({ "GET /api/accounts?limit=10&offset=0&status=inactive": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [] }) });

    renderWithAuth(<InactiveAccountsPage />);

    expect(await screen.findByRole("link", { name: "Back to your accounts" })).toHaveAttribute("href", "/");
  });
});

describe("home total", () => {
  it("shows the total of all accounts and the counts of active and inactive ones", async () => {
    routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [active] }),
      "GET /api/accounts/summary": () =>
        jsonResponse(200, { totals: [{ currency: "BRL", amount: 142.5 }], activeCount: 1, inactiveCount: 1 }),
    });

    renderWithAuth(<AccountsPanel />);

    expect(await screen.findByRole("heading", { name: "Total balance" })).toBeInTheDocument();
    expect(screen.getByLabelText("Total amount")).toHaveTextContent("R$142.50");
    expect(screen.getByText(/Across 2 accounts \(1 active, 1 inactive\)/)).toBeInTheDocument();
  });

  it("lists only active accounts on the home page", async () => {
    const calls: string[] = [];
    routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=active": () => {
        calls.push("active");
        return jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [active] });
      },
      "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 1, inactiveCount: 0 }),
    });

    renderWithAuth(<AccountsPanel />);

    const list = await screen.findByRole("list", { name: "Active accounts" });
    expect(within(list).getByText("Savings")).toBeInTheDocument();
    await waitFor(() => expect(calls).toEqual(["active"]));
  });

  it("links to the inactive accounts page", async () => {
    routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, { totalCount: 0, limit: 10, offset: 0, items: [] }),
      "GET /api/accounts/summary": () => jsonResponse(200, { totals: [], activeCount: 0, inactiveCount: 0 }),
    });

    renderWithAuth(<AccountsPanel />);

    expect(await screen.findByRole("link", { name: "View inactive accounts" })).toHaveAttribute(
      "href",
      "/inactive-accounts",
    );
  });
});
