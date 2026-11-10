import { describe, expect, it } from "vitest";
import { waitForOperation } from "@/lib/api/operations";
import { jsonResponse, routeFetch } from "./helpers";

const OPERATION_ID = "0b1d0000-0000-4000-8000-000000000042";

function operation(status: string, failureReason: string | null = null) {
  return {
    id: OPERATION_ID,
    accountId: "a1ce0000-0000-4000-8000-000000000001",
    kind: "Deposit",
    status,
    failureReason,
    transactionId: null,
    createdAt: "2026-01-01T00:00:00Z",
    completedAt: null,
  };
}

describe("waitForOperation", () => {
  it("polls until the operation is completed", async () => {
    const answers = [operation("Accepted"), operation("Accepted"), operation("Completed")];
    const fetchMock = routeFetch({
      [`GET /api/operations/${OPERATION_ID}`]: () => jsonResponse(200, answers.shift()),
    });

    const result = await waitForOperation(OPERATION_ID, { intervalMs: 0 });

    expect(result.status).toBe("Completed");
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("returns a rejected operation with its reason", async () => {
    routeFetch({
      [`GET /api/operations/${OPERATION_ID}`]: () =>
        jsonResponse(200, operation("Rejected", "Insufficient funds")),
    });

    const result = await waitForOperation(OPERATION_ID, { intervalMs: 0 });

    expect(result).toMatchObject({ status: "Rejected", failureReason: "Insufficient funds" });
  });

  it("gives up after the maximum attempts and returns the last state", async () => {
    const fetchMock = routeFetch({
      [`GET /api/operations/${OPERATION_ID}`]: () => jsonResponse(200, operation("Accepted")),
    });

    const result = await waitForOperation(OPERATION_ID, { intervalMs: 0, maxAttempts: 3 });

    expect(result.status).toBe("Accepted");
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });
});
