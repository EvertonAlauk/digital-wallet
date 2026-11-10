const HOME = "/";

/** Used only to resolve relative paths against a base that can never be the real origin. */
const PROBE_ORIGIN = "http://local.invalid";

/**
 * Returns a path inside this application, or "/" when the value could lead elsewhere. Rejected: absolute URLs, scheme
 * or host-relative tricks such as "//host" and "/\host", control characters that browsers strip, and the login page.
 */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.startsWith("/\\")) {
    return HOME;
  }

  if (/[\u0000-\u001f\u007f]/.test(value)) {
    return HOME;
  }

  try {
    const url = new URL(value, PROBE_ORIGIN);
    if (url.origin !== PROBE_ORIGIN || url.pathname.startsWith("/login")) {
      return HOME;
    }

    return `${url.pathname}${url.search}${url.hash}`;
  } catch {
    return HOME;
  }
}
