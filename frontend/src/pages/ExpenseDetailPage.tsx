import { useEffect, useState } from "react";
import { api, ApiError, type Expense } from "../api";
import { formatDate, formatDateTime, formatInr } from "../format";
import { BackLink, ButtonLink, Notice, PageHeader, StatusBadge } from "../components/ui";

export function ExpenseDetailPage({ id }: { id: number }) {
  const [expense, setExpense] = useState<Expense>();
  const [error, setError] = useState<string>();

  useEffect(() => {
    api
      .getExpense(id)
      .then(setExpense)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Couldn't load this expense."));
  }, [id]);

  return (
    <>
      <BackLink />
      <PageHeader
        title="Expense"
        actions={
          expense && (
            <ButtonLink href={`/expenses/${id}/edit`} variant="primary">
              Edit expense
            </ButtonLink>
          )
        }
      />
      {error && <Notice tone="error">{error}</Notice>}
      {!expense && !error && <p className="loading">Loading expense…</p>}
      {expense && (
        <section className="surface form-surface">
          <dl className="detail-list">
            <div>
              <dt>Amount (INR)</dt>
              <dd>{formatInr(expense.amount)}</dd>
            </div>
            <div>
              <dt>Date</dt>
              <dd>{formatDate(expense.date)}</dd>
            </div>
            <div>
              <dt>Category</dt>
              <dd>{expense.category}</dd>
            </div>
            <div>
              <dt>Payment state</dt>
              <dd>
                <StatusBadge state={expense.paymentState} />
              </dd>
            </div>
            <div className="field--full">
              <dt>Description</dt>
              <dd>{expense.description}</dd>
            </div>
          </dl>
          <p className="detail-meta">
            Added {formatDateTime(expense.createdAt)} · Last updated {formatDateTime(expense.updatedAt)}
          </p>
        </section>
      )}
    </>
  );
}
