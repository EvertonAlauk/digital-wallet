import { RegisterForm } from "./register-form";
import styles from "../login/login.module.css";

export default function RegisterPage() {
  return (
    <main className={styles.page}>
      <RegisterForm />
    </main>
  );
}
