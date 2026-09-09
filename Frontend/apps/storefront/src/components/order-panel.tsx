"use client";

import { Ban, ClipboardList, CreditCard, LoaderCircle, RefreshCw, X } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import type { OrderSummary, PaymentSummary } from "@/lib/storefront/types";

type OrderPanelProps = {
  isLoading: boolean;
  completingSandboxPaymentId: string | null;
  cancellingOrderId: string | null;
  message: string | null;
  orders: OrderSummary[];
  paymentsByOrder: Record<string, PaymentSummary | null>;
  recentOrder: OrderSummary | null;
  paymentMessage: string | null;
  onClose: () => void;
  onRetry: () => void;
  onCompleteSandboxPayment: (paymentId: string, orderId: string) => void;
  onCancelOrder: (orderId: string) => void;
};

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const dateTime = new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" });

export function OrderPanel({
  isLoading,
  completingSandboxPaymentId,
  cancellingOrderId,
  message,
  orders,
  paymentsByOrder,
  recentOrder,
  paymentMessage,
  onClose,
  onRetry,
  onCompleteSandboxPayment,
  onCancelOrder,
}: OrderPanelProps) {
  const [confirmingCancellationOrderId, setConfirmingCancellationOrderId] = useState<string | null>(null);
  const visibleOrders = recentOrder && !orders.some((order) => order.id === recentOrder.id)
    ? [recentOrder, ...orders]
    : orders;

  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-black/35" role="presentation">
      <aside aria-label="Orders and account" className="flex h-full w-full max-w-xl flex-col bg-[var(--surface)] shadow-xl">
        <header className="flex min-h-16 items-center justify-between border-b border-[var(--line)] px-5">
          <div className="flex items-center gap-3">
            <ClipboardList aria-hidden="true" size={19} />
            <div>
              <h2 className="font-semibold">Orders &amp; account</h2>
              <p className="text-xs text-[var(--muted)]">Your confirmed order history</p>
            </div>
          </div>
          <button aria-label="Close orders" className="grid size-9 place-items-center border border-[var(--line)] text-[var(--muted)] hover:bg-[#f3f5f2]" onClick={onClose} type="button">
            <X aria-hidden="true" size={18} />
          </button>
        </header>

        {message ? <ErrorNotice isLoading={isLoading} message={message} onRetry={onRetry} /> : null}
        {paymentMessage ? <p className="mx-5 mt-4 border-l-2 border-[var(--accent)] bg-[#f4fbf6] px-3 py-2 text-sm text-[var(--accent-strong)]" role="status">{paymentMessage}</p> : null}
        {isLoading && visibleOrders.length > 0 ? <p aria-live="polite" className="mx-5 mt-4 flex items-center gap-2 text-sm text-[var(--muted)]"><LoaderCircle aria-hidden="true" className="animate-spin" size={16} />Refreshing order status...</p> : null}

        {isLoading && visibleOrders.length === 0 ? (
          <div aria-label="Loading orders" className="grid flex-1 place-items-center text-[var(--muted)]"><LoaderCircle aria-hidden="true" className="animate-spin" size={23} /></div>
        ) : visibleOrders.length === 0 ? (
          <div className="grid flex-1 place-items-center px-8 text-center"><div><ClipboardList aria-hidden="true" className="mx-auto text-[var(--muted)]" size={30} /><h3 className="mt-4 font-semibold">No orders yet</h3><p className="mt-2 text-sm text-[var(--muted)]">Orders you place will appear here.</p></div></div>
        ) : (
          <ul className="flex-1 divide-y divide-[var(--line)] overflow-y-auto">
            {visibleOrders.map((order) => <OrderRow cancellingOrderId={cancellingOrderId} completingSandboxPaymentId={completingSandboxPaymentId} confirmingCancellationOrderId={confirmingCancellationOrderId} key={order.id} onCancelOrder={onCancelOrder} onCompleteSandboxPayment={onCompleteSandboxPayment} onToggleCancellationConfirmation={(orderId) => setConfirmingCancellationOrderId((current) => current === orderId ? null : orderId)} order={order} payment={paymentsByOrder[order.id] ?? null} />)}
          </ul>
        )}
      </aside>
    </div>
  );
}

function ErrorNotice({ isLoading, message, onRetry }: { isLoading: boolean; message: string; onRetry: () => void }) {
  return <div className="mx-5 mt-4 border-l-2 border-[var(--danger)] bg-[#fff7f6] px-3 py-2 text-sm text-[var(--danger)]" role="alert"><p>{message}</p><button className="mt-2 inline-flex h-8 items-center gap-2 border border-[var(--danger)] px-3 text-sm font-semibold hover:bg-white disabled:opacity-60" disabled={isLoading} onClick={onRetry} type="button"><RefreshCw aria-hidden="true" size={15} />Retry</button></div>;
}

function OrderRow({ order, payment, completingSandboxPaymentId, cancellingOrderId, confirmingCancellationOrderId, onCompleteSandboxPayment, onCancelOrder, onToggleCancellationConfirmation }: { order: OrderSummary; payment: PaymentSummary | null; completingSandboxPaymentId: string | null; cancellingOrderId: string | null; confirmingCancellationOrderId: string | null; onCompleteSandboxPayment: (paymentId: string, orderId: string) => void; onCancelOrder: (orderId: string) => void; onToggleCancellationConfirmation: (orderId: string) => void }) {
  const isCompletingSandboxPayment = payment !== null && completingSandboxPaymentId === payment.id;
  const isCancelling = cancellingOrderId === order.id;
  const isCancellationConfirmationOpen = confirmingCancellationOrderId === order.id;

  return (
    <li className="px-5 py-5">
      <div className="flex items-start justify-between gap-4"><div><p className="font-medium">Order {shortId(order.id)}</p><p className="mt-1 text-sm text-[var(--muted)]">{formatDate(order.createdAtUtc)}</p></div><StatusBadge status={order.status} /></div>
      <div className="mt-4 space-y-2 text-sm">{order.items.map((item) => <div className="flex justify-between gap-4" key={item.id}><span className="min-w-0 text-[var(--muted)]">{item.quantity} x {item.productName}</span><span className="shrink-0">{money.format(item.totalPrice)}</span></div>)}</div>
      {order.shippingAddress ? <AddressSnapshot order={order} /> : null}
      <div className="mt-4 flex justify-between gap-4 border-t border-[var(--line)] pt-3 font-semibold"><span>Total</span><span className="text-right">{money.format(order.totalAmount)} {order.currency}</span></div>
      <PaymentState isCompletingSandboxPayment={isCompletingSandboxPayment} onCompleteSandboxPayment={onCompleteSandboxPayment} order={order} payment={payment} />
      <CancellationAction isCancelling={isCancelling} isConfirmationOpen={isCancellationConfirmationOpen} order={order} onCancelOrder={onCancelOrder} onToggleConfirmation={onToggleCancellationConfirmation} />
    </li>
  );
}

function PaymentState({ order, payment, isCompletingSandboxPayment, onCompleteSandboxPayment }: { order: OrderSummary; payment: PaymentSummary | null; isCompletingSandboxPayment: boolean; onCompleteSandboxPayment: (paymentId: string, orderId: string) => void }) {
  if (payment) {
    const canCompleteSandboxPayment = payment.provider === "Sandbox" && payment.status === "PendingAuthorization";
    const canChooseAnotherMethod = ["Failed", "Expired", "Cancelled"].includes(payment.status);
    return <section className="mt-4 border border-[var(--line)] bg-[#fbfcfa] p-3 text-sm"><div className="flex items-center justify-between gap-3"><div><p className="font-medium">Payment {shortId(payment.id)}</p><p className="mt-1 text-xs text-[var(--muted)]">{payment.provider ?? "Payment provider pending"} - {formatDate(payment.createdAtUtc)}</p></div><StatusBadge status={payment.status} /></div><div className="mt-3 flex justify-between gap-3"><span className="text-[var(--muted)]">Amount</span><span className="font-semibold">{money.format(payment.amount)} {payment.currency}</span></div>{payment.paymentActionExpiresAtUtc && isAwaitingProvider(payment.status) ? <p className="mt-2 text-xs text-[var(--muted)]">Provider action expires {formatDate(payment.paymentActionExpiresAtUtc)}. Order status changes only after provider confirmation.</p> : null}{payment.completedAtUtc ? <p className="mt-2 text-xs text-[var(--muted)]">Last confirmed {formatDate(payment.completedAtUtc)}.</p> : null}{payment.failureReason ? <p className="mt-2 text-xs font-medium text-[var(--danger)]">{payment.failureReason}</p> : null}{canCompleteSandboxPayment ? <button className="mt-3 inline-flex min-h-9 items-center gap-2 bg-[var(--accent)] px-3 py-1 text-sm font-semibold text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:bg-[#8ba89b]" disabled={isCompletingSandboxPayment} onClick={() => onCompleteSandboxPayment(payment.id, order.id)} type="button">{isCompletingSandboxPayment ? <LoaderCircle aria-hidden="true" className="animate-spin" size={16} /> : <CreditCard aria-hidden="true" size={16} />}{isCompletingSandboxPayment ? "Confirming sandbox payment" : "Complete sandbox payment"}</button> : null}{canChooseAnotherMethod ? <Link className="mt-3 inline-flex min-h-9 items-center gap-2 border border-[var(--accent)] px-3 py-1 text-sm font-semibold text-[var(--accent)] hover:bg-white" href={`/account/orders/${encodeURIComponent(order.id)}/payment`}><CreditCard aria-hidden="true" size={16} />Choose another payment method</Link> : null}</section>;
  }

  if (order.status !== "PendingPayment") return null;
  return <section className="mt-4 border border-[#d8d6c5] bg-[#fbfaf2] p-3"><p className="text-sm text-[var(--muted)]">No payment action has been requested. Choosing a method does not confirm payment.</p><Link className="mt-3 inline-flex min-h-9 items-center gap-2 bg-[var(--accent)] px-3 py-1 text-sm font-semibold text-white hover:bg-[var(--accent-strong)]" href={`/account/orders/${encodeURIComponent(order.id)}/payment`}><CreditCard aria-hidden="true" size={16} />Choose payment method</Link></section>;
}
function AddressSnapshot({ order }: { order: OrderSummary }) {
  const address = order.shippingAddress!;
  return <div className="mt-4 border border-[var(--line)] bg-[#fbfcfa] p-3 text-sm"><p className="font-medium">Delivery address snapshot</p><p className="mt-1 text-[var(--muted)]">{address.recipientName}<br />{address.line1}{address.line2 ? <><br />{address.line2}</> : null}<br />{address.city}, {address.countryCode}{address.postalCode ? ` ${address.postalCode}` : ""}</p></div>;
}

function CancellationAction({ order, isCancelling, isConfirmationOpen, onCancelOrder, onToggleConfirmation }: { order: OrderSummary; isCancelling: boolean; isConfirmationOpen: boolean; onCancelOrder: (orderId: string) => void; onToggleConfirmation: (orderId: string) => void }) {
  if (order.status !== "Pending" && order.status !== "PendingPayment") return null;

  if (isConfirmationOpen) {
    return <section className="mt-4 border border-[#f3c5c1] bg-[#fff7f6] p-3 text-sm"><p className="font-medium text-[var(--danger)]">Cancel this order?</p><p className="mt-1 text-[var(--muted)]">The order will be cancelled before fulfillment. Inventory and any promotion reservation will be released.</p><div className="mt-3 flex gap-2"><button className="inline-flex h-9 items-center gap-2 bg-[var(--danger)] px-3 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60" disabled={isCancelling} onClick={() => onCancelOrder(order.id)} type="button">{isCancelling ? <LoaderCircle aria-hidden="true" className="animate-spin" size={16} /> : <Ban aria-hidden="true" size={16} />}{isCancelling ? "Cancelling" : "Confirm cancellation"}</button><button className="h-9 border border-[var(--line)] px-3 text-sm font-semibold hover:bg-white" disabled={isCancelling} onClick={() => onToggleConfirmation(order.id)} type="button">Keep order</button></div></section>;
  }

  return <button className="mt-4 inline-flex h-9 items-center gap-2 border border-[var(--danger)] px-3 text-sm font-semibold text-[var(--danger)] hover:bg-[#fff7f6]" onClick={() => onToggleConfirmation(order.id)} type="button"><Ban aria-hidden="true" size={16} />Cancel order</button>;
}

function StatusBadge({ status }: { status: string }) {
  const isPaid = status === "Paid" || status === "Captured";
  const isFailed = status === "PaymentFailed" || status === "Cancelled" || status === "Declined" || status === "Failed";
  const colors = isPaid ? "border-[#b9d7c6] bg-[#f4fbf6] text-[var(--accent-strong)]" : isFailed ? "border-[#f3c5c1] bg-[#fff7f6] text-[var(--danger)]" : "border-[#d8d6c5] bg-[#fbfaf2] text-[#6f6317]";
  return <span className={"shrink-0 border px-2 py-1 text-xs font-medium " + colors}>{label(status)}</span>;
}

function isAwaitingProvider(status: string) { return status === "PendingAuthorization" || status === "Authorized" || status === "CaptureRequested"; }
function shortId(id: string) { return "#" + id.slice(0, 8).toUpperCase(); }
function label(status: string) { return status.replace(/([a-z])([A-Z])/g, "$1 $2"); }
function formatDate(value: string) { const parsed = new Date(value); return Number.isNaN(parsed.getTime()) ? "Time unavailable" : dateTime.format(parsed); }