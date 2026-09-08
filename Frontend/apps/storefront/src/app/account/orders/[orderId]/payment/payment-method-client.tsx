"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { ArrowLeft, CreditCard, LoaderCircle } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { PaymentMethodSelector } from "@/components/payment-method-selector";
import { problemMessage } from "@/lib/http/problem-details";
import type { PaymentSummary } from "@/lib/storefront/types";

type Order = { id: string; status: string; totalAmount: number; currency: string };
type LoadState = "loading" | "ready" | "not-found" | "unavailable";

export function PaymentMethodClient() {
  const { orderId } = useParams<{ orderId: string }>();
  const router = useRouter();
  const [state, setState] = useState<LoadState>("loading");
  const [order, setOrder] = useState<Order | null>(null);
  const [provider, setProvider] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const paymentActionKeys = useRef(new Map<string, string>());

  const loadOrder = useCallback(async () => {
    if (!isGuid(orderId)) {
      setState("not-found");
      return;
    }

    setState("loading");
    setMessage(null);
    try {
      const response = await fetch(`/api/orders/${encodeURIComponent(orderId)}`, { cache: "no-store" });
      const payload: unknown = await response.json().catch(() => null);
      if (response.status === 404) {
        setState("not-found");
        return;
      }
      if (!response.ok || !isOrder(payload)) {
        throw new Error(problemMessage(payload) ?? "Order details could not be loaded.");
      }
      setOrder(payload);
      setState("ready");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Order details could not be loaded.");
      setState("unavailable");
    }
  }, [orderId]);

  useEffect(() => {
    const task = window.setTimeout(() => { void loadOrder(); }, 0);
    return () => window.clearTimeout(task);
  }, [loadOrder]);

  async function createPayment() {
    if (!order || !provider || busy) return;

    setBusy(true);
    setMessage(null);
    try {
      const response = await fetch("/api/payments", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": getOrCreatePaymentActionKey(paymentActionKeys.current, provider) },
        body: JSON.stringify({ orderId: order.id, provider }),
      });
      const payload: unknown = await response.json().catch(() => null);
      if (!response.ok || !isAction(payload)) {
        throw new Error(problemMessage(payload) ?? "A payment action could not be created.");
      }

      if (payload.action.checkoutUrl) {
        window.location.assign(payload.action.checkoutUrl);
        return;
      }

      router.push(`/account/orders/${encodeURIComponent(order.id)}`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "A payment action could not be created.");
    } finally {
      setBusy(false);
    }
  }

  const backHref = isGuid(orderId) ? `/account/orders/${encodeURIComponent(orderId)}` : "/account";
  if (state === "loading") return <main className="grid min-h-screen place-items-center bg-[var(--background)] text-[var(--muted)]"><LoaderCircle aria-hidden="true" className="animate-spin" size={24} /></main>;
  if (state === "not-found") return <Empty backHref="/account" detail="This order does not exist or does not belong to this account." title="Order not found" />;
  if (state === "unavailable" || !order) return <Empty backHref={backHref} detail={message ?? "Try again shortly."} onRetry={loadOrder} title="Payment options are temporarily unavailable" />;

  const eligible = ["Pending", "PendingPayment", "PaymentFailed"].includes(order.status);
  return <main className="min-h-screen bg-[var(--background)]"><header className="border-b border-[var(--line)] bg-[var(--surface)]"><div className="mx-auto max-w-xl px-4 py-3 sm:px-6"><Link className="inline-flex items-center gap-2 text-sm font-medium text-[var(--accent)] hover:underline" href={backHref}><ArrowLeft aria-hidden="true" size={16} />Back to order</Link></div></header><div className="mx-auto max-w-xl px-4 py-8 sm:px-6"><p className="text-sm font-medium text-[var(--accent)]">Secure checkout</p><h1 className="mt-2 text-3xl font-semibold">Choose payment method</h1><p className="mt-3 text-sm leading-6 text-[var(--muted)]">Order {order.id.slice(0, 8).toUpperCase()} - {money(order.totalAmount, order.currency)}</p>{message ? <p className="mt-5 border-l-2 border-[var(--danger)] bg-[#fff7f6] px-3 py-3 text-sm text-[var(--danger)]" role="alert">{message}</p> : null}{eligible ? <section className="mt-8 border border-[var(--line)] bg-[var(--surface)] p-5"><PaymentMethodSelector currency={order.currency} disabled={busy} onChange={setProvider} selectedProvider={provider} /><button className="mt-6 inline-flex h-11 w-full items-center justify-center gap-2 bg-[var(--accent)] px-4 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60" disabled={!provider || busy} onClick={() => void createPayment()} type="button">{busy ? <LoaderCircle aria-hidden="true" className="animate-spin" size={16} /> : <CreditCard aria-hidden="true" size={16} />}{busy ? "Opening payment..." : "Continue securely"}</button><p className="mt-3 text-xs leading-5 text-[var(--muted)]">Opening a provider page does not confirm payment. Your order changes only after the verified provider callback is processed.</p></section> : <section className="mt-8 border-l-2 border-[#d8d6c5] bg-[#fbfaf2] px-4 py-4 text-sm text-[var(--muted)]">This order is not eligible for a new payment action. Review its latest status before trying again.</section>}</div></main>;
}

function Empty({ backHref, detail, onRetry, title }: { backHref: string; detail: string; onRetry?: () => void; title: string }) {
  return <main className="grid min-h-screen place-items-center bg-[var(--background)] px-4"><section className="max-w-md border border-[var(--line)] bg-[var(--surface)] p-7 text-center"><h1 className="text-xl font-semibold">{title}</h1><p className="mt-2 text-sm text-[var(--muted)]">{detail}</p>{onRetry ? <button className="mt-6 inline-flex h-10 items-center bg-[var(--accent)] px-4 text-sm font-semibold text-white" onClick={onRetry} type="button">Retry</button> : <Link className="mt-6 inline-flex h-10 items-center bg-[var(--accent)] px-4 text-sm font-semibold text-white" href={backHref}>Back to account</Link>}</section></main>;
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

function isOrder(value: unknown): value is Order {
  if (!value || typeof value !== "object") return false;
  const item = value as Record<string, unknown>;
  return typeof item.id === "string" && typeof item.status === "string" && typeof item.totalAmount === "number" && typeof item.currency === "string";
}

function isPayment(value: unknown): value is PaymentSummary {
  return !!value && typeof value === "object" && typeof (value as Record<string, unknown>).id === "string" && typeof (value as Record<string, unknown>).status === "string" && typeof (value as Record<string, unknown>).amount === "number" && typeof (value as Record<string, unknown>).currency === "string";
}

function isAction(value: unknown): value is { payment: PaymentSummary; action: { checkoutUrl: string | null } } {
  if (!value || typeof value !== "object") return false;
  const item = value as { payment?: unknown; action?: { checkoutUrl?: unknown } };
  return isPayment(item.payment) && !!item.action && (typeof item.action.checkoutUrl === "string" || item.action.checkoutUrl === null);
}

function getOrCreatePaymentActionKey(keys: Map<string, string>, provider: string): string {
  const existing = keys.get(provider);
  if (existing) return existing;

  const value = crypto.randomUUID();
  keys.set(provider, value);
  return value;
}

function money(amount: number, currency: string): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount);
}
