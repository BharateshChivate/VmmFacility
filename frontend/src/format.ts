const inr = new Intl.NumberFormat("en-IN", {
  style: "currency",
  currency: "INR",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const localDate = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });
const localDateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

/** Formats a decimal string (e.g. "1250.50") as INR for display only. */
export function formatInr(amount: string): string {
  return inr.format(Number(amount));
}

/** Formats a yyyy-MM-dd date in the user's local format without timezone shifts. */
export function formatDate(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  return localDate.format(new Date(y, m - 1, d));
}

export function formatDateTime(isoUtc: string): string {
  return localDateTime.format(new Date(isoUtc));
}
