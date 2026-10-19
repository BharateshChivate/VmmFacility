import type { ExpenseInput, FieldErrors } from "./api";

// Mirrors backend/VmmFacility.Api/ExpenseRules.cs. The backend remains the source of truth.

export const CATEGORIES = [
  "Utilities",
  "Maintenance & Repairs",
  "Housekeeping",
  "Security",
  "Supplies",
  "Other",
] as const;

export const PAYMENT_STATES = ["Paid", "Unpaid"] as const;

export const DESCRIPTION_MAX = 500;
const MAX_AMOUNT = 999_999_999.99;

export function todayIso(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function validateField(field: keyof ExpenseInput, values: ExpenseInput): string | undefined {
  switch (field) {
    case "amount": {
      const v = values.amount.trim();
      if (!v) return "Enter the amount.";
      if (!/^\d+(\.\d{1,2})?$/.test(v))
        return "Enter a number in INR with up to 2 decimal places, for example 1250 or 1250.50.";
      const n = Number(v);
      if (n <= 0) return "Amount must be greater than 0.";
      if (n > MAX_AMOUNT) return "Amount must be 999,999,999.99 or less.";
      return undefined;
    }
    case "date": {
      const v = values.date.trim();
      if (!v) return "Enter the expense date.";
      if (!/^\d{4}-\d{2}-\d{2}$/.test(v)) return "Enter a valid date.";
      if (v > todayIso()) return "Date can't be in the future.";
      return undefined;
    }
    case "category":
      if (!values.category) return "Choose a category.";
      if (!(CATEGORIES as readonly string[]).includes(values.category)) return "Choose a category from the list.";
      return undefined;
    case "description": {
      const v = values.description.trim();
      if (!v) return "Enter a description.";
      if (v.length > DESCRIPTION_MAX) return `Description must be ${DESCRIPTION_MAX} characters or fewer.`;
      return undefined;
    }
    case "paymentState":
      if (!values.paymentState) return "Choose Paid or Unpaid.";
      return undefined;
  }
}

export const FIELD_ORDER: (keyof ExpenseInput)[] = ["amount", "date", "category", "paymentState", "description"];

export function validateAll(values: ExpenseInput): FieldErrors {
  const errors: FieldErrors = {};
  for (const field of FIELD_ORDER) {
    const message = validateField(field, values);
    if (message) errors[field] = message;
  }
  return errors;
}
