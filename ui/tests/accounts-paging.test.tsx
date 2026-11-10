import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import AccountPage from "@/app/(protected)/account/page";
import { AccountsPanel } from "@/components/accounts/accounts-panel";
import { InactiveAccountsPanel } from "@/components/accounts/inactive-accounts-panel";
import { jsonResponse, renderWithAuth, requestSentTo, routeFetch } from "./helpers";

const navigation = vi.hoisted(() => ({
  search: new URLSearchParams(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() }),
  usePathname: () => "/account",
  useSearchParams: () => navigation.search,
}));

const summary = { totals: [], activeCount: 25, inactiveCount: 0 };

function accountAt(index: number, status = "Active") {
  return {
    id: `5a71${String(index).padStart(4, "0")}-0000-4000-8000-000000000000`,
    ownerId: "a1ce0000-0000-4000-8000-000000000001",
    name: `Account ${index}`,
    status,
    balance: index,
    currency: "BRL",
    createdAt: "2026-01-02T10:00:00Z",
  };
}

/** A paged list of accounts: the items for the offset, and the total of all accounts. */
function pageOf(total: number, offset: number, status = "Active") {
  const items = Array.from({ length: Math.min(10, Math.max(0, total - offset)) }, (_, index) =>
    accountAt(offset + index, status),
  );
  return { totalCount: total, limit: 10, offset, items };
}

describe("active accounts pagination on the home page", () => {
  it("shows the page and the total, and moves to the next page", async () => {
    const fetchMock = routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, pageOf(25, 0)),
      "GET /api/accounts?limit=10&offset=10&status=active": () => jsonResponse(200, pageOf(25, 10)),
      "GET /api/accounts/summary": () => jsonResponse(200, summary),
    });
    renderWithAuth(<AccountsPanel />);
    await screen.findByRole("list", { name: "Active accounts" });

    const pagination = within(screen.getByRole("navigation", { name: "Pagination" }));
    expect(pagination.getByText("Page 1 of 3")).toBeInTheDocument();
    expect(pagination.getByRole("button", { name: "Previous" })).toBeDisabled();

    await userEvent.setup().click(pagination.getByRole("button", { name: "Next" }));

    await waitFor(() => expect(screen.getByText("Page 2 of 3")).toBeInTheDocument());
    expect(fetchMock.mock.calls.some(([input]) => String(input) === "/api/accounts?limit=10&offset=10&status=active")).toBe(true);
  });

  it("shows the pagination with one page when everything fits on it", async () => {
    routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, pageOf(3, 0)),
      "GET /api/accounts/summary": () => jsonResponse(200, { ...summary, activeCount: 3 }),
    });
    renderWithAuth(<AccountsPanel />);
    await screen.findByRole("list", { name: "Active accounts" });

    const pagination = within(screen.getByRole("navigation", { name: "Pagination" }));
    expect(pagination.getByText("Page 1 of 1")).toBeInTheDocument();
    expect(pagination.getByRole("button", { name: "Previous" })).toBeDisabled();
    expect(pagination.getByRole("button", { name: "Next" })).toBeDisabled();
  });
});

describe("deactivating from the active list", () => {
  it("deactivates an account from the list and removes it from the list", async () => {
    let accounts = [accountAt(1), accountAt(2)];
    const fetchMock = routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=active": () =>
        jsonResponse(200, { totalCount: accounts.length, limit: 10, offset: 0, items: accounts }),
      "GET /api/accounts/summary": () => jsonResponse(200, { ...summary, activeCount: accounts.length }),
      [`POST /api/accounts/${accountAt(1).id}/deactivate`]: () => {
        accounts = accounts.filter((account) => account.id !== accountAt(1).id);
        return jsonResponse(200, accountAt(1, "Blocked"));
      },
    });
    renderWithAuth(<AccountsPanel />);
    const list = await screen.findByRole("list", { name: "Active accounts" });

    const row = within(list).getByText("Account 1").closest("li")!;
    await userEvent.setup().click(within(row).getByRole("button", { name: "Deactivate" }));

    await waitFor(() => expect(within(list).queryByText("Account 1")).not.toBeInTheDocument());
    expect(within(list).getByText("Account 2")).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([input, init]) => init?.method === "POST" && String(input).endsWith("/deactivate"))).toBe(true);
  });

  it("shows the API's reason when deactivating fails", async () => {
    routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=active": () => jsonResponse(200, pageOf(1, 0)),
      "GET /api/accounts/summary": () => jsonResponse(200, { ...summary, activeCount: 1 }),
      [`POST /api/accounts/${accountAt(0).id}/deactivate`]: () => jsonResponse(500, { title: "Server error" }),
    });
    renderWithAuth(<AccountsPanel />);
    await screen.findByRole("list", { name: "Active accounts" });

    await userEvent.setup().click(screen.getByRole("button", { name: "Deactivate" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Server error");
  });
});

describe("inactive accounts pagination", () => {
  it("pages through the inactive accounts", async () => {
    routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=inactive": () => jsonResponse(200, pageOf(12, 0, "Blocked")),
      "GET /api/accounts?limit=10&offset=10&status=inactive": () => jsonResponse(200, pageOf(12, 10, "Blocked")),
    });
    renderWithAuth(<InactiveAccountsPanel />);
    await screen.findByRole("list", { name: "Inactive accounts" });

    await userEvent.setup().click(screen.getByRole("button", { name: "Next" }));

    await waitFor(() => expect(screen.getByText("Page 2 of 2")).toBeInTheDocument());
    expect(within(screen.getByRole("list", { name: "Inactive accounts" })).getByText("Account 10")).toBeInTheDocument();
  });

  it("requests the inactive filter on the first page", async () => {
    const fetchMock = routeFetch({
      "GET /api/accounts?limit=10&offset=0&status=inactive": () => jsonResponse(200, pageOf(0, 0, "Blocked")),
    });
    renderWithAuth(<InactiveAccountsPanel />);
    await screen.findByText("You have no inactive accounts.");

    expect(requestSentTo(fetchMock, "GET /api/accounts?limit=10&offset=0&status=inactive").url).toContain("status=inactive");
  });
});

describe("inactive account page", () => {
  it("shows no movement forms, only the notice", async () => {
    navigation.search = new URLSearchParams(`id=${accountAt(1).id}`);
    routeFetch({
      [`GET /api/accounts/${accountAt(1).id}`]: () => jsonResponse(200, accountAt(1, "Blocked")),
      [`GET /api/accounts/${accountAt(1).id}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    });

    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Account 1" });

    expect(screen.queryByRole("form", { name: "Deposit" })).not.toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Withdraw" })).not.toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Transfer to another account" })).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("This account is inactive.");
  });
});
