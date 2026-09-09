"use client";

import Link from "next/link";
import { Box, Heart, LogIn, LogOut, Search, ShoppingBag, UserRound } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { AuthDialog } from "@/components/auth-dialog";
import type { CurrentUser } from "@/lib/storefront/types";

type SessionState = { status: "loading" | "anonymous" } | { status: "authenticated"; user: CurrentUser };

export function StorefrontHeader() {
  const [session, setSession] = useState<SessionState>({ status: "loading" });
  const [isAuthOpen, setIsAuthOpen] = useState(false);

  const loadSession = useCallback(async () => {
    try {
      const response = await fetch("/api/session", { headers: { Accept: "application/json" } });
      const payload: unknown = await response.json().catch(() => null);
      const user = sessionUser(payload);
      setSession(user ? { status: "authenticated", user } : { status: "anonymous" });
    } catch {
      setSession({ status: "anonymous" });
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadSession(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadSession]);

  async function signOut() {
    await fetch("/api/session", { method: "DELETE", headers: { Accept: "application/json" } }).catch(() => null);
    setSession({ status: "anonymous" });
  }

  return (
    <header className="sticky top-0 z-30 border-b border-[var(--line)] bg-white/95 backdrop-blur">
      <div className="mx-auto flex min-h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
        <Link className="flex items-center gap-2 text-sm font-semibold tracking-tight" href="/">
          <span className="grid size-9 place-items-center rounded-sm bg-[var(--foreground)] text-white"><Box aria-hidden="true" size={18} /></span>
          MicroShop
        </Link>
        <nav aria-label="Store navigation" className="flex items-center gap-1">
          <Link className="store-icon-button" href="/products" aria-label="Search products"><Search aria-hidden="true" size={18} /></Link>
          <Link className="store-nav-link hidden sm:inline-flex" href="/products">Shop</Link>
          {session.status === "authenticated" ? <>
            <Link className="store-icon-button" href="/account#saved-items" aria-label="Saved items"><Heart aria-hidden="true" size={18} /></Link>
            <Link className="store-icon-button" href="/account" aria-label="Your account"><UserRound aria-hidden="true" size={18} /></Link>
            <button className="store-icon-button hidden sm:grid" onClick={() => void signOut()} type="button" aria-label="Sign out"><LogOut aria-hidden="true" size={18} /></button>
          </> : session.status === "anonymous" ? <button className="store-icon-button" onClick={() => setIsAuthOpen(true)} type="button" aria-label="Sign in"><LogIn aria-hidden="true" size={18} /></button> : null}
          <Link className="store-icon-button" href="/checkout" aria-label="Cart and checkout"><ShoppingBag aria-hidden="true" size={18} /></Link>
        </nav>
      </div>
      <AuthDialog notice="Sign in to save products, manage orders, and checkout." onClose={() => setIsAuthOpen(false)} onSignedIn={(user) => { setSession({ status: "authenticated", user }); setIsAuthOpen(false); }} open={isAuthOpen} />
    </header>
  );
}

function sessionUser(value: unknown): CurrentUser | null {
  if (typeof value !== "object" || value === null) return null;
  const user = (value as { user?: unknown }).user;
  if (typeof user !== "object" || user === null) return null;
  const record = user as Record<string, unknown>;
  return typeof record.userId === "string"
    && typeof record.userName === "string"
    && typeof record.role === "string"
    && typeof record.isEmailVerified === "boolean"
    && typeof record.receiveOrderUpdates === "boolean"
    ? record as CurrentUser
    : null;
}

export function StorefrontFooter() {
  return (
    <footer className="mt-auto border-t border-[var(--line)] bg-white">
      <div className="mx-auto flex max-w-7xl flex-col gap-3 px-4 py-7 text-sm text-[var(--muted)] sm:flex-row sm:items-center sm:justify-between sm:px-6 lg:px-8">
        <p>MicroShop catalog and order experience.</p>
        <nav aria-label="Footer navigation" className="flex flex-wrap gap-x-5 gap-y-2 font-medium">
          <Link className="hover:text-[var(--foreground)] hover:underline" href="/products">Catalog</Link>
          <Link className="hover:text-[var(--foreground)] hover:underline" href="/account">Account</Link>
          <Link className="hover:text-[var(--foreground)] hover:underline" href="/checkout">Checkout</Link>
        </nav>
      </div>
    </footer>
  );
}