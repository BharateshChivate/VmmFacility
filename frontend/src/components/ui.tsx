import type { ButtonHTMLAttributes, ReactNode } from "react";
import { Link } from "../router";
import type { PaymentState } from "../api";

// Shared building blocks used by every page. Keep visual patterns here, not in pages.

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <div className="page-header">
      <div>
        <h1>{title}</h1>
        {subtitle && <p className="page-header__subtitle">{subtitle}</p>}
      </div>
      {actions && <div className="page-header__actions">{actions}</div>}
    </div>
  );
}

export function BackLink({ onClick }: { onClick?: () => void }) {
  return (
    <Link href="/" className="back-link" onClick={onClick}>
      ← Back to Expenses
    </Link>
  );
}

type ButtonVariant = "primary" | "secondary" | "text";

export function Button({
  variant = "secondary",
  className = "",
  type = "button",
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant }) {
  return <button type={type} className={`button button--${variant} ${className}`} {...rest} />;
}

export function ButtonLink({ href, variant = "secondary", children }: { href: string; variant?: ButtonVariant; children: ReactNode }) {
  return (
    <Link href={href} className={`button button--${variant}`}>
      {children}
    </Link>
  );
}

export type NoticeTone = "success" | "error" | "info";

export function Notice({ tone, children }: { tone: NoticeTone; children: ReactNode }) {
  return (
    <div className={`notice notice--${tone}`} role={tone === "error" ? "alert" : "status"}>
      {children}
    </div>
  );
}

export function StatusBadge({ state }: { state: PaymentState }) {
  return <span className={`status status--${state === "Paid" ? "paid" : "unpaid"}`}>{state}</span>;
}

export interface ControlProps {
  id: string;
  "aria-invalid": boolean;
  "aria-describedby"?: string;
  "aria-required": true;
}

/** Label above the control, optional hint, and field-level error below. */
export function Field({
  id,
  label,
  hint,
  error,
  full,
  children,
}: {
  id: string;
  label: string;
  hint?: ReactNode;
  error?: string;
  full?: boolean;
  children: (props: ControlProps) => ReactNode;
}) {
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(" ") || undefined;
  return (
    <div className={`field${full ? " field--full" : ""}`}>
      <label className="field__label" htmlFor={id}>
        {label} <span className="field__required">(required)</span>
      </label>
      {children({ id, "aria-invalid": Boolean(error), "aria-describedby": describedBy, "aria-required": true })}
      {hint && (
        <span id={hintId} className="field__hint">
          {hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="field__error">
          {error}
        </span>
      )}
    </div>
  );
}

/** Native radio group with a visible legend, styled like other fields. */
export function RadioField({
  name,
  label,
  options,
  value,
  error,
  onChange,
}: {
  name: string;
  label: string;
  options: readonly string[];
  value: string;
  error?: string;
  onChange: (value: string) => void;
}) {
  const errorId = error ? `${name}-error` : undefined;
  return (
    <fieldset className="field" aria-describedby={errorId} aria-invalid={Boolean(error)} aria-required="true">
      <legend className="field__label">
        {label} <span className="field__required">(required)</span>
      </legend>
      <div className="radio-row">
        {options.map((option, i) => (
          <label key={option}>
            <input
              type="radio"
              id={i === 0 ? name : undefined}
              name={name}
              value={option}
              checked={value === option}
              onChange={() => onChange(option)}
            />
            {option}
          </label>
        ))}
      </div>
      {error && (
        <span id={errorId} className="field__error">
          {error}
        </span>
      )}
    </fieldset>
  );
}
