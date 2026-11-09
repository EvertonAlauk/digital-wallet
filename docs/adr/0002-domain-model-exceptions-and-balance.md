# ADR 0002: Domain model, error handling, and balance as a cache

## Decisions

### 1. A business rule violation throws a domain exception

Every violation inherits from `DomainException` (e.g. `InsufficientFundsException`,
`AccountBlockedException`, `UnbalancedTransactionException`, `CurrencyMismatchException`,
`InvalidAmountException`, `InvalidTransactionException`).

Reasons:

- The rules are invariants: an account with a negative balance or an unbalanced entry set is a state that **must not exist**, not an expected outcome to be handled.
- With `Result`, every domain method and every caller would have to propagate the error by hand. Because the rules are chained (`Transaction.Create` -> `Account.ApplyEntry`), that would spread noise through the code.
- The API translates `DomainException` into `ProblemDetails` (422) in a single place, the middleware.

Accepted trade-off: exceptions cost more than `Result` and do not appear in the method signature. That is why the exceptions are typed and methods document which ones they throw.

Invalid programming arguments (e.g. a malformed currency code) throw `ArgumentException`, not
`DomainException`, because they indicate a bug in the caller, not a business rule.

### 2. `Money` is a `readonly record struct` with a `decimal` and a currency

- Value equality, immutable, no `double`/`float`.
- Operations between different currencies throw `CurrencyMismatchException`.
- Values may be negative (debit entries use the sign). The "positive value" rule is enforced by the callers that require it, via `EnsurePositive()`, not by the type. This keeps `Money` useful for sums and for signed ledger entries.

### 3. The balance changes only through `Account.ApplyEntry(LedgerEntry)`

- `Balance` has a private setter. There is no public `Credit`/`Debit` that changes the balance without a ledger entry.
- `ApplyEntry` validates, in this order: the entry's account, account blocked, currency, and non-negative balance. Only then does it change the balance and increment `Version`. If any validation fails, the state does not change.

### 4. A transaction validates its set of entries before creating them

`Transaction.Create` requires: at least two entries, non-zero amounts, the same currency, and a sum of zero. `Transaction.Transfer` produces the debit/credit pair with the same amount and rejects a source account equal to the destination account.

`LedgerEntry` is a `record` with read-only properties. There is no mutating operation. Corrections are made by reversal (`Transaction.MarkAsReversed` marks the original; the reversal is a new transaction).

### 5. Reversing is only a status change

`MarkAsReversed` changes only the `Status` of the original transaction, from `Completed` to `Reversed`. The original entries do not change. Reversals cannot be reversed.

## Open questions

- **Deposits and withdrawals.** Both are double-entry, so they need a counterparty (a settlement account, e.g. the bank's account). That account cannot carry a negative balance like a customer account, so it needs its own type. This decision is deferred to the application-layer ADR, when the deposit flow is implemented. For now, `Transaction.Create` accepts any balanced set of entries, and the balance rule applies to every account that receives `ApplyEntry`.
- **Atomicity across accounts.** A transfer affects two accounts. The domain rules guarantee each account in isolation. Atomicity (the same database transaction, locks/versions) is the responsibility of the application layer and the infrastructure.
- **Ledger entry identity.** `LedgerEntry` has no `Id` of its own, because the specified model does not ask for one. Persistence may require a key; if it does, it will be added with its own ADR.
