"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { ArrowLeft, CheckCircle2, CircleAlert, LoaderCircle, RefreshCw } from "lucide-react";
import type { PaymentSummary } from "@/lib/storefront/types";
import { problemMessage } from "@/lib/http/problem-details";

type PaymentReturnClientProps = { cancelled: boolean };

export function PaymentReturnClient({ cancelled }: PaymentReturnClientProps) {
  const [payment, setPayment] = useState<PaymentSummary | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const paymentId = typeof window === "undefined" ? null : new URLSearchParams(window.location.search).get("paymentId");

  const refresh = useCallback(async () => {
    if (!paymentId) {
      setMessage("The payment reference is missing. Return to your orders and try again.");
      setLoading(false);
      return;
    }

    setLoading(true);
    try {
      const response = await fetch(`/api/payments/${encodeURIComponent(paymentId)}`, { cache: "no-store" });
      const payload: unknown = await response.json().catch(() => null);
      if (!response.ok || !isPayment(payload)) {
        throw new Error(problemMessage(payload) ?? "Payment status could not be loaded.");
      }

      setPayment(payload);
      setMessage(null);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Payment status could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [paymentId]);

  const paymentStatus = payment?.status;

  useEffect(() => {
    const task = window.setTimeout(() => void refresh(), 0);
    return () => window.clearTimeout(task);
  }, [refresh]);

  useEffect(() => {
    if (!paymentStatus || isFinal(paymentStatus)) {
      return;
    }

    const timer = window.setInterval(() => void refresh(), 5000);
    return () => window.clearInterval(timer);
  }, [paymentStatus, refresh]);

  const title = cancelled ? "Payment was not completed" : paymentStatus && isFinal(paymentStatus) ? "Payment status updated" : "Confirming your payment";
  const description = cancelled
    ? "No payment has been confirmed. You can return to your orders and choose another available payment action while the order is still awaiting payment."
    : paymentStatus && isFinal(paymentStatus)
      ? "The provider response has been recorded. Your order may continue processing asynchronously."
      : "We are waiting for the provider webhook. Do not submit payment again while this status is refreshing.";

  return <main className="min-h-screen bg-[var(--background)] px-4 py-8 sm:px-6 sm:py-12">
    <section className="mx-auto max-w-xl border border-[var(--line)] bg-[var(--surface)] p-6 sm:p-8">
      <Link className="inline-flex items-center gap-2 text-sm font-medium text-[var(--accent)] hover:underline" href="/"><ArrowLeft aria-hidden="true" size={16} />Back to store</Link>
      <div className="mt-8 flex gap-3"><div className="pt-0.5 text-[var(--accent)]">{message ? <CircleAlert aria-hidden="true" size={23} /> : loading ? <LoaderCircle aria-hidden="true" className="animate-spin" size={23} /> : <CheckCircle2 aria-hidden="true" size={23} />}</div><div><p className="text-sm font-medium text-[var(--accent)]">Payment</p><h1 className="mt-1 text-2xl font-semibold">{title}</h1><p className="mt-2 text-sm leading-6 text-[var(--muted)]">{description}</p></div></div>
      {message ? <p className="mt-6 border-l-2 border-[var(--danger)] bg-[#fff7f6] px-3 py-2 text-sm text-[var(--danger)]" role="alert">{message}</p> : null}
      {payment ? <dl className="mt-7 divide-y divide-[var(--line)] border-y border-[var(--line)] text-sm"><Row label="Payment" value={payment.id.slice(0, 8).toUpperCase()} /><Row label="Provider" value={payment.provider ?? "Pending"} /><Row label="Status" value={humanize(payment.status)} /><Row label="Amount" value={formatMoney(payment.amount, payment.currency)} /><Row label="Order" value={payment.orderId.slice(0, 8).toUpperCase()} />{payment.failureReason ? <Row label="Reason" value={payment.failureReason} /> : null}</dl> : null}
      <button className="mt-7 inline-flex h-11 w-full items-center justify-center gap-2 border border-[var(--accent)] px-4 text-sm font-semibold text-[var(--accent)] hover:bg-[#e9f2ed] disabled:opacity-60" disabled={loading} onClick={() => void refresh()} type="button"><RefreshCw aria-hidden="true" className={loading ? "animate-spin" : ""} size={16} />Refresh payment status</button>
    </section>
  </main>;
}

function Row({ label, value }: { label: string; value: string }) { return <div className="flex items-start justify-between gap-5 py-3"><dt className="text-[var(--muted)]">{label}</dt><dd className="text-right font-medium">{value}</dd></div>; }
function formatMoney(amount: number, currency: string) { return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount); }
function isFinal(status: string) { return ["Captured", "Failed", "Voided", "Refunded", "ReconciliationRequired"].includes(status); }
function humanize(value: string) { return value.replace(/([a-z])([A-Z])/g, "$1 $2"); }
function isPayment(value: unknown): value is PaymentSummary { return typeof value === "object" && value !== null && typeof (value as Record<string, unknown>).id === "string" && typeof (value as Record<string, unknown>).status === "string" && typeof (value as Record<string, unknown>).amount === "number" && typeof (value as Record<string, unknown>).currency === "string"; }
