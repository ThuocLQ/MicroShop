"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { ClipboardList, LoaderCircle, MapPin, RefreshCw, ShieldCheck, UserRound } from "lucide-react";
import { AddressSelection, type AddressLoadState } from "@/components/address-selection";
import { StorefrontFooter, StorefrontHeader } from "@/components/storefront-shell";
import { problemMessage } from "@/lib/http/problem-details";
import type { CurrentUser, CustomerAddress, OrderSummary } from "@/lib/storefront/types";

type LoadState = "loading" | "ready" | "unauthenticated" | "unavailable";

export function AccountClient() {
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [addresses, setAddresses] = useState<CustomerAddress[]>([]);
  const [orders, setOrders] = useState<OrderSummary[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [addressMessage, setAddressMessage] = useState<string | null>(null);
  const [busyAddressId, setBusyAddressId] = useState<string | null>(null);
  const [addressLoadState, setAddressLoadState] = useState<AddressLoadState>("loading");
  const [preferenceBusy, setPreferenceBusy] = useState(false);
  const [preferenceMessage, setPreferenceMessage] = useState<string | null>(null);

  const loadAccount = useCallback(async () => {
    setLoadState("loading");
    setMessage(null);
    try {
      const sessionResponse = await fetch("/api/session", { cache: "no-store" });
      const sessionPayload: unknown = await sessionResponse.json().catch(() => null);
      if (sessionResponse.status === 401) {
        setLoadState("unauthenticated");
        return;
      }
      if (!sessionResponse.ok || !isSession(sessionPayload)) throw new Error(problemMessage(sessionPayload) ?? "Your account could not be loaded.");
      setUser(sessionPayload.user);

      const [addressesResponse, ordersResponse] = await Promise.all([
        fetch("/api/addresses", { cache: "no-store" }),
        fetch("/api/orders", { cache: "no-store" }),
      ]);
      const addressesPayload: unknown = await addressesResponse.json().catch(() => null);
      const ordersPayload: unknown = await ordersResponse.json().catch(() => null);
      if (!addressesResponse.ok || !isAddresses(addressesPayload)) throw new Error(problemMessage(addressesPayload) ?? "Saved addresses could not be loaded.");
      if (!ordersResponse.ok || !isOrders(ordersPayload)) throw new Error(problemMessage(ordersPayload) ?? "Orders could not be loaded.");

      setAddresses(addressesPayload);
      setOrders(ordersPayload);
      setAddressLoadState("ready");
      setLoadState("ready");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Your account could not be loaded.");
      setAddressLoadState("unavailable");
      setLoadState("unavailable");
    }
  }, []);

  useEffect(() => {
    const task = window.setTimeout(() => { void loadAccount(); }, 0);
    return () => window.clearTimeout(task);
  }, [loadAccount]);

  async function reloadAddresses() {
    setAddressLoadState("loading");
    setAddressMessage(null);
    try {
      const response = await fetch("/api/addresses", { cache: "no-store" });
      const payload: unknown = await response.json().catch(() => null);
      if (!response.ok || !isAddresses(payload)) throw new Error(problemMessage(payload) ?? "Saved addresses could not be loaded.");
      setAddresses(payload);
      setAddressLoadState("ready");
    } catch (error) {
      setAddressMessage(error instanceof Error ? error.message : "Saved addresses could not be loaded.");
      setAddressLoadState("unavailable");
    }
  }

  async function mutateAddress(path: string, method: "POST" | "PATCH" | "PUT" | "DELETE", body?: unknown, busyId = "new") {
    setBusyAddressId(busyId);
    setAddressMessage(null);
    try {
      const response = await fetch(`/api/addresses${path}`, {
        method,
        headers: body ? { "Content-Type": "application/json", ...(method === "POST" ? { "Idempotency-Key": crypto.randomUUID() } : {}) } : undefined,
        body: body ? JSON.stringify(body) : undefined,
      });
      if (response.status === 401) throw new Error("Your session has expired. Sign in again to continue.");
      if (!response.ok) {
        const payload: unknown = await response.json().catch(() => null);
        throw new Error(problemMessage(payload) ?? "This address could not be saved.");
      }
      await reloadAddresses();
    } catch (error) {
      setAddressMessage(error instanceof Error ? error.message : "This address could not be saved.");
    } finally {
      setBusyAddressId(null);
    }
  }

  async function updateOrderUpdatePreference(receiveOrderUpdates: boolean) {
    setPreferenceBusy(true);
    setPreferenceMessage(null);
    try {
      const response = await fetch("/api/notification-preferences", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ receiveOrderUpdates }),
      });
      const payload: unknown = await response.json().catch(() => null);
      if (response.status === 401) throw new Error("Your session has expired. Sign in again to continue.");
      if (!response.ok || !isNotificationPreference(payload)) {
        throw new Error(problemMessage(payload) ?? "Notification preferences could not be saved.");
      }
      setUser((current) => current ? { ...current, receiveOrderUpdates: payload.receiveOrderUpdates } : current);
      setPreferenceMessage("Notification preference saved.");
    } catch (error) {
      setPreferenceMessage(error instanceof Error ? error.message : "Notification preferences could not be saved.");
    } finally {
      setPreferenceBusy(false);
    }
  }
  const selectedAddressId = addresses.find((address) => address.isDefault)?.id ?? addresses[0]?.id ?? null;

  if (loadState === "loading") return <Loading />;
  if (loadState === "unauthenticated") return <SignInRequired />;
  if (loadState === "unavailable" || !user) return <Unavailable message={message} onRetry={loadAccount} />;

  return <main className="flex min-h-screen flex-col bg-[var(--background)]">
    <StorefrontHeader />
    <div className="mx-auto max-w-7xl flex-1 px-4 py-8 sm:px-6 lg:px-8"><header className="border-b border-[var(--line)] pb-6"><p className="text-sm font-medium text-[var(--accent)]">Account</p><div className="mt-2 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between"><div><h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">Welcome back, {user.userName}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-[var(--muted)]">Manage delivery addresses and review the orders confirmed under this account.</p></div><EmailState verified={user.isEmailVerified} /></div></header>
      <div className="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_360px]"><section><div className="flex items-center justify-between gap-4"><div><h2 className="text-xl font-semibold">Recent orders</h2><p className="mt-1 text-sm text-[var(--muted)]">Payment and fulfillment updates are shown after the server confirms them.</p></div><Link className="text-sm font-semibold text-[var(--accent)] hover:underline" href="/">Open order status</Link></div><OrderList orders={orders} /></section>
        <aside className="rounded-sm border border-[var(--line)] bg-[var(--surface)] p-5"><div className="flex gap-3"><UserRound aria-hidden="true" className="mt-0.5 text-[var(--accent)]" size={20} /><div><h2 className="font-semibold">Account</h2><p className="mt-1 text-sm text-[var(--muted)]">{user.userName}</p><p className="mt-1 text-xs text-[var(--muted)]">Manage your saved addresses and order-update preference here.</p></div></div><NotificationPreferencePanel busy={preferenceBusy} message={preferenceMessage} receiveOrderUpdates={user.receiveOrderUpdates} verified={user.isEmailVerified} onChange={(value) => void updateOrderUpdatePreference(value)} /><AddressSelection addresses={addresses} busyAddressId={busyAddressId} loadState={addressLoadState} message={addressMessage} onCreate={(input) => void mutateAddress("", "POST", input)} onDelete={(addressId) => void mutateAddress(`/${encodeURIComponent(addressId)}`, "DELETE", undefined, addressId)} onRetry={() => void reloadAddresses()} onSelect={() => undefined} onSetDefault={(addressId) => void mutateAddress(`/${encodeURIComponent(addressId)}/default`, "PUT", undefined, addressId)} onUpdate={(addressId, input) => void mutateAddress(`/${encodeURIComponent(addressId)}`, "PATCH", input, addressId)} selectedAddressId={selectedAddressId} /></aside>
      </div>
    </div>
    <StorefrontFooter />
  </main>;
}

function NotificationPreferencePanel({ busy, message, receiveOrderUpdates, verified, onChange }: { busy: boolean; message: string | null; receiveOrderUpdates: boolean; verified: boolean; onChange: (value: boolean) => void }) {
  return <section className="mt-6 border-t border-[var(--line)] pt-5"><h2 className="font-semibold">Order updates</h2><p className="mt-1 text-sm leading-6 text-[var(--muted)]">Receive email updates after your order is confirmed. Email verification messages are always sent when requested.</p><label className="mt-4 flex cursor-pointer items-start gap-3 text-sm"><input aria-label="Receive order updates" checked={receiveOrderUpdates} className="mt-1 size-4 accent-[var(--accent)]" disabled={busy} onChange={(event) => onChange(event.target.checked)} type="checkbox" /><span><strong className="block">Email me about my orders</strong><span className="mt-1 block text-xs text-[var(--muted)]">{verified ? "Your verified email can receive lifecycle updates." : "Verify your email before order updates can be delivered."}</span></span></label>{message ? <p aria-live="polite" className="mt-3 text-xs text-[var(--muted)]">{message}</p> : null}</section>;
}
function isNotificationPreference(value: unknown): value is { receiveOrderUpdates: boolean } { return typeof value === "object" && value !== null && typeof (value as { receiveOrderUpdates?: unknown }).receiveOrderUpdates === "boolean"; }
function OrderList({ orders }: { orders: OrderSummary[] }) {
  if (orders.length === 0) return <div className="mt-5 border border-dashed border-[var(--line)] bg-[var(--surface)] px-6 py-12 text-center"><ClipboardList aria-hidden="true" className="mx-auto text-[var(--muted)]" size={28} /><h3 className="mt-4 font-semibold">No orders yet</h3><p className="mt-2 text-sm text-[var(--muted)]">When you complete checkout, your confirmed orders will appear here.</p><Link className="mt-5 inline-flex h-10 items-center bg-[var(--accent)] px-4 text-sm font-semibold text-white hover:bg-[var(--accent-strong)]" href="/products">Browse products</Link></div>;
  return <ul className="mt-5 divide-y divide-[var(--line)] rounded-sm border border-[var(--line)] bg-[var(--surface)]">{orders.map((order) => <li className="flex flex-col gap-3 p-5 sm:flex-row sm:items-center sm:justify-between" key={order.id}><div><p className="font-semibold">Order {order.id.slice(0, 8).toUpperCase()}</p><p className="mt-1 text-sm text-[var(--muted)]">{new Date(order.createdAtUtc).toLocaleString()} · {order.items.length} item{order.items.length === 1 ? "" : "s"}</p></div><div className="flex items-center gap-4 sm:text-right"><div><p className="font-semibold">{formatMoney(order.totalAmount, order.currency)}</p><p className="mt-1 text-sm text-[var(--accent)]">{humanize(order.status)}</p></div><Link className="inline-flex h-10 items-center border border-[var(--accent)] px-3 text-sm font-semibold text-[var(--accent)] hover:bg-[#e9f2ed]" href={`/account/orders/${encodeURIComponent(order.id)}`}>View</Link></div></li>)}</ul>;
}
function EmailState({ verified }: { verified: boolean }) { return <span className={`inline-flex items-center gap-2 border px-3 py-2 text-sm font-medium ${verified ? "border-[#b9d7c6] bg-[#f4fbf6] text-[var(--accent-strong)]" : "border-[#f3c5c1] bg-[#fff7f6] text-[var(--danger)]"}`}><ShieldCheck aria-hidden="true" size={16} />{verified ? "Email verified" : "Email verification pending"}</span>; }
function Loading() { return <main className="grid min-h-screen place-items-center bg-[var(--background)] text-[var(--muted)]"><LoaderCircle aria-hidden="true" className="animate-spin" size={25} /></main>; }
function SignInRequired() { return <main className="grid min-h-screen place-items-center bg-[var(--background)] px-4"><section className="max-w-md border border-[var(--line)] bg-[var(--surface)] p-7 text-center"><UserRound aria-hidden="true" className="mx-auto text-[var(--accent)]" size={26} /><h1 className="mt-4 text-xl font-semibold">Sign in to view your account</h1><p className="mt-2 text-sm leading-6 text-[var(--muted)]">Your addresses and order history are available only to the account that created them.</p><Link className="mt-6 inline-flex h-10 items-center bg-[var(--accent)] px-4 text-sm font-semibold text-white hover:bg-[var(--accent-strong)]" href="/">Sign in</Link></section></main>; }
function Unavailable({ message, onRetry }: { message: string | null; onRetry: () => void }) { return <main className="grid min-h-screen place-items-center bg-[var(--background)] px-4"><section className="max-w-md border border-[#f3c5c1] bg-[var(--surface)] p-7 text-center"><MapPin aria-hidden="true" className="mx-auto text-[var(--danger)]" size={26} /><h1 className="mt-4 text-xl font-semibold">Account is temporarily unavailable</h1><p className="mt-2 text-sm leading-6 text-[var(--muted)]">{message ?? "Please try again shortly."}</p><button className="mt-6 inline-flex h-10 items-center gap-2 border border-[var(--accent)] px-4 text-sm font-semibold text-[var(--accent)] hover:bg-[#e9f2ed]" onClick={onRetry} type="button"><RefreshCw aria-hidden="true" size={16} />Retry</button></section></main>; }
function formatMoney(amount: number, currency: string) { return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount); }
function humanize(value: string) { return value.replace(/([a-z])([A-Z])/g, "$1 $2"); }
function isSession(value: unknown): value is { user: CurrentUser } { return typeof value === "object" && value !== null && typeof (value as { user?: unknown }).user === "object" && (value as { user: Record<string, unknown> }).user !== null && typeof (value as { user: Record<string, unknown> }).user.userId === "string"; }
function isAddresses(value: unknown): value is CustomerAddress[] { return Array.isArray(value) && value.every((address) => typeof address === "object" && address !== null && typeof (address as Record<string, unknown>).id === "string"); }
function isOrders(value: unknown): value is OrderSummary[] { return Array.isArray(value) && value.every((order) => typeof order === "object" && order !== null && typeof (order as Record<string, unknown>).id === "string" && Array.isArray((order as Record<string, unknown>).items)); }