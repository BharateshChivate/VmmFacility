import { useEffect, useMemo, useState, type FormEvent } from "react";
import { api, ApiError, type ExpenseInput, type FieldErrors } from "../api";
import { CATEGORIES, DESCRIPTION_MAX, FIELD_ORDER, PAYMENT_STATES, todayIso, validateAll, validateField } from "../expenseRules";
import { navigate, setNavigationGuard } from "../router";
import { BackLink, Button, Field, Notice, PageHeader, RadioField } from "../components/ui";

const EMPTY: ExpenseInput = { amount: "", date: todayIso(), category: "", description: "", paymentState: "" };

/** Add (no id) or edit (id) an expense. */
export function ExpenseFormPage({ id }: { id?: number }) {
  const isEdit = id !== undefined;
  const [initial, setInitial] = useState<ExpenseInput | undefined>(isEdit ? undefined : { ...EMPTY, date: todayIso() });
  const [values, setValues] = useState<ExpenseInput>(initial ?? EMPTY);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [touched, setTouched] = useState<Partial<Record<keyof ExpenseInput, boolean>>>({});
  const [formError, setFormError] = useState<string>();
  const [loadError, setLoadError] = useState<string>();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!isEdit) return;
    api
      .getExpense(id)
      .then((e) => {
        const loaded = {
          amount: e.amount,
          date: e.date,
          category: e.category,
          description: e.description,
          paymentState: e.paymentState,
        };
        setInitial(loaded);
        setValues(loaded);
      })
      .catch((err) => setLoadError(err instanceof ApiError ? err.message : "Couldn't load this expense."));
  }, [id, isEdit]);

  const dirty = useMemo(
    () => initial !== undefined && FIELD_ORDER.some((f) => values[f] !== initial[f]),
    [initial, values],
  );

  // Warn before discarding changes, both in-app and on reload/close.
  useEffect(() => {
    if (!dirty || saving) return;
    setNavigationGuard(() => window.confirm("Discard your unsaved changes?"));
    const onBeforeUnload = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => {
      setNavigationGuard(null);
      window.removeEventListener("beforeunload", onBeforeUnload);
    };
  }, [dirty, saving]);

  function update(field: keyof ExpenseInput, value: string) {
    const next = { ...values, [field]: value };
    setValues(next);
    if (touched[field] || errors[field]) setErrors((prev) => ({ ...prev, [field]: validateField(field, next) ?? "" }));
  }

  function blur(field: keyof ExpenseInput) {
    setTouched((prev) => ({ ...prev, [field]: true }));
    setErrors((prev) => ({ ...prev, [field]: validateField(field, values) ?? "" }));
  }

  function focusFirstError(fieldErrors: FieldErrors) {
    const first = FIELD_ORDER.find((f) => fieldErrors[f]);
    if (first) document.getElementById(first)?.focus();
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (saving) return;
    const clientErrors = validateAll(values);
    setErrors(clientErrors);
    setFormError(undefined);
    if (Object.keys(clientErrors).length > 0) {
      setFormError("Fix the highlighted fields to save this expense.");
      focusFirstError(clientErrors);
      return;
    }

    setSaving(true);
    try {
      const payload = { ...values, amount: values.amount.trim(), description: values.description.trim() };
      if (isEdit) await api.updateExpense(id, payload);
      else await api.createExpense(payload);
      navigate("/", { flash: isEdit ? "Expense updated." : "Expense saved.", skipGuard: true });
    } catch (err) {
      const apiErr = err instanceof ApiError ? err : undefined;
      setSaving(false);
      if (apiErr && Object.keys(apiErr.fieldErrors).length > 0) {
        setErrors(apiErr.fieldErrors);
        setFormError("Fix the highlighted fields to save this expense.");
        focusFirstError(apiErr.fieldErrors);
      } else {
        setFormError(`Couldn't save the expense. ${apiErr?.message ?? ""}`.trim());
      }
    }
  }

  const title = isEdit ? "Edit expense" : "Add expense";
  const descriptionLength = values.description.trim().length;

  return (
    <>
      <BackLink />
      <PageHeader
        title={title}
        subtitle={isEdit ? "Update the details and save to replace the current values." : "Record a facility expense. All fields are required."}
      />
      {loadError && <Notice tone="error">{loadError}</Notice>}
      {isEdit && !initial && !loadError && <p className="loading">Loading expense…</p>}
      {formError && <Notice tone="error">{formError}</Notice>}

      {initial && (
        <form className="surface form-surface" onSubmit={handleSubmit} noValidate>
          <div className="form-grid">
            <Field id="amount" label="Amount (INR)" hint="Amount in INR, up to 2 decimal places." error={errors.amount}>
              {(props) => (
                <input
                  {...props}
                  className="input"
                  inputMode="decimal"
                  autoComplete="off"
                  placeholder="e.g. 1250.50"
                  value={values.amount}
                  onChange={(e) => update("amount", e.target.value)}
                  onBlur={() => blur("amount")}
                />
              )}
            </Field>
            <Field id="date" label="Date" hint="Today or an earlier date." error={errors.date}>
              {(props) => (
                <input
                  {...props}
                  className="input"
                  type="date"
                  max={todayIso()}
                  value={values.date}
                  onChange={(e) => update("date", e.target.value)}
                  onBlur={() => blur("date")}
                />
              )}
            </Field>
            <Field id="category" label="Category" error={errors.category}>
              {(props) => (
                <select
                  {...props}
                  className="input"
                  value={values.category}
                  onChange={(e) => update("category", e.target.value)}
                  onBlur={() => blur("category")}
                >
                  <option value="">Select a category</option>
                  {CATEGORIES.map((c) => (
                    <option key={c} value={c}>
                      {c}
                    </option>
                  ))}
                </select>
              )}
            </Field>
            <RadioField
              name="paymentState"
              label="Payment state"
              options={PAYMENT_STATES}
              value={values.paymentState}
              error={errors.paymentState}
              onChange={(v) => update("paymentState", v)}
            />
            <Field
              id="description"
              label="Description"
              full
              hint={`${descriptionLength} of ${DESCRIPTION_MAX} characters.`}
              error={errors.description}
            >
              {(props) => (
                <textarea
                  {...props}
                  className="input"
                  rows={4}
                  value={values.description}
                  onChange={(e) => update("description", e.target.value)}
                  onBlur={() => blur("description")}
                />
              )}
            </Field>
          </div>
          <div className="form-actions">
            <Button onClick={() => navigate(isEdit ? `/expenses/${id}` : "/")} disabled={saving}>
              Cancel
            </Button>
            <Button variant="primary" type="submit" disabled={saving}>
              {saving ? "Saving…" : "Save expense"}
            </Button>
          </div>
        </form>
      )}
    </>
  );
}
