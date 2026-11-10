import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { LoginForm } from "@/app/login/login-form";
import { RegisterForm } from "@/app/register/register-form";
import { ApiError } from "@/lib/api/client";
import { registrationErrorMessage, validateRegisterForm, type RegisterForm as Form } from "@/lib/auth/register-rules";
import { jsonResponse, renderWithAuth, requestSentTo, routeFetch } from "./helpers";

const navigation = vi.hoisted(() => ({
  router: { replace: vi.fn(), push: vi.fn() },
  search: new URLSearchParams(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => navigation.router,
  usePathname: () => "/register",
  useSearchParams: () => navigation.search,
}));

const sessionMissing = () => jsonResponse(401, { title: "Not signed in" });

const valid: Form = {
  firstName: "Alice",
  lastName: "Customer",
  email: "alice@example.com",
  password: "a-strong-password",
  confirmPassword: "a-strong-password",
};

beforeEach(() => {
  navigation.router.replace.mockReset();
  navigation.search = new URLSearchParams();
});

async function fillForm(form: Partial<Form>) {
  const user = userEvent.setup();
  const values: Array<[string, string | undefined]> = [
    ["First name", form.firstName],
    ["Last name", form.lastName],
    ["Email (your username)", form.email],
    ["Password", form.password],
    ["Confirm password", form.confirmPassword],
  ];
  for (const [label, value] of values) {
    if (value) {
      await user.type(screen.getByLabelText(label), value);
    }
  }
  await user.click(screen.getByRole("button", { name: "Create account" }));
}

describe("register rules", () => {
  it("accepts a complete form", () => {
    expect(validateRegisterForm(valid)).toEqual({});
  });

  it("requires a valid email", () => {
    expect(validateRegisterForm({ ...valid, email: "not-an-email" })).toHaveProperty("email");
  });

  it("requires a password of at least 8 characters", () => {
    expect(validateRegisterForm({ ...valid, password: "short", confirmPassword: "short" })).toHaveProperty("password");
  });

  it("requires the two passwords to match", () => {
    expect(validateRegisterForm({ ...valid, confirmPassword: "different-password" })).toHaveProperty("confirmPassword");
  });

  it("explains a taken email with a pointer to sign in", () => {
    expect(registrationErrorMessage(new ApiError(409, "taken"))).toBe(
      "An account with this email already exists. Sign in instead.",
    );
  });
});

describe("register page", () => {
  it("creates the account and goes to the login page with the registered flag", async () => {
    const fetchMock = routeFetch({
      "GET /bff/session": sessionMissing,
      "POST /bff/register": () => new Response(null, { status: 201 }),
    });
    renderWithAuth(<RegisterForm />);
    await screen.findByLabelText("Email (your username)");

    await fillForm(valid);

    await waitFor(() => expect(navigation.router.replace).toHaveBeenCalledWith("/login?registered=1"));
    const sent = requestSentTo(fetchMock, "POST /bff/register");
    expect(sent.body).toEqual({
      email: "alice@example.com",
      password: "a-strong-password",
      firstName: "Alice",
      lastName: "Customer",
    });
    expect(sent.headers.get("X-CSRF")).toBe("1");
  });

  it("does not call the BFF when the passwords do not match", async () => {
    const fetchMock = routeFetch({ "GET /bff/session": sessionMissing });
    renderWithAuth(<RegisterForm />);
    await screen.findByLabelText("Email (your username)");

    await fillForm({ ...valid, confirmPassword: "different-password" });

    expect(await screen.findByText("The passwords do not match.")).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([input]) => String(input) === "/bff/register")).toBe(false);
  });

  it("shows that the email is taken, and clears the passwords", async () => {
    routeFetch({
      "GET /bff/session": sessionMissing,
      "POST /bff/register": () => jsonResponse(409, { title: "Account already exists" }),
    });
    renderWithAuth(<RegisterForm />);
    await screen.findByLabelText("Email (your username)");

    await fillForm(valid);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "An account with this email already exists. Sign in instead.",
    );
    expect(screen.getByLabelText("Password")).toHaveValue("");
    expect(navigation.router.replace).not.toHaveBeenCalled();
  });

  it("shows the identity provider's refusal on 400", async () => {
    routeFetch({
      "GET /bff/session": sessionMissing,
      "POST /bff/register": () => jsonResponse(400, { title: "Invalid request" }),
    });
    renderWithAuth(<RegisterForm />);
    await screen.findByLabelText("Email (your username)");

    await fillForm(valid);

    expect(await screen.findByRole("alert")).toHaveTextContent("The identity provider did not accept these details.");
  });

  it("links back to sign in", async () => {
    routeFetch({ "GET /bff/session": sessionMissing });
    renderWithAuth(<RegisterForm />);

    const link = await screen.findByRole("link", { name: "Already have an account? Sign in" });
    expect(link).toHaveAttribute("href", "/login");
  });
});

describe("login page after registration", () => {
  it("shows that the account was created, and links to the register page", async () => {
    navigation.search = new URLSearchParams("registered=1");
    routeFetch({ "GET /bff/session": sessionMissing });

    renderWithAuth(<LoginForm />);

    const form = within(await screen.findByRole("form", { name: "Sign in to your wallet" }));
    expect(form.getByText("Account created. Sign in with your email and password.")).toBeInTheDocument();
    expect(form.getByRole("link", { name: "Create an account" })).toHaveAttribute("href", "/register");
  });

  it("shows no notice without the registered flag", async () => {
    routeFetch({ "GET /bff/session": sessionMissing });

    renderWithAuth(<LoginForm />);

    await screen.findByLabelText("Username");
    expect(screen.queryByText("Account created. Sign in with your email and password.")).not.toBeInTheDocument();
  });
});
