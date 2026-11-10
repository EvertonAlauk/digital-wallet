import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AccountPage from "@/app/(protected)/account/page";
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
const OPERATION_ID = "0b1d0000-0000-4000-8000-000000000042";
const OTHER_ACCOUNT_ID = "5a710000-0000-4000-8000-000000000009";

function account(balance: number) {
  return {
    id: ACCOUNT_ID,
    ownerId: "a1ce0000-0000-4000-8000-000000000001",
    name: "Savings",
    status: "Active",
    balance,
    currency: "BRL",
    createdAt: "2026-01-02T10:00:00Z",
  };
}

function operation(status: string, failureReason: string | null = null) {
  return {
    id: OPERATION_ID,
    accountId: ACCOUNT_ID,
    kind: "Deposit",
    status,
    failureReason,
    transactionId: null,
    createdAt: "2026-01-02T10:00:00Z",
    completedAt: null,
  };
}

const statementEntry = (amount: number, type = "Deposit") => ({
  transactionId: `tx-${type}-${amount}`,
  type,
  amount,
  currency: "BRL",
  createdAt: "2026-01-02T10:00:00Z",
});

beforeEach(() => {
  navigation.search = new URLSearchParams(`id=${ACCOUNT_ID}`);
});

/** Holds the balance the fake API reports, so a completed movement is visible on the next read. */
function bank(initialBalance: number) {
  let balance = initialBalance;
  return {
    get balance() {
      return balance;
    },
    setBalance(value: number) {
      balance = value;
    },
  };
}

describe("account page", () => {
  it("asks for an id when the query string has none, and does not call the API", async () => {
    navigation.search = new URLSearchParams();
    const fetchMock = routeFetch({});

    renderWithAuth(<AccountPage />);

    expect(await screen.findByRole("heading", { name: "Account not found" })).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([input]) => String(input).startsWith("/api/accounts"))).toBe(false);
  });

  it("shows not found when the API does not know the account (or it belongs to someone else)", async () => {
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(404, { title: "Account not found" }),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(404, { title: "Account not found" }),
    });

    renderWithAuth(<AccountPage />);

    expect(await screen.findByRole("heading", { name: "Account not found" })).toBeInTheDocument();
  });

  it("shows the name, balance and id of the account", async () => {
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account(150)),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    });

    renderWithAuth(<AccountPage />);

    expect(await screen.findByRole("heading", { name: "Savings" })).toBeInTheDocument();
    expect(screen.getByLabelText("Balance")).toHaveTextContent("150.00");
    expect(screen.getByText(ACCOUNT_ID)).toBeInTheDocument();
  });

  it("deposits: sends the amount with an Idempotency-Key, follows the operation, and shows the new balance", async () => {
    const state = bank(0);
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account(state.balance)),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
      [`POST /api/accounts/${ACCOUNT_ID}/deposits`]: () => {
        state.setBalance(100);
        return jsonResponse(202, { operationId: OPERATION_ID, status: "Accepted" });
      },
      [`GET /api/operations/${OPERATION_ID}`]: () => jsonResponse(200, operation("Completed")),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const deposit = within(screen.getByRole("form", { name: "Deposit" }));
    await userEvent.setup().type(deposit.getByLabelText("Amount (BRL)"), "100");
    await userEvent.setup().click(deposit.getByRole("button", { name: "Deposit" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Deposit: completed.");
    await waitFor(() => expect(screen.getByLabelText("Balance")).toHaveTextContent("100.00"));
    const sent = requestSentTo(fetchMock, `POST /api/accounts/${ACCOUNT_ID}/deposits`);
    expect(sent.body).toEqual({ amount: 100 });
    expect(sent.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/);
  });

  it("withdraws: a rejected operation is shown with the reason", async () => {
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account(10)),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
      [`POST /api/accounts/${ACCOUNT_ID}/withdrawals`]: () => jsonResponse(202, { operationId: OPERATION_ID, status: "Accepted" }),
      [`GET /api/operations/${OPERATION_ID}`]: () => jsonResponse(200, operation("Rejected", "Insufficient funds")),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const withdrawal = within(screen.getByRole("form", { name: "Withdraw" }));
    await userEvent.setup().type(withdrawal.getByLabelText("Amount (BRL)"), "50");
    await userEvent.setup().click(withdrawal.getByRole("button", { name: "Withdraw" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Withdrawal: rejected. Insufficient funds");
  });

  it("does not call the API for an amount that is not valid", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account(10)),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const deposit = within(screen.getByRole("form", { name: "Deposit" }));
    await userEvent.setup().type(deposit.getByLabelText("Amount (BRL)"), "10.123");
    await userEvent.setup().click(deposit.getByRole("button", { name: "Deposit" }));

    expect(await deposit.findByRole("alert")).toHaveTextContent("at most two decimal places");
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
  });

  it("transfers to another account by id", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account(100)),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
      "POST /api/transfers": () => jsonResponse(202, { operationId: OPERATION_ID, status: "Accepted" }),
      [`GET /api/operations/${OPERATION_ID}`]: () => jsonResponse(200, operation("Completed")),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const transfer = within(screen.getByRole("form", { name: "Transfer to another account" }));
    const user = userEvent.setup();
    await user.type(transfer.getByLabelText("Destination account id"), OTHER_ACCOUNT_ID);
    await user.type(transfer.getByLabelText("Amount (BRL)"), "25.5");
    await user.click(transfer.getByRole("button", { name: "Send transfer" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Transfer: completed.");
    expect(requestSentTo(fetchMock, "POST /api/transfers").body).toEqual({
      sourceAccountId: ACCOUNT_ID,
      destinationAccountId: OTHER_ACCOUNT_ID,
      amount: 25.5,
    });
  });

  it("refuses a destination that is not an account id, without calling the API", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account(100)),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const transfer = within(screen.getByRole("form", { name: "Transfer to another account" }));
    const user = userEvent.setup();
    await user.type(transfer.getByLabelText("Destination account id"), "bob");
    await user.type(transfer.getByLabelText("Amount (BRL)"), "5");
    await user.click(transfer.getByRole("button", { name: "Send transfer" }));

    expect(await transfer.findByRole("alert")).toHaveTextContent("Enter the destination account id");
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
  });

  it("lists the statement with the direction of each entry", async () => {
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account(20)),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () =>
        jsonResponse(200, { items: [statementEntry(30), statementEntry(-10, "Transfer")], nextCursor: null }),
    });

    renderWithAuth(<AccountPage />);

    const table = await screen.findByRole("table");
    expect(within(table).getByText("Transfer")).toBeInTheDocument();
    expect(within(table).getByText("In", { selector: "td" })).toBeInTheDocument();
    expect(within(table).getByText("Out", { selector: "td" })).toBeInTheDocument();
  });

  it("loads older entries with the cursor from the previous page", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account(60)),
      [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () =>
        jsonResponse(200, { items: [statementEntry(30)], nextCursor: "cursor-2" }),
      [`GET /api/accounts/${ACCOUNT_ID}/statement?cursor=cursor-2`]: () =>
        jsonResponse(200, { items: [statementEntry(30, "Withdrawal")], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("table");

    await userEvent.setup().click(screen.getByRole("button", { name: "Load older entries" }));

    expect(await screen.findByText("Withdrawal")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Load older entries" })).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith("?cursor=cursor-2"))).toBe(true);
  });
});
