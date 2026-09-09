"use client";

import Link from "next/link";
import { ArrowLeft, CircleAlert, ClipboardList, CreditCard, LoaderCircle, RefreshCw, Search, ShieldAlert } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { OperationsWorkspace } from "@/components/operations-workspace";
import { FulfillmentCell } from "@/components/fulfillment-cell";
import { loadOrderPaymentQueue, OperationsApiError, type OrderPaymentRow } from "@/lib/operations/order-payment-queue";

type User = { userId: string; userName: string; role: string };
type QueueFilter = "all" | "attention" | "unpaid" | "paid";

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const dateTime = new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" });

export default function OrdersPage() {
  const [authorized, setAuthorized] = useState<boolean | null>(null);
  const [orders, setOrders] = useState<OrderPaymentRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<QueueFilter>("attention");
  const [query, setQuery] = useState("");
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setOrders(await loadOrderPaymentQueue());
      setUpdatedAt(new Date());
    } catch (exception) {
      if (exception instanceof OperationsApiError && (exception.status === 401 || exception.status === 403)) {
        window.location.replace("/");
        return;
      }
      setError(exception instanceof Error ? exception.message : "Orders could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();

    async function initialize() {
      try {
        const response = await fetch("/api/session", { signal: controller.signal });
        const payload = response.ok ? await response.json().catch(() => null) : null;
        const user = isUser(payload?.user) ? payload.user : null;
        if (!active) return;
        if (!user || user.role !== "Admin") {
          window.location.replace("/");
          return;
        }
        setAuthorized(true);
        await load();
      } catch (exception) {
        if (!active || isAbortError(exception)) return;
        setAuthorized(true);
        setError("Your session could not be checked. Refresh to try again.");
      }
    }

    void initialize();
    return () => { active = false; controller.abort(); };
  }, [load]);

  const summary = useMemo(() => ({
    attention: orders.filter(needsAttention).length,
    pending: orders.filter((order) => order.payment && isPendingPayment(order.payment.status)).length,
    paidRevenue: orders.filter((order) => order.payment?.status === "Captured").reduce((sum, order) => sum + order.totalAmount, 0),
  }), [orders]);

  const visibleOrders = useMemo(() => {
    const term = query.trim().toLowerCase();
    return orders.filter((order) => {
      if (filter === "attention" && !needsAttention(order)) return false;
      if (filter === "unpaid" && order.payment?.status === "Captured") return false;
      if (filter === "paid" && order.payment?.status !== "Captured") return false;
      return !term || [order.id, order.customerId, order.status, order.payment?.id, order.payment?.providerTransactionId, order.payment?.failureReason]
        .filter((value): value is string => typeof value === "string")
        .some((value) => value.toLowerCase().includes(term));
    });
  }, [filter, orders, query]);

  if (authorized !== true) {
    return <main className="signin"><div><LoaderCircle className="spin" size={22} /><p>Checking administrator accessÃ¢â‚¬Â¦</p></div></main>;
  }

  return <OperationsWorkspace area="orders"><main className="orders-page">
    <header className="orders-header">
      <Link className="back" href="/"><ArrowLeft size={17} />Catalog control</Link>
      <div className="orders-title">
        <div><p className="eyebrow">Operations / P0</p><h1>Order payment queue</h1><p className="page-summary">Review checkout orders against their recorded payment. Payment and order updates can arrive at different times.</p></div>
        <button className="icon-button" aria-label="Refresh order payment queue" title="Refresh order payment queue" disabled={loading} onClick={() => void load()}>{loading ? <LoaderCircle className="spin" size={18} /> : <RefreshCw size={18} />}</button>
      </div>
    </header>

    <div className="metrics">
      <Metric label="Needs review" value={String(summary.attention)} tone="danger" />
      <Metric label="Payment pending" value={String(summary.pending)} tone="warn" />
      <Metric label="Captured value" value={money.format(summary.paidRevenue)} />
    </div>

    {error ? <div className="notice"><CircleAlert size={19} /><span>{error}</span><button onClick={() => void load()}>Retry</button></div> : null}

    <div className="queue-toolbar">
      <label className="search"><Search size={17} /><span className="sr-only">Search order payment queue</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Order, customer, payment or provider reference" /></label>
      <div className="filter-row" aria-label="Queue filter">
        {(["attention", "all", "unpaid", "paid"] as QueueFilter[]).map((value) => <button key={value} className={filter === value ? "filter-active" : ""} aria-pressed={filter === value} onClick={() => setFilter(value)}>{filterLabel(value)}</button>)}
      </div>
    </div>

    <p className="queue-meta">{loading ? "Refreshing queueÃ¢â‚¬Â¦" : updatedAt ? `Last refreshed ${dateTime.format(updatedAt)} Ã‚Â· ${visibleOrders.length} shown` : "Loading queueÃ¢â‚¬Â¦"}</p>
    <div className="table-wrap">
      <table><thead><tr><th>Order</th><th>Payment</th><th>Customer</th><th>Created</th><th>Items</th><th>Total</th><th>Fulfillment</th></tr></thead>
        <tbody>{visibleOrders.map((order) => <tr key={order.id}>
          <td><strong><ClipboardList size={16} />{shortId(order.id)}</strong><span><Status value={order.status} /></span></td>
          <td><PaymentCell payment={order.payment} /></td>
          <td><code>{shortId(order.customerId)}</code></td>
          <td>{formatDate(order.createdAtUtc)}</td>
          <td>{order.items.length}</td>
          <td><strong>{money.format(order.totalAmount)}</strong><span>{order.discountCode ? `${order.discountCode} applied` : order.currency}</span></td>
          <td><FulfillmentCell order={order} onUpdated={() => void load()} /></td>
        </tr>)}</tbody>
      </table>
      {!loading && !error && visibleOrders.length === 0 ? <div className="empty"><ShieldAlert size={22} /><p>{orders.length === 0 ? "No checkout orders have been created yet." : "No orders match the current queue filter."}</p>{orders.length > 0 ? <button className="command" onClick={() => { setFilter("all"); setQuery(""); }}>Clear filters</button> : null}</div> : null}
    </div>
  </main></OperationsWorkspace>;
}

function PaymentCell({ payment }: { payment: OrderPaymentRow["payment"] }) {
  if (!payment) return <><strong><CreditCard size={16} />No recent payment record</strong><span className="payment-hint">The payment API exposes a capped recent list, so older payments may not appear here.</span></>;
  return <><strong><CreditCard size={16} />{shortId(payment.id)}</strong><span><PaymentStatus value={payment.status} reason={payment.failureReason} /></span>{payment.providerTransactionId ? <code>{payment.providerTransactionId}</code> : null}</>;
}

function Metric({ label, value, tone }: { label: string; value: string; tone?: string }) { return <div className={`metric ${tone ?? ""}`}><span>{label}</span><strong>{value}</strong></div>; }
function Status({ value }: { value: string }) { const className = value === "Paid" ? "status paid" : value === "Cancelled" ? "status cancelled" : "status pending"; return <span className={className}>{humanize(value)}</span>; }
function PaymentStatus({ value, reason }: { value: string; reason: string | null }) { const lower = value.toLowerCase(); const className = lower.includes("captured") ? "status paid" : lower.includes("failed") ? "status cancelled" : lower.includes("refund") || lower.includes("void") ? "status neutral" : "status pending"; return <span className={className}>{humanize(value)}{reason ? `: ${reason}` : ""}</span>; }
function needsAttention(order: OrderPaymentRow) { return order.payment !== null && (order.payment.failureReason !== null || order.payment.status === "Failed" || (order.status === "Paid" && order.payment.status !== "Captured")); }
function isPendingPayment(status: string) { return status === "PendingAuthorization" || status === "Authorized" || status === "CapturePending"; }
function humanize(value: string) { return value.replace(/([A-Z])/g, " $1").trim(); }
function shortId(value: string) { return value.slice(0, 8); }
function formatDate(value: string) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? "Unknown" : dateTime.format(date); }
function filterLabel(value: QueueFilter) { return value === "attention" ? "Needs review" : value === "all" ? "All orders" : value === "unpaid" ? "Not captured" : "Captured"; }
function isUser(value: unknown): value is User { return typeof value === "object" && value !== null && typeof (value as Record<string, unknown>).userId === "string" && typeof (value as Record<string, unknown>).role === "string"; }
function isAbortError(value: unknown) { return value instanceof DOMException && value.name === "AbortError"; }
