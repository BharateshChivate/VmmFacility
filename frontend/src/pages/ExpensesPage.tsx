import { useEffect, useState } from "react";
import { api, ApiError, type Expense } from "../api";
import { formatDate, formatInr } from "../format";
import { consumeFlash, Link } from "../router";
import { Button, ButtonLink, Notice, PageHeader, StatusBadge, type NoticeTone } from "../components/ui";

export function ExpensesPage({ flash }: { flash?: string }) {
  const [expenses, setExpenses] = useState<Expense[] | null>(null);
  const [loadError, setLoadError] = useState<string>();
  const [notice, setNotice] = useState<{ tone: NoticeTone; text: string } | undefined>(
    flash ? { tone: "success", text: flash } : undefined,
  );
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    consumeFlash();
    api
      .listExpenses()
      .then(setExpenses)
      .catch((err) => setLoadError(err instanceof ApiError ? err.message : "Couldn't load expenses."));
  }, []);

  async function handleExport() {
    if (exporting) return;
    setExporting(true);
    setNotice(undefined);
    try {
      await api.downloadReport();
      setNotice({ tone: "success", text: "PDF report download started." });
    } catch (err) {
      const reason = err instanceof ApiError ? err.message : "";
      setNotice({ tone: "error", text: `Couldn't create the PDF report. ${reason}`.trim() });
    } finally {
      setExporting(false);
    }
  }

  return (
    <>
      <PageHeader
        title="Expenses"
        actions={
          <>
            <Button onClick={handleExport} disabled={exporting || !expenses}>
              {exporting ? "Preparing PDF…" : "Export PDF"}
            </Button>
            <ButtonLink href="/expenses/new" variant="primary">
              Add expense
            </ButtonLink>
          </>
        }
      />

      {notice && <Notice tone={notice.tone}>{notice.text}</Notice>}
      {loadError && <Notice tone="error">{loadError}</Notice>}
      {!expenses && !loadError && <p className="loading">Loading expenses…</p>}

      {expenses && expenses.length === 0 && (
        <section className="surface empty-state">
          <h2>No expenses recorded yet</h2>
          <p>Expenses you add will appear here, newest first.</p>
          <ButtonLink href="/expenses/new" variant="primary">
            Add expense
          </ButtonLink>
        </section>
      )}

      {expenses && expenses.length > 0 && (
        <div className="surface table-wrap">
          <table className="ledger">
            <caption className="visually-hidden">Expenses, newest first</caption>
            <thead>
              <tr>
                <th scope="col">Date</th>
                <th scope="col">Description</th>
                <th scope="col">Category</th>
                <th scope="col" className="num">
                  Amount (INR)
                </th>
                <th scope="col">Payment state</th>
                <th scope="col" className="ledger__actions">
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {expenses.map((e) => (
                <tr key={e.id}>
                  <td className="nowrap">{formatDate(e.date)}</td>
                  <td className="ledger__description">
                    <Link href={`/expenses/${e.id}`}>{e.description}</Link>
                  </td>
                  <td className="nowrap">{e.category}</td>
                  <td className="num">{formatInr(e.amount)}</td>
                  <td>
                    <StatusBadge state={e.paymentState} />
                  </td>
                  <td className="ledger__actions">
                    <Link href={`/expenses/${e.id}/edit`} aria-label={`Edit expense: ${e.description}`}>
                      Edit
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
