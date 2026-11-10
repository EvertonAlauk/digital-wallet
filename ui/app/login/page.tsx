import { Suspense } from "react";
import { LoginForm } from "./login-form";
import styles from "./login.module.css";

export default function LoginPage() {
  return (
    <main className={styles.page}>
      {/* The form reads the returnUrl parameter, which needs a Suspense boundary for static export. */}
      <Suspense fallback={null}>
        <LoginForm />
      </Suspense>
    </main>
  );
}
