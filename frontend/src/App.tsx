import { useCallback, useEffect, useState } from "react";
import { api, ApiError, setUnauthorizedHandler, type Session } from "./api";
import { canLeave, setNavigationGuard, useLocation } from "./router";
import { AppShell, AuthShell } from "./components/AppShell";
import { BackLink, Notice, PageHeader } from "./components/ui";
import { AuthPage } from "./pages/AuthPage";
import { ExpensesPage } from "./pages/ExpensesPage";
import { ExpenseDetailPage } from "./pages/ExpenseDetailPage";
import { ExpenseFormPage } from "./pages/ExpenseFormPage";

export function App() {
  const [session, setSession] = useState<Session>();
  const [sessionError, setSessionError] = useState<string>();
  const [authMessage, setAuthMessage] = useState<string>();
  const location = useLocation();

  const loadSession = useCallback(() => {
    setSessionError(undefined);
    api
      .session()
      .then(setSession)
      .catch((err) => setSessionError(err instanceof ApiError ? err.message : "Couldn't reach the app server."));
  }, []);

  useEffect(() => {
    loadSession();
    setUnauthorizedHandler(() => {
      setNavigationGuard(null);
      setAuthMessage("Your session ended. Sign in again to continue.");
      setSession({ setupRequired: false, signedIn: false, userName: null });
    });
  }, [loadSession]);

  if (sessionError) {
    return (
      <AuthShell>
        <Notice tone="error">{sessionError}</Notice>
      </AuthShell>
    );
  }

  if (!session) {
    return (
      <AuthShell>
        <p className="loading">Loading…</p>
      </AuthShell>
    );
  }

  if (!session.signedIn) {
    return (
      <AuthShell>
        <AuthPage
          key={session.setupRequired ? "setup" : "login"}
          mode={session.setupRequired ? "setup" : "login"}
          message={authMessage}
          onSignedIn={() => {
            setAuthMessage(undefined);
            loadSession();
          }}
        />
      </AuthShell>
    );
  }

  async function signOut() {
    if (!canLeave()) return;
    setNavigationGuard(null);
    try {
      await api.logout();
    } catch {
      // The session cookie is cleared server-side when reachable; show the sign-in page regardless.
    }
    window.history.replaceState({}, "", "/");
    setAuthMessage("You've signed out.");
    setSession({ setupRequired: false, signedIn: false, userName: null });
  }

  return (
    <AppShell userName={session.userName ?? ""} onSignOut={signOut}>
      <Routes path={location.path} flash={location.flash} routeKey={location.key} />
    </AppShell>
  );
}

function Routes({ path, flash, routeKey }: { path: string; flash?: string; routeKey: number }) {
  if (path === "/") return <ExpensesPage key={routeKey} flash={flash} />;
  if (path === "/expenses/new") return <ExpenseFormPage key={routeKey} />;

  let match = /^\/expenses\/(\d+)$/.exec(path);
  if (match) return <ExpenseDetailPage key={routeKey} id={Number(match[1])} />;

  match = /^\/expenses\/(\d+)\/edit$/.exec(path);
  if (match) return <ExpenseFormPage key={routeKey} id={Number(match[1])} />;

  return (
    <>
      <BackLink />
      <PageHeader title="Page not found" subtitle="This page doesn't exist." />
    </>
  );
}
