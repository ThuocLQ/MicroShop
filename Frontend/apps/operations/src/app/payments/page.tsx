"use client";

import Link from "next/link";
import { ArrowLeft, BadgeDollarSign, CircleAlert, Filter, History, LoaderCircle, RefreshCw, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { OperationsWorkspace } from "@/components/operations-workspace";
import { problemMessage } from "@/lib/http/problem-details";
import { loadPaymentOperationalActions, type PaymentOperationalAction } from "@/lib/operations/payment-operational-actions";

type Payment = {
  id: string;
  orderId: string;
  customerId: string;
  amount: number;
  currency: string;
  status: string;
  providerTransactionId: string | null;
  failureReason: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
};

export default function PaymentsPage() {
  const [payments, setPayments] = useState<Payment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedPayment, setSelectedPayment] = useState<Payment | null>(null);
  const [actions, setActions] = useState<PaymentOperationalAction[]>([]);
  const [actionsLoading, setActionsLoading] = useState(false);
  const [actionsError, setActionsError] = useState<string | null>(null);
  const [view, setView] = useState<"exceptions" | "all">("exceptions");

  const getPayments = useCallback(async () => {
    const response = await fetch("/api/payments/admin?limit=100", { cache: "no-store" });
    const payload: unknown = await response.json().catch(() => null);
    if (!response.ok || !isPaymentList(payload)) throw new Error(messageOf(payload) ?? "Payments could not be loaded.");
    return payload;
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setPayments(await getPayments());
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Payments could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [getPayments]);

  const loadActions = useCallback(async (payment: Payment) => {
    setSelectedPayment(payment);
    setActionsLoading(true);
    setActionsError(null);
    try {
      setActions(await loadPaymentOperationalActions(payment.id));
    } catch (exception) {
      setActions([]);
      setActionsError(exception instanceof Error ? exception.message : "Payment audit actions could not be loaded.");
    } finally {
      setActionsLoading(false);
    }
  }, []);

  useEffect(() => {
    const timerId = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timerId);
  }, [load]);

  const summary = useMemo(() => ({
    awaiting: payments.filter(payment => payment.status === "PendingAuthorization" || payment.status === "Authorized" || payment.status === "CapturePending").length,
    captured: payments.filter(payment => payment.status === "Captured").length,
    exceptions: payments.filter(needsReview).length,
  }), [payments]);
  const visiblePayments = useMemo(() => view === "exceptions" ? payments.filter(needsReview) : payments, [payments, view]);

  return <OperationsWorkspace area="payments"><main className="orders-page">
    <header className="orders-header">
      <Link className="back" href="/"><ArrowLeft size={17} />Catalog control</Link>
      <div className="orders-title">
        <div><p className="eyebrow">Operations</p><h1>Payment ledger</h1><p className="page-summary">Latest 100 payment records. Amounts are shown in their recorded currency and are never aggregated across currencies.</p></div>
        <button className="icon-button" aria-label="Refresh payments" title="Refresh payments" disabled={loading} onClick={() => void load()}>{loading ? <LoaderCircle className="spin" size={18} /> : <RefreshCw size={18} />}</button>
      </div>
    </header>

    <div className="metrics">
      <Metric label="Payment records" value={payments.length.toString()} />
      <Metric label="Awaiting provider" value={summary.awaiting.toString()} tone="warn" />
      <Metric label="Captured payments" value={summary.captured.toString()} />
    </div>

    {error ? <div className="notice"><CircleAlert size={19} /><span>{error}</span><button onClick={() => void load()}>Retry</button></div> : null}
    <div className="section-heading payment-queue-heading"><div><p className="eyebrow">Triage queue</p><h2>{view === "exceptions" ? "Payments needing review" : "All recent payments"}</h2></div><div className="button-group" aria-label="Payment list filter"><button className={view === "exceptions" ? "command active" : "command"} type="button" onClick={() => setView("exceptions")}><Filter size={16} />Needs review ({summary.exceptions})</button><button className={view === "all" ? "command active" : "command"} type="button" onClick={() => setView("all")}>All records</button></div></div>
    <div className="table-wrap"><table><thead><tr><th>Payment</th><th>Order</th><th>Created</th><th>Status</th><th>Provider reference</th><th>Amount</th><th>Audit</th></tr></thead><tbody>{visiblePayments.map(payment => <tr key={payment.id}><td><strong><BadgeDollarSign size={16} />{shortId(payment.id)}</strong><span>{shortId(payment.customerId)}</span></td><td><code>{shortId(payment.orderId)}</code></td><td>{formatDate(payment.createdAtUtc)}</td><td><PaymentStatus value={payment.status} reason={payment.failureReason} /></td><td><code>{payment.providerTransactionId ?? "Not assigned"}</code></td><td><strong>{formatMoney(payment.amount, payment.currency)}</strong><span>{payment.currency}</span></td><td><button className="command" type="button" onClick={() => void loadActions(payment)}><History size={16} />View audit</button></td></tr>)}</tbody></table>{!loading && visiblePayments.length === 0 ? <div className="empty">{view === "exceptions" ? "No payment exceptions need review." : "No payments have been initiated yet."}</div> : null}</div>

    {selectedPayment ? <section className="operations-section" aria-labelledby="payment-audit-heading"><div className="section-heading"><div><p className="eyebrow">Server audit</p><h2 id="payment-audit-heading">Payment {shortId(selectedPayment.id)}</h2></div><button className="icon-button" type="button" aria-label="Close payment audit" title="Close payment audit" onClick={() => setSelectedPayment(null)}><X size={18} /></button></div>{actionsError ? <div className="notice"><CircleAlert size={19} /><span>{actionsError}</span><button onClick={() => void loadActions(selectedPayment)}>Retry</button></div> : null}<div className="table-wrap"><table><thead><tr><th>Action</th><th>Requested by</th><th>Requested</th><th>Outcome</th><th>Reason</th></tr></thead><tbody>{actions.map(action => <tr key={action.id}><td><strong>{action.actionType}</strong></td><td>{action.requestedBy}</td><td>{formatDate(action.requestedAtUtc)}</td><td>{action.completedAtUtc ? <span className="status paid">Completed {formatDate(action.completedAtUtc)}</span> : action.failureReason ? <span className="status cancelled">Failed</span> : <span className="status pending">Pending confirmation</span>}</td><td><span className="audit-reason">{action.failureReason ?? action.reason}</span></td></tr>)}</tbody></table>{actionsLoading ? <div className="empty"><LoaderCircle className="spin" size={20} />Loading payment audit...</div> : !actionsError && actions.length === 0 ? <div className="empty">No operational actions are recorded for this payment.</div> : null}</div></section> : null}
  </main></OperationsWorkspace>;
}

function Metric({ label, value, tone }: { label: string; value: string; tone?: string }) { return <div className={`metric ${tone ?? ""}`}><span>{label}</span><strong>{value}</strong></div>; }
function PaymentStatus({ value, reason }: { value: string; reason: string | null }) { const lower = value.toLowerCase(); const className = lower.includes("captured") ? "status paid" : lower.includes("failed") ? "status cancelled" : lower.includes("refund") || lower.includes("void") ? "status neutral" : "status pending"; return <span className={className}>{value.replace(/([A-Z])/g, " $1").trim()}{reason ? `: ${reason}` : ""}</span>; }
function shortId(value: string) { return value.slice(0, 8); }
function formatDate(value: string) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? "Unknown" : date.toLocaleString(); }
function formatMoney(amount: number, currency: string) { try { return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount); } catch { return `${amount.toFixed(2)} ${currency}`; } }
function needsReview(payment: Payment) { return payment.status === "Failed" || payment.status === "ReconciliationRequired" || payment.status === "VoidPending" || payment.status === "RefundPending" || payment.status === "CapturePending"; }
function isPaymentList(value: unknown): value is Payment[] { return Array.isArray(value) && value.every((item) => isRecord(item) && typeof item.id === "string" && typeof item.orderId === "string" && typeof item.customerId === "string" && typeof item.amount === "number" && typeof item.currency === "string" && typeof item.status === "string" && (item.providerTransactionId === null || typeof item.providerTransactionId === "string") && (item.failureReason === null || typeof item.failureReason === "string") && typeof item.createdAtUtc === "string" && (item.completedAtUtc === null || typeof item.completedAtUtc === "string")); }
const messageOf = problemMessage;
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null; }