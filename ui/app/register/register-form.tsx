"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { registerRequest } from "@/lib/auth/session-api";
import { registrationErrorMessage, validateRegisterForm, type RegisterFieldErrors } from "@/lib/auth/register-rules";
import styles from "../login/login.module.css";

const EMPTY = { firstName: "", lastName: "", email: "", password: "", confirmPassword: "" };

export function RegisterForm() {
  const router = useRouter();
  const [form, setForm] = useState(EMPTY);
  const [fieldErrors, setFieldErrors] = useState<RegisterFieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  function update(field: keyof typeof EMPTY, value: string) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const errors = validateRegisterForm(form);
    setFieldErrors(errors);
    setFormError(null);
    if (Object.keys(errors).length > 0) {
      return;
    }

    setSubmitting(true);
    try {
      await registerRequest({
        email: form.email.trim(),
        password: form.password,
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
      });
      router.replace("/login?registered=1");
    } catch (error) {
      setFormError(registrationErrorMessage(error));
      setForm((current) => ({ ...current, password: "", confirmPassword: "" }));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className={styles.form} onSubmit={handleSubmit} noValidate aria-labelledby="register-title">
      <h1 id="register-title" className={styles.title}>
        Create your wallet account
      </h1>

      <div className={styles.field}>
        <label htmlFor="register-first-name">First name</label>
        <input
          id="register-first-name"
          autoComplete="given-name"
          value={form.firstName}
          onChange={(event) => update("firstName", event.target.value)}
          aria-invalid={fieldErrors.firstName ? true : undefined}
          aria-describedby={fieldErrors.firstName ? "register-first-name-error" : undefined}
          disabled={submitting}
        />
        {fieldErrors.firstName && (
          <p id="register-first-name-error" className={styles.fieldError}>
            {fieldErrors.firstName}
          </p>
        )}
      </div>

      <div className={styles.field}>
        <label htmlFor="register-last-name">Last name</label>
        <input
          id="register-last-name"
          autoComplete="family-name"
          value={form.lastName}
          onChange={(event) => update("lastName", event.target.value)}
          aria-invalid={fieldErrors.lastName ? true : undefined}
          aria-describedby={fieldErrors.lastName ? "register-last-name-error" : undefined}
          disabled={submitting}
        />
        {fieldErrors.lastName && (
          <p id="register-last-name-error" className={styles.fieldError}>
            {fieldErrors.lastName}
          </p>
        )}
      </div>

      <div className={styles.field}>
        <label htmlFor="register-email">Email (your username)</label>
        <input
          id="register-email"
          type="email"
          autoComplete="email"
          value={form.email}
          onChange={(event) => update("email", event.target.value)}
          aria-invalid={fieldErrors.email ? true : undefined}
          aria-describedby={fieldErrors.email ? "register-email-error" : undefined}
          disabled={submitting}
        />
        {fieldErrors.email && (
          <p id="register-email-error" className={styles.fieldError}>
            {fieldErrors.email}
          </p>
        )}
      </div>

      <div className={styles.field}>
        <label htmlFor="register-password">Password</label>
        <input
          id="register-password"
          type="password"
          autoComplete="new-password"
          value={form.password}
          onChange={(event) => update("password", event.target.value)}
          aria-invalid={fieldErrors.password ? true : undefined}
          aria-describedby={fieldErrors.password ? "register-password-error" : undefined}
          disabled={submitting}
        />
        {fieldErrors.password && (
          <p id="register-password-error" className={styles.fieldError}>
            {fieldErrors.password}
          </p>
        )}
      </div>

      <div className={styles.field}>
        <label htmlFor="register-confirm-password">Confirm password</label>
        <input
          id="register-confirm-password"
          type="password"
          autoComplete="new-password"
          value={form.confirmPassword}
          onChange={(event) => update("confirmPassword", event.target.value)}
          aria-invalid={fieldErrors.confirmPassword ? true : undefined}
          aria-describedby={fieldErrors.confirmPassword ? "register-confirm-password-error" : undefined}
          disabled={submitting}
        />
        {fieldErrors.confirmPassword && (
          <p id="register-confirm-password-error" className={styles.fieldError}>
            {fieldErrors.confirmPassword}
          </p>
        )}
      </div>

      {formError && (
        <p role="alert" className={styles.formError}>
          {formError}
        </p>
      )}

      <button type="submit" className={styles.submit} disabled={submitting}>
        {submitting ? "Creating account…" : "Create account"}
      </button>

      <Link href="/login">Already have an account? Sign in</Link>
    </form>
  );
}
