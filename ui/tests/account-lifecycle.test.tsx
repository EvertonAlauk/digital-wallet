import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AccountPage from "@/app/(protected)/account/page";
import { BackLink } from "@/components/accounts/back-link";
import { jsonResponse, renderWithAuth, requestSentTo, routeFetch } from "./helpers";

const navigation = vi.hoisted(() => ({
  search: new URLSearchParams(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() }),
  usePathname: () => "/account",
  useSearchParams: () => navigation.search,
}));

const ACCOUNT_ID = "5a710000-0000-4000-8000-000000000001";

function account(overrides: Partial<{ name: string | null; status: string; balance: number }> = {}) {
  return {
    id: ACCOUNT_ID,
    ownerId: "a1ce0000-0000-4000-8000-000000000001",
    name: "Savings",
    status: "Active",
    balance: 150,
    currency: "BRL",
    createdAt: "2026-01-02T10:00:00Z",
    ...overrides,
  };
}

const statementRoutes = {
  [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
};

beforeEach(() => {
  navigation.search = new URLSearchParams(`id=${ACCOUNT_ID}`);
});

describe("account lifecycle on the account page", () => {
  it("renames the account and shows the saved name", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account()),
      ...statementRoutes,
      [`PATCH /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account({ name: "Holidays" })),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Edit name" }));
    const input = screen.getByLabelText("Account name");
    await user.clear(input);
    await user.type(input, "Holidays");
    await user.click(screen.getByRole("button", { name: "Save name" }));

    expect(await screen.findByRole("heading", { name: "Holidays" })).toBeInTheDocument();
    expect(requestSentTo(fetchMock, `PATCH /api/accounts/${ACCOUNT_ID}`).body).toEqual({ name: "Holidays" });
  });

  it("refuses a name longer than 60 characters without calling the API", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account()),
      ...statementRoutes,
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Edit name" }));
    await user.type(screen.getByLabelText("Account name"), "x".repeat(61));
    await user.click(screen.getByRole("button", { name: "Save name" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The name can have at most 60 characters.");
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "PATCH")).toBe(false);
  });

  it("deactivates the account, shows the inactive notice and disables the movement forms", async () => {
    let current = account();
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, current),
      ...statementRoutes,
      [`POST /api/accounts/${ACCOUNT_ID}/deactivate`]: () => {
        current = account({ status: "Blocked" });
        return jsonResponse(200, current);
      },
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    await userEvent.setup().click(screen.getByRole("button", { name: "Deactivate account" }));

    expect(await screen.findByRole("status")).toHaveTextContent("This account is inactive.");
    expect(screen.getByText("Status: Inactive")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Deposit" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Withdraw" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Send transfer" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Activate account" })).toBeInTheDocument();
  });

  it("activates an inactive account again and re-enables the movements", async () => {
    let current = account({ status: "Blocked" });
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, current),
      ...statementRoutes,
      [`POST /api/accounts/${ACCOUNT_ID}/activate`]: () => {
        current = account();
        return jsonResponse(200, current);
      },
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("button", { name: "Activate account" });

    await userEvent.setup().click(screen.getByRole("button", { name: "Activate account" }));

    expect(await screen.findByRole("button", { name: "Deactivate account" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deposit" })).toBeEnabled();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("shows the API's reason when the status cannot be changed", async () => {
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account()),
      ...statementRoutes,
      [`POST /api/accounts/${ACCOUNT_ID}/deactivate`]: () => jsonResponse(500, { title: "Server error" }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    await userEvent.setup().click(screen.getByRole("button", { name: "Deactivate account" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Server error");
  });
});

describe("back link", () => {
  it("links to the home page with the text 'Back to your accounts'", () => {
    renderWithAuth(<BackLink />);

    const link = within(screen.getByRole("link", { name: "Back to your accounts" }));
    expect(link.getByText("Back to your accounts")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to your accounts" })).toHaveAttribute("href", "/");
  });
});

// Keeps the waitFor import used even when no async assertion needs it.
void waitFor;
