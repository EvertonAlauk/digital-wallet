import { ApiError } from "@/lib/api/client";

export const MIN_PASSWORD_LENGTH = 8;
export const MAX_PASSWORD_LENGTH = 128;
export const MAX_NAME_LENGTH = 60;

export interface RegisterForm {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export type RegisterFieldErrors = Partial<Record<keyof RegisterForm, string>>;

const EMAIL_PATTERN = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

/** Same rules as the BFF, so the form explains a problem before the request is sent. */
export function validateRegisterForm(form: RegisterForm): RegisterFieldErrors {
  const errors: RegisterFieldErrors = {};

  if (form.firstName.trim() === "" || form.firstName.trim().length > MAX_NAME_LENGTH) {
    errors.firstName = `Enter a first name of up to ${MAX_NAME_LENGTH} characters.`;
  }

  if (form.lastName.trim() === "" || form.lastName.trim().length > MAX_NAME_LENGTH) {
    errors.lastName = `Enter a last name of up to ${MAX_NAME_LENGTH} characters.`;
  }

  const email = form.email.trim();
  if (email === "" || email.length > 254 || !EMAIL_PATTERN.test(email)) {
    errors.email = "Enter a valid email address. It will be your username.";
  }

  if (form.password.length < MIN_PASSWORD_LENGTH || form.password.length > MAX_PASSWORD_LENGTH) {
    errors.password = `The password must have between ${MIN_PASSWORD_LENGTH} and ${MAX_PASSWORD_LENGTH} characters.`;
  }

  if (form.confirmPassword !== form.password) {
    errors.confirmPassword = "The passwords do not match.";
  }

  return errors;
}

/** Message for a failed registration. The 409 says the email is taken and points to sign-in. */
export function registrationErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) {
      return "An account with this email already exists. Sign in instead.";
    }

    if (error.status === 400) {
      return "The identity provider did not accept these details. Check the password and try again.";
    }

    if (error.status === 429) {
      return "Too many attempts. Wait a minute and try again.";
    }
  }

  return "Could not create the account right now. Try again.";
}
