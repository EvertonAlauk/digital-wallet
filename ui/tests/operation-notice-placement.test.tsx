import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import AccountPage from "@/app/(protected)/account/page";
import { jsonResponse, renderWithAuth, routeFetch } from "./helpers";

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

const account = {
  id: ACCOUNT_ID,
  ownerId: "a1ce0000-0000-4000-8000-000000000001",
  name: "Savings",
  status: "Active",
  balance: 100,
  currency: "BRL",
  createdAt: "2026-01-02T10:00:00Z",
};

function operation(status: string) {
  return {
    id: OPERATION_ID,
    accountId: ACCOUNT_ID,
    kind: "Transfer",
    status,
    failureReason: null,
    transactionId: null,
    createdAt: "2026-01-02T10:00:00Z",
    completedAt: null,
  };
}

function setup() {
  navigation.search = new URLSearchParams(`id=${ACCOUNT_ID}`);
  return routeFetch({
    [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account),
    [`GET /api/accounts/${ACCOUNT_ID}/statement`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    "POST /api/transfers": () => jsonResponse(202, { operationId: OPERATION_ID, status: "Accepted" }),
    [`POST /api/accounts/${ACCOUNT_ID}/deposits`]: () => jsonResponse(202, { operationId: OPERATION_ID, status: "Accepted" }),
    [`GET /api/operations/${OPERATION_ID}`]: () => jsonResponse(200, operation("Completed")),
  });
}

describe("operation outcome placement", () => {
  it("shows a transfer outcome inside the transfer box, not the deposit and withdraw box", async () => {
    setup();
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const transfer = within(screen.getByRole("form", { name: "Transfer to another account" }));
    const user = userEvent.setup();
    await user.type(transfer.getByLabelText("Destination account id"), "5a710000-0000-4000-8000-000000000009");
    await user.type(transfer.getByLabelText("Amount (BRL)"), "5");
    await user.click(transfer.getByRole("button", { name: "Send transfer" }));

    const transferBox = screen.getByRole("region", { name: "Transfer money" });
    await waitFor(() => expect(within(transferBox).getByRole("status")).toHaveTextContent("Transfer: completed."));
    expect(within(screen.getByRole("region", { name: "Move money" })).queryByRole("status")).not.toBeInTheDocument();
  });

  it("shows a deposit outcome inside the deposit and withdraw box", async () => {
    setup();
    renderWithAuth(<AccountPage />);
    await screen.findByRole("heading", { name: "Savings" });

    const deposit = within(screen.getByRole("form", { name: "Deposit" }));
    await userEvent.setup().type(deposit.getByLabelText("Amount (BRL)"), "10");
    await userEvent.setup().click(deposit.getByRole("button", { name: "Deposit" }));

    const movementBox = screen.getByRole("region", { name: "Move money" });
    await waitFor(() => expect(within(movementBox).getByRole("status")).toHaveTextContent("Deposit: completed."));
    expect(within(screen.getByRole("region", { name: "Transfer money" })).queryByRole("status")).not.toBeInTheDocument();
  });
});
