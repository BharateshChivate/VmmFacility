# UI Guidelines: VMM Facility Management

**Purpose:** Shared interface rules for every page in the local VMM Facility Management web app.  
**Status:** MVP baseline, version 0.1  
**Date:** 2026-10-18

These rules keep screens familiar and easy to use as the app grows. Prefer reusing shared components and tokens over styling each page independently. Keep the current MVP small; do not add features just to fill space.

## Product principles

- Build for a Facility Manager doing practical day-to-day work on a Mac.
- Keep the interface calm, clear, and efficient. Prioritize the task over decoration.
- Use plain language, explicit labels, and predictable actions.
- Keep page layout, navigation, typography, controls, and feedback consistent.
- Support local use. Do not imply that data has synced, uploaded, or been saved remotely.

## Visual foundation

Use these as the initial shared tokens. Define them once as CSS variables or the existing design-token mechanism; do not repeat literal values across components.

| Token | Starter value | Use |
|---|---|---|
| `--color-page` | `#F7F8F6` | App page background |
| `--color-surface` | `#FFFFFF` | Forms, menus, and table surface |
| `--color-text` | `#202923` | Primary text |
| `--color-muted` | `#66716A` | Helper text and secondary labels |
| `--color-border` | `#DDE3DE` | Input borders and table separators |
| `--color-primary` | `#28634F` | Primary buttons, links, and selected navigation |
| `--color-primary-hover` | `#1F503F` | Hover/pressed primary state |
| `--color-danger` | `#A33A32` | Destructive or error state |
| `--color-success` | `#28634F` | Paid/success state |
| `--color-pending` | `#8A5A16` | Unpaid/pending state |

- Use a light neutral page background, white work surfaces, dark readable text, and one restrained green primary accent.
- Use color only to reinforce meaning. Always pair status colors with readable text.
- Avoid gradients, decorative illustrations, excessive shadows, nested cards, and large areas of saturated color.
- Use one sans-serif UI font across the app. Use the existing project font if one is already configured; otherwise use the system UI font stack. Do not add a font download dependency for the local MVP.

## Typography and spacing

- Base body text: 15–16px. Form labels and table text must remain comfortably readable; helper text should not fall below 13px.
- Use a small consistent type scale: page title 28–32px, section title 18–20px, body 15–16px, helper/meta 13–14px.
- Use a 4px spacing scale (4, 8, 12, 16, 24, 32, 40). Prefer 16–24px between related controls and 32px between page sections.
- Use consistent control heights (about 40px for inputs and buttons), border radius (6–8px), and focus outlines.
- Keep content aligned to a shared page grid and avoid long full-width text measures.

## Shared page shell

- Use one shared app shell on every signed-in page: a consistent VMM identity/header area, navigation location, page content area, and spacing.
- If navigation is present, keep its position and styling consistent. In the MVP, show only destinations that exist; do not add placeholder links for future modules.
- Keep the main content at a readable maximum width (approximately 1120px) and centered within the available window. On narrower windows, use the full width with consistent side padding.
- Every page starts with a consistent page header: title on the left and page-level primary/secondary actions on the right. Add a subtitle only when it helps explain the task.
- Use the same back-navigation placement and wording on detail or form pages.
- Do not make every section a card. Use whitespace, alignment, and subtle separators first; add a bordered surface only when it improves grouping.

## Buttons and controls

- Use one clear primary action per page or form. Primary actions use the shared green token; secondary actions use a quiet outline or text treatment.
- Use consistent button height, padding, corner radius, label casing, and icon placement. Prefer text labels over icon-only buttons.
- Show hover, keyboard-focus, disabled, and submitting states. Prevent duplicate submissions while a save is in progress.
- Keep button labels specific: “Add expense”, “Save expense”, “Cancel”, and “Export PDF”. Avoid vague labels such as “OK” or “Submit”.
- Use native semantic controls where suitable. Dropdowns and date inputs must have visible labels and keyboard support.

## Forms

- Put a visible label above every input. Placeholder text may show an example, but must not replace the label.
- Keep related fields together and use the same field spacing, input height, border, and error placement on every form.
- For the Expense MVP, use this order: Amount (INR) and Date in a two-column row; Category and Payment state in a two-column row; Description full width; form actions at the bottom.
- Mark required fields consistently. Explain constraints near the field when useful (for example, “Amount in INR, up to 2 decimal places”).
- Validate after interaction and again on save. Place the error beside its field, preserve valid entered data, and provide a short actionable message.
- Place Save as the primary action and Cancel as secondary. Keep them together in a consistent footer area; on narrow layouts, stack them with Save first.
- Warn before discarding a form only when the user has changed a value.

## Lists and tables

- Present expenses as one clear ledger/table, not a grid of separate cards.
- Keep column order consistent: Date, Description, Category, Amount (INR), Payment state, then row actions if needed.
- Right-align amounts and format them consistently in INR. Keep dates in the user's local format.
- Use a clear table header, comfortable row height, and subtle horizontal separators. Avoid heavy gridlines and zebra stripes unless they materially improve scanning.
- Keep row actions visually secondary and consistently positioned. Make the whole row behavior clear; do not rely on hover-only actions.
- Empty states should say what is empty and show the one relevant next action. Do not show fabricated sample data as real user data.

## Status, feedback, and dialogs

- Display payment state as text: **Paid** or **Unpaid**. Use a small subtle status treatment, but never rely on color alone.
- Use consistent inline feedback for save success, validation errors, and service failures. Keep the message near the action or content it describes.
- Do not use a modal for routine create/edit flows. Use the dedicated form page pattern for the Expense MVP.
- Reserve confirmation dialogs for actions that discard unsaved changes or remove data. Do not add delete actions to the Expense MVP.

## PDF export

- Place **Export PDF** as a secondary action in the page header, beside the main page action.
- Explain failures in the app. On success, rely on the browser's download behavior and show a short confirmation that the PDF download started.
- Do not imitate a native Mac save dialog or claim a file was saved to a specific folder unless the browser reports that information.

## Accessibility and responsive behavior

- All page actions and inputs must work with keyboard only. Maintain a visible focus indicator and logical tab order.
- Use semantic headings, buttons, labels, form controls, and table markup.
- Provide accessible names for controls and announce form errors and save feedback to assistive technology.
- Ensure text and control boundaries are visibly distinguishable from their backgrounds. Status must be understandable without color.
- At narrower widths, keep the same content and task order. Stack paired fields, allow the table to scroll horizontally when necessary, and keep primary actions easy to reach. Do not hide required fields or actions.
- Respect system zoom and text scaling; do not fix the app to a single screen size.

## Expense MVP page patterns

### Expenses page

- Page title **Expenses**; primary action **Add expense**; secondary action **Export PDF**.
- Show a simple ledger with newest expenses first and an empty state for a new database.
- Do not add charts, KPI cards, filters, receipt indicators, budget tools, or extra modules to this MVP.

### Add/Edit expense page

- Show a consistent back link to **Expenses**, the page title (**Add expense** or **Edit expense**), and one short helper sentence.
- Use the shared form order above, with fields limited to Amount (INR), Date, Category, Payment state, and Description.
- Show **Cancel** and **Save expense** at the bottom. After save, return to the expense list and show a confirmation.

### View expense page

- Show the same back link to **Expenses**, the page title **Expense**, and **Edit expense** as the primary header action.
- List the same fields in the same order as the form, read-only, with created and last-updated times as muted meta text.
- Open it from the expense description in the ledger; the ledger's **Edit** row action goes straight to the edit form.

### Account setup and sign-in pages

- Use a reduced shell: the VMM identity header only, without navigation, and a narrow centered content column.
- First launch shows **Create your account** (Username, Password, Confirm password; primary action **Create account**). Later launches show **Sign in** (Username, Password; primary action **Sign in**).
- Use the shared form fields, error placement, and notices. Show a neutral notice after signing out or when a session ends.
- Show **Signed in as {username}** and a quiet **Sign out** text action at the right of the signed-in header.

## Implementation rule for Claude Code

Before implementing or changing any UI page, read this file and `docs/specs/mvp-expense-tracker.md`. Reuse shared layout, tokens, buttons, form controls, table styles, and feedback components. If a new requirement conflicts with these rules, follow the explicit product requirement and update this file so later pages remain consistent. Do not add new visual patterns or features without a product requirement.
