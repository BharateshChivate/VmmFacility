import { useState, type FormEvent } from "react";
import { api, ApiError, type FieldErrors } from "../api";
import { Button, Field, Notice, PageHeader } from "../components/ui";

/** First-launch account creation ("setup") or sign-in ("login"). */
export function AuthPage({
  mode,
  message,
  onSignedIn,
}: {
  mode: "setup" | "login";
  message?: string;
  onSignedIn: () => void;
}) {
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  const isSetup = mode === "setup";

  function validate(): FieldErrors {
    const next: FieldErrors = {};
    const name = userName.trim();
    if (!name) next.userName = "Enter a username.";
    else if (isSetup && (name.length < 3 || name.length > 50)) next.userName = "Username must be 3 to 50 characters.";
    if (!password) next.password = "Enter a password.";
    else if (isSetup && (password.length < 8 || password.length > 128))
      next.password = "Password must be 8 to 128 characters.";
    if (isSetup && !next.password && confirm !== password) next.confirm = "Passwords don't match.";
    return next;
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (submitting) return;
    const next = validate();
    setErrors(next);
    setFormError(undefined);
    const first = Object.keys(next)[0];
    if (first) {
      document.getElementById(first)?.focus();
      return;
    }

    setSubmitting(true);
    try {
      if (isSetup) await api.setup(userName.trim(), password);
      else await api.login(userName.trim(), password);
      onSignedIn();
    } catch (err) {
      const apiErr = err instanceof ApiError ? err : undefined;
      setErrors(apiErr?.fieldErrors ?? {});
      setFormError(apiErr?.message ?? "Something went wrong. Please try again.");
      setPassword("");
      setConfirm("");
      setSubmitting(false);
    }
  }

  return (
    <>
      <PageHeader
        title={isSetup ? "Create your account" : "Sign in"}
        subtitle={
          isSetup
            ? "Set up the Facility Manager account for this Mac. You'll use it to sign in each time you open the app."
            : "Sign in to manage facility expenses."
        }
      />
      {message && !formError && <Notice tone="info">{message}</Notice>}
      {formError && <Notice tone="error">{formError}</Notice>}
      <form className="surface form-surface" onSubmit={handleSubmit} noValidate>
        <div className="form-grid form-grid--single">
          <Field id="userName" label="Username" error={errors.userName}>
            {(props) => (
              <input
                {...props}
                className="input"
                autoComplete="username"
                value={userName}
                onChange={(e) => setUserName(e.target.value)}
              />
            )}
          </Field>
          <Field
            id="password"
            label="Password"
            hint={isSetup ? "At least 8 characters." : undefined}
            error={errors.password}
          >
            {(props) => (
              <input
                {...props}
                className="input"
                type="password"
                autoComplete={isSetup ? "new-password" : "current-password"}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            )}
          </Field>
          {isSetup && (
            <Field id="confirm" label="Confirm password" error={errors.confirm}>
              {(props) => (
                <input
                  {...props}
                  className="input"
                  type="password"
                  autoComplete="new-password"
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                />
              )}
            </Field>
          )}
        </div>
        <div className="form-actions">
          <Button variant="primary" type="submit" disabled={submitting}>
            {submitting ? (isSetup ? "Creating account…" : "Signing in…") : isSetup ? "Create account" : "Sign in"}
          </Button>
        </div>
      </form>
    </>
  );
}
