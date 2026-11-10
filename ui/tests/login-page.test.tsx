import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { LoginForm } from "@/app/login/login-form";
import { aliceUser, jsonResponse, renderWithAuth, requestSentTo, routeFetch } from "./helpers";

const navigation = vi.hoisted(() => ({
  router: { replace: vi.fn(), push: vi.fn() },
  search: new URLSearchParams(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => navigation.router,
  usePathname: () => "/login",
  useSearchParams: () => navigation.search,
}));

const sessionMissing = () => jsonResponse(401, { title: "Not signed in" });

beforeEach(() => {
  navigation.router.replace.mockReset();
  navigation.search = new URLSearchParams();
});

async function fillAndSubmit(username: string, password: string) {
  const user = userEvent.setup();
  // userEvent cannot type an empty string, so an empty field is left as it is.
  if (username !== "") {
    await user.type(screen.getByLabelText("Username"), username);
  }

  if (password !== "") {
    await user.type(screen.getByLabelText("Password"), password);
  }

  await user.click(screen.getByRole("button", { name: "Sign in" }));
}

describe("login page", () => {
  it("signs in and goes to the return URL", async () => {
    navigation.search = new URLSearchParams("returnUrl=/accounts/123");
    const fetchMock = routeFetch({
      "GET /bff/session": sessionMissing,
      "POST /bff/login": () => jsonResponse(200, { user: aliceUser }),
    });
    renderWithAuth(<LoginForm />);
    await screen.findByLabelText("Username");

    await fillAndSubmit("alice", "alice-dev-password");

    await waitFor(() => expect(navigation.router.replace).toHaveBeenCalledWith("/accounts/123"));
    const sent = requestSentTo(fetchMock, "POST /bff/login");
    expect(sent.body).toEqual({ username: "alice", password: "alice-dev-password" });
    expect(sent.headers.get("X-CSRF")).toBe("1");
  });

  it("shows a generic message on a wrong password and clears the password field", async () => {
    routeFetch({
      "GET /bff/session": sessionMissing,
      "POST /bff/login": () => jsonResponse(401, { detail: "Invalid username or password" }),
    });
    renderWithAuth(<LoginForm />);
    await screen.findByLabelText("Username");

    await fillAndSubmit("alice", "wrong");

    expect(await screen.findByRole("alert")).toHaveTextContent("Invalid username or password.");
    expect(screen.getByLabelText("Password")).toHaveValue("");
    expect(navigation.router.replace).not.toHaveBeenCalled();
  });

  it("explains the rate limit on 429", async () => {
    routeFetch({
      "GET /bff/session": sessionMissing,
      "POST /bff/login": () => jsonResponse(429, { title: "Too many requests" }),
    });
    renderWithAuth(<LoginForm />);
    await screen.findByLabelText("Username");

    await fillAndSubmit("alice", "alice-dev-password");

    expect(await screen.findByRole("alert")).toHaveTextContent("Too many attempts. Wait a minute and try again.");
  });

  it("validates required fields before calling the BFF", async () => {
    const fetchMock = routeFetch({ "GET /bff/session": sessionMissing });
    renderWithAuth(<LoginForm />);
    await screen.findByLabelText("Username");

    await fillAndSubmit("", "");

    expect(await screen.findByText("Enter your username.")).toBeInTheDocument();
    expect(screen.getByText("Enter your password.")).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([input]) => String(input) === "/bff/login")).toBe(false);
  });

  it("ignores an external return URL and goes to the home page", async () => {
    navigation.search = new URLSearchParams("returnUrl=https://evil.example/phish");
    routeFetch({
      "GET /bff/session": sessionMissing,
      "POST /bff/login": () => jsonResponse(200, { user: aliceUser }),
    });
    renderWithAuth(<LoginForm />);
    await screen.findByLabelText("Username");

    await fillAndSubmit("alice", "alice-dev-password");

    await waitFor(() => expect(navigation.router.replace).toHaveBeenCalledWith("/"));
    expect(navigation.router.replace).not.toHaveBeenCalledWith("https://evil.example/phish");
  });

  it("sends an already signed-in user straight to the return URL", async () => {
    navigation.search = new URLSearchParams("returnUrl=/accounts");
    routeFetch({ "GET /bff/session": () => jsonResponse(200, { user: aliceUser }) });

    renderWithAuth(<LoginForm />);

    await waitFor(() => expect(navigation.router.replace).toHaveBeenCalledWith("/accounts"));
  });
});
