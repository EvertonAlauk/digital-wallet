import { ApiError } from "@/lib/api/client";

export interface LoginFieldErrors {
  username?: string;
  password?: string;
}

export function validateLoginFields(username: string, password: string): LoginFieldErrors {
  const errors: LoginFieldErrors = {};
  if (username.trim() === "") {
    errors.username = "Enter your username.";
  }

  if (password === "") {
    errors.password = "Enter your password.";
  }

  return errors;
}

/**
 * Message for a failed sign-in. The wrong-password and locked-account cases share one message on purpose, so the
 * form does not reveal which accounts exist or are locked.
 */
export function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) {
      return "Invalid username or password.";
    }

    if (error.status === 429) {
      return "Too many attempts. Wait a minute and try again.";
    }
  }

  return "Could not sign in right now. Try again.";
}
