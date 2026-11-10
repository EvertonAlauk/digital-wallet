import Link from "next/link";
import styles from "./accounts.module.css";

/** The way back to the home page. White text on the accent colour, so it stands out on every page. */
export function BackLink() {
  return (
    <Link href="/" className={styles.backLink}>
      Back to your accounts
    </Link>
  );
}
