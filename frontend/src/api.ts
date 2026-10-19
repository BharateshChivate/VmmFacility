export type PaymentState = "Paid" | "Unpaid";

export interface Expense {
  id: number;
  amount: string; // decimal string, e.g. "1250.50"
  date: string; // yyyy-MM-dd
  category: string;
  description: string;
  paymentState: PaymentState;
  createdAt: string;
  updatedAt: string;
}

export interface ExpenseInput {
  amount: string;
  date: string;
  category: string;
  description: string;
  paymentState: string;
}

export interface Session {
  setupRequired: boolean;
  signedIn: boolean;
  userName: string | null;
}

export type FieldErrors = Record<string, string>;

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public fieldErrors: FieldErrors = {},
  ) {
    super(message);
  }
}

let onUnauthorized: () => void = () => {};

/** Called when an authenticated request finds the session has ended. */
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

async function send(method: string, url: string, body?: unknown): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(url, {
      method,
      credentials: "same-origin",
      headers: {
        "X-VMM-Request": "1",
        ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError("The app server isn't responding. Check that the app is still running, then try again.", 0);
  }

  if (response.ok) return response;

  let message = "Something went wrong. Please try again.";
  const fieldErrors: FieldErrors = {};
  try {
    const problem = await response.json();
    if (problem?.errors) {
      for (const [key, messages] of Object.entries(problem.errors as Record<string, string[]>)) {
        fieldErrors[key] = messages[0];
      }
      message = "Some fields need attention.";
    } else if (problem?.detail) {
      message = problem.detail;
    }
  } catch {
    // Non-JSON error body; keep the generic message.
  }

  if (response.status === 401 && !url.startsWith("/api/auth/")) onUnauthorized();
  throw new ApiError(message, response.status, fieldErrors);
}

async function json<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await send(method, url, body);
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const api = {
  session: () => json<Session>("GET", "/api/auth/session"),
  setup: (userName: string, password: string) => json<void>("POST", "/api/auth/setup", { userName, password }),
  login: (userName: string, password: string) => json<void>("POST", "/api/auth/login", { userName, password }),
  logout: () => json<void>("POST", "/api/auth/logout"),

  listExpenses: () => json<Expense[]>("GET", "/api/expenses"),
  getExpense: (id: number) => json<Expense>("GET", `/api/expenses/${id}`),
  createExpense: (input: ExpenseInput) => json<Expense>("POST", "/api/expenses", input),
  updateExpense: (id: number, input: ExpenseInput) => json<Expense>("PUT", `/api/expenses/${id}`, input),

  /** Fetches the PDF and hands it to the browser's normal download behaviour. */
  async downloadReport() {
    const response = await send("GET", "/api/expenses/report.pdf");
    const blob = await response.blob();
    const disposition = response.headers.get("Content-Disposition") ?? "";
    const match = /filename="?([^";]+)"?/.exec(disposition);
    const filename = match?.[1] ?? "VMM-Expense-Report.pdf";

    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  },
};
