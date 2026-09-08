import Link from "next/link";
import { Box, ShoppingBag } from "lucide-react";

export function StorefrontHeader() {
  return (
    <header className="border-b border-[var(--line)] bg-white/95 backdrop-blur">
      <div className="mx-auto flex min-h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
        <Link className="flex items-center gap-2 text-sm font-semibold tracking-tight" href="/">
          <span className="grid size-9 place-items-center rounded-sm bg-[var(--foreground)] text-white"><Box aria-hidden="true" size={18} /></span>
          MicroShop
        </Link>
        <nav aria-label="Store navigation" className="flex items-center gap-1">
          <Link className="store-nav-link" href="/products">Shop all</Link>
          <Link className="store-nav-link" href="/account">Account</Link>
          <Link className="store-icon-button" href="/checkout"><ShoppingBag aria-hidden="true" size={18} /><span className="sr-only">Checkout</span></Link>
        </nav>
      </div>
    </header>
  );
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
