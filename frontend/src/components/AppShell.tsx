import type { ReactNode } from "react";
import { Link } from "../router";
import { Button } from "./ui";

/** Shared shell for every signed-in page. Only list destinations that exist. */
export function AppShell({
  userName,
  onSignOut,
  children,
}: {
  userName: string;
  onSignOut: () => void;
  children: ReactNode;
}) {
  return (
    <>
      <header className="app-header">
        <div className="app-header__inner">
          <span className="app-identity">VMM Facility Management</span>
          <nav className="app-nav" aria-label="Main">
            <Link href="/" aria-current="page">
              Expenses
            </Link>
          </nav>
          <div className="app-account">
            <span>Signed in as {userName}</span>
            <Button variant="text" onClick={onSignOut}>
              Sign out
            </Button>
          </div>
        </div>
      </header>
      <main className="app-main">{children}</main>
    </>
  );
}

/** Shell for the setup and sign-in pages: identity only, narrow content. */
export function AuthShell({ children }: { children: ReactNode }) {
  return (
    <>
      <header className="app-header">
        <div className="app-header__inner">
          <span className="app-identity">VMM Facility Management</span>
        </div>
      </header>
      <main className="app-main app-main--narrow">{children}</main>
    </>
  );
}
