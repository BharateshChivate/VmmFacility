# Feature 001: Expense Tracker

**Status:** Draft for implementation  
**Version:** 0.1  
**Date:** 2026-10-18  
**Product:** VMM Facility Management

## 1. Purpose

Let the Facility Manager record and review expenses incurred for managing the VMM facility. This is a local-first feature in the desktop app. The Mac POC should use the existing project stack—React + TypeScript, ASP.NET Core Web API, EF Core, and SQLite—where present, and must not depend on Windows-only WebView2. It must work without internet access, Ollama, or cloud backup.

## 2. Goals

- Record expenses with amount, date, category, payee, description, and payment state.
- Review expenses over a selected period, with useful filters and totals.
- Correct mistakes without losing the history of changes.
- Optionally attach a receipt or invoice to an expense.
- Keep records and receipt files available locally and include them in the app's existing backup/restore process.

## 3. Non-goals

- Accounting ledger, tax/GST filing, invoicing, payroll, or bank reconciliation.
- Budget planning, purchase approvals, recurring expenses, or vendor management.
- Automatic receipt OCR, AI categorization, or cloud storage integration.
- Multiple facilities, currencies per expense, or role/approval workflows beyond existing app authorization.

## 4. Roles and assumptions

- **Facility Manager (FM):** authenticated user who creates, views, edits, and exports expense records.
- The feature represents facility operating expenses, not personal expenses or income.
- One facility and one configured currency are in scope. Use the app's existing facility currency setting; if none exists, default to INR for the POC and display the currency explicitly.
- The expense date is the local calendar date on which the expense was incurred. Persist the date without a time component.
- A record is **Paid** or **Unpaid**. “Unpaid” means the expense is recorded but payment has not yet been made; no partial-payment accounting is included.
- Use existing user identity, authorization, audit, and UI conventions. Do not create duplicate user or facility models.

## 5. User stories

1. As the FM, I can add an expense so that facility spending is recorded as it happens.
2. As the FM, I can mark an expense Paid or Unpaid and record its payment method when paid.
3. As the FM, I can attach a receipt or invoice so that supporting evidence stays with the record.
4. As the FM, I can browse, search, filter, and total expenses for a selected period.
5. As the FM, I can edit a mistaken entry while retaining who changed it, when, and why.
6. As the FM, I can export the filtered expense list to CSV for review outside the app.

## 6. User experience and behavior

### Expense list

- Add **Expenses** to the existing desktop app navigation.
- Default the list to the current calendar month, with date range controls and a clear **All dates** option.
- Show a table/list with date, description, category, payee, amount (including currency), payment state, payment method when paid, and receipt indicator.
- Show totals for the filtered results: total expenses, paid, and unpaid. Apply the same filters to rows and totals.
- Provide search across description, payee, and reference number; category and payment-state filters; date range filter; and a clear-filters action.
- Sort newest date first by default. Keep the selected range and filters while opening and closing an expense detail/edit view.
- Provide **Add expense**, **View**, and **Edit** actions. Do not hard-delete records from the UI.
- Show loading, empty, no-results, validation, saved, and service-error states.

### Create/edit expense

Required fields:

- Date incurred.
- Category.
- Description (1–500 characters).
- Amount: positive decimal with no more than two fractional digits, stored as a decimal-safe value (not binary floating point).
- Currency: inherited from the facility and read-only in this feature.
- Payment state: Paid or Unpaid.

Optional fields:

- Payee/vendor (maximum 200 characters).
- Payment method (`Cash`, `Bank transfer`, `Card`, `Cheque`, `Other`) when Paid. “Other” may include an optional short detail.
- Reference/receipt number (maximum 100 characters).
- Notes (maximum 1000 characters).
- One receipt/invoice file, PDF/JPEG/PNG, maximum 10 MB. Keep the original file; do not perform OCR or alter the image.

Additional behavior:

- If state is Unpaid, payment method must be empty. If state is Paid, payment method is required.
- Future expense dates are allowed only if the existing product convention supports planned expenses; otherwise reject them. For the POC, reject future dates and record planned expenses as out of scope.
- Editing a saved record requires a reason (maximum 500 characters). Require confirmation before saving the correction.
- A correction updates the record and writes audit history in the same database transaction. Same-value saves do not create history entries.
- Before leaving a form with unsaved changes, prompt to save or discard.
- Receipt attachment is optional. Replacing/removing one during edit requires confirmation and must not delete a file still referenced by another record.

### Expense detail and history

- Show all entered fields, receipt preview/open action, creation/update actor and timestamps, and correction history.
- Each correction history item shows actor, time, reason, and before/after values for changed fields.
- Do not expose local filesystem paths as editable user input or render an uploaded file as executable content.

### CSV export

- Export exactly the currently filtered records and totals, with a header row and stable column names.
- Include date, description, category, payee, amount, currency, payment state, payment method, reference, notes, and receipt filename (if present).
- Use UTF-8 and safe CSV escaping. Prevent spreadsheet formula injection for values beginning with `=`, `+`, `-`, or `@`.
- Export is initiated by the user and saved to a user-selected location. Do not silently write into the application install folder.

## 7. Categories

Use the application's existing expense categories if they exist. Otherwise seed these built-in categories for the POC: **Utilities**, **Maintenance & Repairs**, **Housekeeping**, **Security**, **Supplies**, and **Other**.

- A category must be selected for every expense.
- Do not allow deletion of a category that is used by an expense; allow it to be deactivated if category management already exists.
- If category administration does not exist, keep the initial category list fixed in this feature and leave custom category management for a later feature.

## 8. Data model

Follow existing EF Core conventions and reuse existing users/facility/currency entities.

### ExpenseRecord

- `Id`: primary key using the project's standard key type.
- `ExpenseDate`: required local calendar date.
- `CategoryId` (or existing category key): required category.
- `Description`: required, maximum 500 characters.
- `Payee`: nullable, maximum 200 characters.
- `Amount`: required decimal with precision sufficient for the facility currency and scale 2; never use float/double for money.
- `CurrencyCode`: required, inherited from facility configuration and immutable per record in this feature.
- `PaymentStatus`: required (`Paid`, `Unpaid`).
- `PaymentMethod`: nullable enum/value; required when Paid and empty when Unpaid.
- `PaymentMethodDetail`: nullable, maximum 100 characters, only for `Other`.
- `ReferenceNumber`: nullable, maximum 100 characters.
- `Notes`: nullable, maximum 1000 characters.
- `ReceiptStorageKey`, `ReceiptOriginalName`, `ReceiptContentType`, `ReceiptSizeBytes`: nullable metadata for one local attachment. Store the file in the app's managed data area, not as an arbitrary path supplied by the client.
- `CreatedAtUtc`, `CreatedByUserId`, `UpdatedAtUtc`, `UpdatedByUserId`: required audit fields where identity is available.

### ExpenseChange

- `Id`, `ExpenseRecordId`, `ChangedAtUtc`, `ChangedByUserId`, `Reason` (required on correction, max 500), and structured `BeforeJson` / `AfterJson` snapshots of expense fields.
- Do not include receipt bytes, secrets, or unrelated personal data in audit snapshots.

Do not hard-delete expenses. If removal/cancellation is needed later, define an auditable void status in a separate product decision.

## 9. API requirements

Follow the API's existing routes, authorization, validation, and error response conventions. Suggested routes (adapt names to the repository):

- `GET /api/expenses?from=YYYY-MM-DD&to=YYYY-MM-DD&categoryId=&paymentStatus=&search=&sort=` — return filtered/paged expenses and summary totals calculated over the complete filtered set.
- `POST /api/expenses` — create an expense, optionally with a receipt upload using the project's upload convention.
- `GET /api/expenses/{id}` — return detail and receipt metadata.
- `PUT /api/expenses/{id}` — update an expense; require correction reason for changed persisted values; write audit in the same transaction.
- `GET /api/expenses/{id}/history` — return changes chronologically.
- `GET /api/expenses/{id}/receipt` — securely stream the attachment after authorization.
- `DELETE /api/expenses/{id}/receipt` — remove an optional attachment after explicit confirmation; retain expense record and audit the removal.

Validation and safety:

- Validate date ranges, category existence/active state, amount > 0 and supported decimal scale, currency, payment status/method consistency, field lengths, and upload MIME type/size.
- Do not trust the file extension or client-provided content type alone; validate allowed file signatures where practical and generate a storage key server-side.
- Avoid path traversal; never accept a raw server filesystem path from the client.
- Ensure database updates and audit records are atomic. Enforce authorization on every read, write, export, and receipt request.
- Keep API behavior local-first; no internet, AI, or cloud call is required to create, edit, list, or export expenses.

## 10. Local storage, backup, and offline behavior

- Store expense records in the existing SQLite database via EF Core and add a migration. Never reset or recreate the user's database to apply this feature.
- Store attachments in an app-managed directory under the existing per-user data root, using generated names and metadata in SQLite. Keep them readable only by the current OS user.
- Ensure backup/restore includes both expense rows and referenced receipt files and handles interrupted backup/restore safely.
- If attachment storage is unavailable or an upload fails, do not save a record that appears to have an attached receipt; show a recoverable error.
- Expense entry, viewing, filtering, editing, and CSV export work while offline and with Ollama stopped.

## 11. Accessibility and usability

- Support keyboard navigation, labeled controls, and visible focus.
- Represent Paid/Unpaid with text and icon/status cue, not color alone.
- Format amount with the configured currency and locale while preserving exact decimal values.
- Make receipt status and file type available to assistive technology.
- Confirm destructive attachment removal and unsaved form navigation.

## 12. Acceptance criteria

1. Expenses is reachable from the existing desktop navigation and defaults to the current month.
2. The FM can create, read, and edit an expense with validated amount, date, category, description, and payment state.
3. Paid expenses require a payment method; Unpaid expenses do not accept one.
4. The expense list supports date range, search, category, and payment-state filters, and its totals match the complete filtered result set.
5. An optional allowed receipt can be attached and opened only by an authorized user; invalid/oversized files are rejected safely.
6. Corrections require a reason and preserve before/after values, actor, and timestamp; unchanged saves do not create audit entries.
7. The FM can export the current filtered view as correctly escaped UTF-8 CSV without formula injection.
8. Data and receipts persist locally across restart and are included in supported backup/restore.
9. Core expense workflows operate offline and do not call Ollama or cloud backup services.
10. Existing database data survives the EF Core migration; failed migrations preserve the original database.
11. Keyboard, labels, focus, and non-color status cues meet the accessibility requirements above.

## 13. Implementation instructions for Claude Code

1. Inspect the repository and its local `AGENTS.md` instructions before changing files. Identify the existing desktop shell and platform target, staff/facility/user/currency models, authorization, API patterns, EF Core/SQLite setup, file storage, backup/restore, and UI conventions.
2. Implement inside the existing app. Do not replace the desktop shell or introduce a duplicate database, auth system, or facility model.
3. For a Mac POC, use a macOS-compatible desktop host already present in the repository. Do not use WebView2; it is Windows-specific. If no host exists, report that constraint and propose the smallest supported Mac shell before adding a new framework.
4. Add the EF Core migration, API behavior, UI, receipt storage, CSV export, and audit history described here.
5. Add tests for money precision/validation, payment-method rules, filtering/totals, audit transaction, upload validation/path safety, CSV escaping/formula-injection handling, and backup/restore of receipt files.
6. Run the relevant tests and build/lint checks available in the repo. Summarize changed files, commands and results, and any product decisions that required a default.
7. Do not add payroll, tax filing, budgets, approval workflows, recurring expenses, OCR, or cloud-only behavior.

## 14. Decisions to confirm

- Confirm the facility currency and locale. Default for the Mac POC: INR if the existing app does not configure currency.
- Confirm whether receipts should allow one file per expense or multiple files. This spec starts with one.
- Confirm the final built-in category list and whether FM can add categories. This spec uses a fixed starter list unless category management already exists.
- Confirm whether the FM needs a void/cancel workflow. This spec prohibits hard deletion and leaves voiding for a later decision.
