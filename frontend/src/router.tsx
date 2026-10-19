import { useEffect, useState, type AnchorHTMLAttributes, type MouseEvent } from "react";

// Minimal history-based router; the MVP has only a handful of pages.

type Guard = () => boolean;
let guard: Guard | null = null;

/** Register a check that runs before in-app navigation (e.g. unsaved form changes). */
export function setNavigationGuard(next: Guard | null) {
  guard = next;
}

/** True when there is nothing to lose by leaving the current page (or the user confirmed). */
export function canLeave(): boolean {
  return !guard || guard();
}

export function navigate(to: string, options: { flash?: string; skipGuard?: boolean } = {}) {
  if (!options.skipGuard && !canLeave()) return;
  window.history.pushState({ flash: options.flash }, "", to);
  window.dispatchEvent(new PopStateEvent("popstate"));
  window.scrollTo(0, 0);
}

export function useLocation() {
  const read = () => ({
    path: window.location.pathname,
    flash: (window.history.state?.flash as string | undefined) ?? undefined,
    key: Math.random(),
  });
  const [location, setLocation] = useState(read);
  useEffect(() => {
    const onChange = () => setLocation(read());
    window.addEventListener("popstate", onChange);
    return () => window.removeEventListener("popstate", onChange);
  }, []);
  return location;
}

/** Clear a one-time flash message so it does not reappear on back/forward. */
export function consumeFlash() {
  if (window.history.state?.flash) window.history.replaceState({}, "");
}

export function Link({ href, onClick, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement> & { href: string }) {
  const handleClick = (e: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(e);
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    navigate(href);
  };
  return <a href={href} onClick={handleClick} {...rest} />;
}
