"use client";

import Link from "next/link";
import { Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { AccountDetail } from "@/components/accounts/account-detail";
import { isAccountId } from "@/lib/accounts/ids";

/**
 * One account, read from ?id=. The id is a query parameter instead of a path segment, because a static export cannot
 * list every account id at build time.
 */
function AccountPageContent() {
  const id = useSearchParams().get("id");

  if (!id || !isAccountId(id)) {
    return (
      <section>
        <h1>Account not found</h1>
        <p>Choose an account from your accounts list.</p>
        <Link href="/">Back to your accounts</Link>
      </section>
    );
  }

  return <AccountDetail accountId={id.trim()} />;
}

export default function AccountPage() {
  return (
    <Suspense fallback={<p>Loading account…</p>}>
      <AccountPageContent />
    </Suspense>
  );
}
