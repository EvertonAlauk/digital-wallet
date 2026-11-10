import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AccountPage from "@/app/(protected)/account/page";
import {
  NO_STATEMENT_FILTER,
  hasStatementFilter,
  statementFilterError,
  statementQuery,
} from "@/lib/accounts/statement-filter";
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
const STATEMENT = `/api/accounts/${ACCOUNT_ID}/statement`;

const account = {
  id: ACCOUNT_ID,
  ownerId: "a1ce0000-0000-4000-8000-000000000001",
  name: "Savings",
  status: "Active",
  balance: 20,
  currency: "BRL",
  createdAt: "2026-01-02T10:00:00Z",
};

const entry = (amount: number, type: string, id: string) => ({
  transactionId: id,
  type,
  amount,
  currency: "BRL",
  createdAt: "2026-01-02T10:00:00Z",
});

beforeEach(() => {
  navigation.search = new URLSearchParams(`id=${ACCOUNT_ID}`);
});

describe("statement filter helpers", () => {
  it("builds no query when nothing is filtered", () => {
    expect(statementQuery(NO_STATEMENT_FILTER)).toBe("");
    expect(hasStatementFilter(NO_STATEMENT_FILTER)).toBe(false);
  });

  it("puts the filters first and the cursor last", () => {
    expect(statementQuery({ type: "Deposit", from: "2026-01-01", to: "2026-01-31" }, "abc")).toBe(
      "?type=Deposit&from=2026-01-01&to=2026-01-31&cursor=abc",
    );
  });

  it("refuses a start date after the end date", () => {
    expect(statementFilterError({ type: "", from: "2026-02-01", to: "2026-01-01" })).toBe(
      "The start date cannot be after the end date.",
    );
    expect(statementFilterError({ type: "", from: "2026-01-01", to: "2026-01-01" })).toBeNull();
  });
});

describe("statement filters on the account page", () => {
  it("filters by type: the request carries the type and the table shows only that kind", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account),
      [`GET ${STATEMENT}`]: () => jsonResponse(200, { items: [entry(30, "Deposit", "d1")], nextCursor: null }),
      [`GET ${STATEMENT}?type=Withdrawal`]: () => jsonResponse(200, { items: [entry(-5, "Withdrawal", "w1")], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("table");

    const filters = within(screen.getByRole("form", { name: "Statement filters" }));
    await userEvent.setup().selectOptions(filters.getByLabelText("Type"), "Withdrawal");
    await userEvent.setup().click(filters.getByRole("button", { name: "Apply filters" }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([input]) => String(input) === `${STATEMENT}?type=Withdrawal`)).toBe(true),
    );
    expect(await within(screen.getByRole("table")).findByText("Withdrawal")).toBeInTheDocument();
  });

  it("filters by a range of days, and sends both ends", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account),
      [`GET ${STATEMENT}`]: () => jsonResponse(200, { items: [], nextCursor: null }),
      [`GET ${STATEMENT}?from=2026-01-01&to=2026-01-31`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("table").catch(() => undefined);

    const filters = within(screen.getByRole("form", { name: "Statement filters" }));
    const user = userEvent.setup();
    await user.type(filters.getByLabelText("From"), "2026-01-01");
    await user.type(filters.getByLabelText("To"), "2026-01-31");
    await user.click(filters.getByRole("button", { name: "Apply filters" }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([input]) => String(input) === `${STATEMENT}?from=2026-01-01&to=2026-01-31`)).toBe(true),
    );
  });

  it("refuses an inverted range without calling the API", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account),
      [`GET ${STATEMENT}`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByText("No activity yet.");

    const filters = within(screen.getByRole("form", { name: "Statement filters" }));
    const user = userEvent.setup();
    await user.type(filters.getByLabelText("From"), "2026-02-01");
    await user.type(filters.getByLabelText("To"), "2026-01-01");
    await user.click(filters.getByRole("button", { name: "Apply filters" }));

    expect(await filters.findByRole("alert")).toHaveTextContent("The start date cannot be after the end date.");
    expect(fetchMock.mock.calls.filter(([input]) => String(input).includes("from="))).toHaveLength(0);
  });

  it("says so when a filter matches nothing", async () => {
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account),
      [`GET ${STATEMENT}`]: () => jsonResponse(200, { items: [entry(30, "Deposit", "d1")], nextCursor: null }),
      [`GET ${STATEMENT}?type=Transfer`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("table");

    const filters = within(screen.getByRole("form", { name: "Statement filters" }));
    await userEvent.setup().selectOptions(filters.getByLabelText("Type"), "Transfer");
    await userEvent.setup().click(filters.getByRole("button", { name: "Apply filters" }));

    expect(await screen.findByText("No entries match these filters.")).toBeInTheDocument();
  });

  it("clear removes the filters and shows the whole statement again", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account),
      [`GET ${STATEMENT}`]: () => jsonResponse(200, { items: [entry(30, "Deposit", "d1")], nextCursor: null }),
      [`GET ${STATEMENT}?type=Transfer`]: () => jsonResponse(200, { items: [], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("table");

    const filters = within(screen.getByRole("form", { name: "Statement filters" }));
    await userEvent.setup().selectOptions(filters.getByLabelText("Type"), "Transfer");
    await userEvent.setup().click(filters.getByRole("button", { name: "Apply filters" }));
    await screen.findByText("No entries match these filters.");

    await userEvent.setup().click(filters.getByRole("button", { name: "Clear" }));

    expect(await within(screen.getByRole("table")).findByText("Deposit")).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([input]) => String(input) === STATEMENT).length).toBeGreaterThanOrEqual(2);
  });

  it("keeps the filter when loading older entries", async () => {
    const fetchMock = routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account),
      [`GET ${STATEMENT}`]: () => jsonResponse(200, { items: [entry(30, "Deposit", "d1")], nextCursor: "c2" }),
      [`GET ${STATEMENT}?type=Deposit`]: () => jsonResponse(200, { items: [entry(30, "Deposit", "d1")], nextCursor: "c2" }),
      [`GET ${STATEMENT}?type=Deposit&cursor=c2`]: () => jsonResponse(200, { items: [entry(10, "Deposit", "d2")], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    await screen.findByRole("table");

    const filters = within(screen.getByRole("form", { name: "Statement filters" }));
    await userEvent.setup().selectOptions(filters.getByLabelText("Type"), "Deposit");
    await userEvent.setup().click(filters.getByRole("button", { name: "Apply filters" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Load older entries" })).toBeInTheDocument());

    await userEvent.setup().click(screen.getByRole("button", { name: "Load older entries" }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([input]) => String(input) === `${STATEMENT}?type=Deposit&cursor=c2`)).toBe(true),
    );
  });
});

describe("direction badges", () => {
  it("marks money in as In and money out as Out, as badges", async () => {
    routeFetch({
      [`GET /api/accounts/${ACCOUNT_ID}`]: () => jsonResponse(200, account),
      [`GET ${STATEMENT}`]: () =>
        jsonResponse(200, { items: [entry(30, "Deposit", "d1"), entry(-10, "Transfer", "t1")], nextCursor: null }),
    });
    renderWithAuth(<AccountPage />);
    const table = await screen.findByRole("table");

    expect(within(table).getByText("In", { selector: "span" }).className).toMatch(/badgeIn/);
    expect(within(table).getByText("Out", { selector: "span" }).className).toMatch(/badgeOut/);
  });
});
