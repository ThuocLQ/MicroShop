"use client";

import Link from "next/link";
import { ArrowLeft, CircleAlert, LoaderCircle, History, PackageCheck, Plus, RefreshCw, Truck, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { OperationsWorkspace } from "@/components/operations-workspace";
import { problemMessage } from "@/lib/http/problem-details";

type Supplier = { id: string; name: string; contactEmail: string | null; active: boolean; createdAtUtc: string; updatedAtUtc: string };
type Product = { id: string; name: string; price: number };
type PurchaseOrderLine = { id: string; productId: string; productName: string; quantity: number; unitCost: number };
type PurchaseOrder = { id: string; number: string; supplierId: string; status: "DRAFT" | "SUBMITTED" | "RECEIPT_PENDING" | "RECEIVED" | "CANCELLED"; currency: string; lines: PurchaseOrderLine[]; createdAtUtc: string; receiptId: string | null; receivedAtUtc: string | null };
type Page<T> = { items: T[]; page: number; pageSize: number; totalItems: number; totalPages: number };
type DraftLine = { productId: string; quantity: number; unitCost: number };
type AuditEvent = { id: string; purchaseOrderId: string | null; receiptId: string | null; action: string; actor: string; correlationId: string | null; occurredAtUtc: string };

const initialPage: Page<never> = { items: [], page: 0, pageSize: 25, totalItems: 0, totalPages: 0 };

export function ProcurementClient() {
  const [suppliers, setSuppliers] = useState<Page<Supplier>>(initialPage);
  const [purchaseOrders, setPurchaseOrders] = useState<Page<PurchaseOrder>>(initialPage);
  const [products, setProducts] = useState<Product[]>([]);
  const [supplierPage, setSupplierPage] = useState(0);
  const [purchaseOrderPage, setPurchaseOrderPage] = useState(0);
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [showSupplierForm, setShowSupplierForm] = useState(false);
  const [showPurchaseOrderForm, setShowPurchaseOrderForm] = useState(false);
  const [confirmReceiptId, setConfirmReceiptId] = useState<string | null>(null);
  const [selectedAuditOrder, setSelectedAuditOrder] = useState<PurchaseOrder | null>(null);
  const [auditEvents, setAuditEvents] = useState<AuditEvent[]>([]);
  const [auditLoading, setAuditLoading] = useState(false);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [supplierName, setSupplierName] = useState("");
  const [supplierEmail, setSupplierEmail] = useState("");
  const [selectedSupplierId, setSelectedSupplierId] = useState("");
  const [draftLine, setDraftLine] = useState<DraftLine>({ productId: "", quantity: 1, unitCost: 0 });
  const [draftLines, setDraftLines] = useState<DraftLine[]>([]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [supplierResponse, purchaseOrderResponse, productResponse] = await Promise.all([
        fetch(`/api/suppliers?page=${supplierPage}&pageSize=25`, { cache: "no-store" }),
        fetch(`/api/procurement/purchase-orders?page=${purchaseOrderPage}&pageSize=25`, { cache: "no-store" }),
        fetch("/api/catalog/products", { cache: "no-store" }),
      ]);
      const [supplierPayload, purchaseOrderPayload, productPayload]: unknown[] = await Promise.all([
        supplierResponse.json().catch(() => null),
        purchaseOrderResponse.json().catch(() => null),
        productResponse.json().catch(() => null),
      ]);
      if (!supplierResponse.ok || !isPage<Supplier>(supplierPayload, isSupplier)) throw new Error(problemMessage(supplierPayload) ?? "Suppliers could not be loaded.");
      if (!purchaseOrderResponse.ok || !isPage<PurchaseOrder>(purchaseOrderPayload, isPurchaseOrder)) throw new Error(problemMessage(purchaseOrderPayload) ?? "Purchase orders could not be loaded.");
      if (!productResponse.ok || !isProducts(productPayload)) throw new Error(problemMessage(productPayload) ?? "Catalog products could not be loaded.");
      setSuppliers(supplierPayload);
      setPurchaseOrders(purchaseOrderPayload);
      setProducts(productPayload);
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Procurement workspace could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [purchaseOrderPage, supplierPage]);

  useEffect(() => {
    const timerId = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timerId);
  }, [load]);

  const supplierById = useMemo(() => new Map(suppliers.items.map((supplier) => [supplier.id, supplier])), [suppliers.items]);

  async function createSupplier(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const name = supplierName.trim();
    if (!name) { setError("Supplier name is required."); return; }
    setWorking("supplier"); setError(null); setNotice(null);
    try {
      await request("/api/suppliers", "POST", { name, contactEmail: supplierEmail.trim() || null });
      setSupplierName(""); setSupplierEmail(""); setShowSupplierForm(false); setNotice("Supplier was created from the server-confirmed record.");
      setSupplierPage(0);
      await load();
    } catch (exception) { setError(messageOf(exception, "Supplier could not be created.")); }
    finally { setWorking(null); }
  }

  function addDraftLine() {
    const product = products.find((item) => item.id === draftLine.productId);
    if (!product || !Number.isInteger(draftLine.quantity) || draftLine.quantity <= 0 || draftLine.unitCost < 0) {
      setError("Choose a product with a positive whole quantity and a non-negative unit cost.");
      return;
    }
    if (draftLines.some((line) => line.productId === product.id)) { setError("A purchase order can contain each product only once."); return; }
    setDraftLines((lines) => [...lines, { ...draftLine }]);
    setDraftLine({ productId: "", quantity: 1, unitCost: 0 }); setError(null);
  }

  async function createPurchaseOrder(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedSupplierId || draftLines.length === 0) { setError("Choose a supplier and add at least one purchase-order line."); return; }
    setWorking("purchase-order"); setError(null); setNotice(null);
    try {
      await request("/api/procurement/purchase-orders", "POST", {
        supplierId: selectedSupplierId,
        currency: "USD",
        lines: draftLines.map((line) => ({
          productId: line.productId,
          productName: products.find((product) => product.id === line.productId)?.name ?? "Unknown product",
          quantity: line.quantity,
          unitCost: line.unitCost,
        })),
      });
      setSelectedSupplierId(""); setDraftLines([]); setShowPurchaseOrderForm(false); setPurchaseOrderPage(0);
      setNotice("Purchase order draft was created. Submitting it does not change inventory.");
      await load();
    } catch (exception) { setError(messageOf(exception, "Purchase order could not be created.")); }
    finally { setWorking(null); }
  }

  async function transition(purchaseOrder: PurchaseOrder, action: "submit" | "receive") {
    setWorking(`${action}:${purchaseOrder.id}`); setError(null); setNotice(null);
    try {
      const result = await request<PurchaseOrder>(`/api/procurement/purchase-orders/${encodeURIComponent(purchaseOrder.id)}/${action}`, "POST");
      setConfirmReceiptId(null);
      setNotice(action === "receive"
        ? `Receipt ${result.receiptId?.slice(0, 8) ?? ""} was confirmed. Inventory was updated by the server.`
        : "Purchase order was submitted. Inventory is unchanged until receipt confirmation.");
      await load();
    } catch (exception) { setError(messageOf(exception, `Purchase order could not be ${action}ted.`)); }
    finally { setWorking(null); }
  }

  async function loadAudit(order: PurchaseOrder) {
    setSelectedAuditOrder(order); setAuditLoading(true); setAuditError(null);
    try {
      const response = await fetch(`/api/procurement/audit?purchaseOrderId=${encodeURIComponent(order.id)}&page=0&pageSize=50`, { cache: "no-store" });
      const payload: unknown = await response.json().catch(() => null);
      if (!response.ok || !isPage<AuditEvent>(payload, isAuditEvent)) throw new Error(problemMessage(payload) ?? "Procurement audit could not be loaded.");
      setAuditEvents(payload.items);
    } catch (exception) {
      setAuditEvents([]); setAuditError(messageOf(exception, "Procurement audit could not be loaded."));
    } finally { setAuditLoading(false); }
  }
  return <OperationsWorkspace area="procurement"><main className="orders-page">
    <header className="orders-header">
      <Link className="back" href="/"><ArrowLeft size={17} />Catalog control</Link>
      <div className="orders-title"><div><p className="eyebrow">Operations / procurement</p><h1>Supplier & goods receipt</h1><p className="page-summary">Create supplier-backed purchase orders, then confirm receipt only when goods are physically accepted. Stock changes only after the Inventory service confirms the receipt.</p></div><button className="icon-button" aria-label="Refresh procurement" title="Refresh procurement" disabled={loading} onClick={() => void load()}>{loading ? <LoaderCircle className="spin" size={18} /> : <RefreshCw size={18} />}</button></div>
    </header>

    <div className="metrics"><Metric label="Suppliers" value={String(suppliers.totalItems)} /><Metric label="Open purchase orders" value={String(purchaseOrders.items.filter((order) => order.status === "DRAFT" || order.status === "SUBMITTED" || order.status === "RECEIPT_PENDING").length)} tone="warn" /><Metric label="Receipts on this page" value={String(purchaseOrders.items.filter((order) => order.status === "RECEIVED").length)} /></div>
    {error ? <div className="notice" role="alert"><CircleAlert size={19} /><span>{error}</span><button onClick={() => void load()}>Retry</button></div> : null}
    {notice ? <div className="notice success" role="status"><PackageCheck size={19} /><span>{notice}</span><button aria-label="Dismiss confirmation" title="Dismiss" onClick={() => setNotice(null)}><X size={16} /></button></div> : null}

    <section className="operations-section"><div className="section-heading"><div><p className="eyebrow">Supplier records</p><h2>Approved suppliers</h2></div><button className="command primary" type="button" onClick={() => setShowSupplierForm(true)}><Plus size={16} />New supplier</button></div><div className="table-wrap"><table><thead><tr><th>Supplier</th><th>Contact</th><th>State</th><th>Updated</th></tr></thead><tbody>{suppliers.items.map((supplier) => <tr key={supplier.id}><td><strong><Truck size={16} />{supplier.name}</strong><span><code>{supplier.id}</code></span></td><td>{supplier.contactEmail ?? "No contact email"}</td><td><Status value={supplier.active ? "Active" : "Inactive"} /></td><td>{formatDate(supplier.updatedAtUtc)}</td></tr>)}</tbody></table>{!loading && suppliers.items.length === 0 ? <div className="empty">No suppliers have been created yet.</div> : null}</div><Pager page={suppliers} onPrevious={() => setSupplierPage((page) => Math.max(0, page - 1))} onNext={() => setSupplierPage((page) => page + 1)} /></section>

    <section className="operations-section"><div className="section-heading"><div><p className="eyebrow">Purchase orders</p><h2>Order, submit, receive</h2></div><button className="command primary" type="button" disabled={suppliers.items.length === 0 || products.length === 0} onClick={() => setShowPurchaseOrderForm(true)}><Plus size={16} />New purchase order</button></div><p className="muted-copy">Submitting a PO records procurement intent only. Confirming a receipt increases on-hand stock exactly once, even if an operator retries.</p><div className="table-wrap"><table><thead><tr><th>Purchase order</th><th>Supplier</th><th>Lines</th><th>Status</th><th>Receipt</th><th>Action</th><th>Audit</th></tr></thead><tbody>{purchaseOrders.items.map((order) => <tr key={order.id}><td><strong>{order.number}</strong><span>{formatDate(order.createdAtUtc)}</span></td><td>{supplierById.get(order.supplierId)?.name ?? <code>{order.supplierId}</code>}</td><td>{order.lines.length} line{order.lines.length === 1 ? "" : "s"}</td><td><Status value={order.status} /></td><td>{order.receiptId ? <><code>{order.receiptId.slice(0, 8)}</code><span>{order.receivedAtUtc ? formatDate(order.receivedAtUtc) : "Awaiting confirmation"}</span></> : "Not requested"}</td><td><Actions order={order} working={working} confirming={confirmReceiptId === order.id} onSubmit={() => void transition(order, "submit")} onAskReceive={() => setConfirmReceiptId(order.id)} onCancelReceive={() => setConfirmReceiptId(null)} onReceive={() => void transition(order, "receive")} /></td><td><button className="command" type="button" onClick={() => void loadAudit(order)}><History size={16} />View audit</button></td></tr>)}</tbody></table>{!loading && purchaseOrders.items.length === 0 ? <div className="empty">No purchase orders have been created yet.</div> : null}</div><Pager page={purchaseOrders} onPrevious={() => setPurchaseOrderPage((page) => Math.max(0, page - 1))} onNext={() => setPurchaseOrderPage((page) => page + 1)} /></section>

    {selectedAuditOrder ? <section className="operations-section" aria-labelledby="procurement-audit-heading"><div className="section-heading"><div><p className="eyebrow">Server audit</p><h2 id="procurement-audit-heading">{selectedAuditOrder.number}</h2></div><button className="icon-button" aria-label="Close procurement audit" title="Close" type="button" onClick={() => setSelectedAuditOrder(null)}><X size={18} /></button></div>{auditError ? <div className="notice" role="alert"><CircleAlert size={19} /><span>{auditError}</span><button onClick={() => void loadAudit(selectedAuditOrder)}>Retry</button></div> : null}<div className="table-wrap"><table><thead><tr><th>Action</th><th>Actor</th><th>When</th><th>Receipt</th><th>Correlation</th></tr></thead><tbody>{auditEvents.map((event) => <tr key={event.id}><td><strong>{event.action.replaceAll("-", " ")}</strong></td><td>{event.actor}</td><td>{formatDate(event.occurredAtUtc)}</td><td><code>{event.receiptId?.slice(0, 8) ?? "-"}</code></td><td><code>{event.correlationId?.slice(0, 12) ?? "-"}</code></td></tr>)}</tbody></table>{auditLoading ? <div className="empty"><LoaderCircle className="spin" size={20} />Loading audit...</div> : !auditError && auditEvents.length === 0 ? <div className="empty">No audit events were returned for this purchase order.</div> : null}</div></section> : null}
    {showSupplierForm ? <div className="dialog-backdrop" role="presentation"><form className="dialog" onSubmit={createSupplier}><div className="dialog-heading"><div><p className="eyebrow">Supplier record</p><h2>New supplier</h2></div><button className="icon-button" aria-label="Close supplier form" title="Close" type="button" onClick={() => setShowSupplierForm(false)}><X size={18} /></button></div><label>Name<input autoFocus maxLength={160} required value={supplierName} onChange={(event) => setSupplierName(event.target.value)} /></label><label>Contact email<input type="email" maxLength={320} value={supplierEmail} onChange={(event) => setSupplierEmail(event.target.value)} /></label><button className="command primary" disabled={working === "supplier"} type="submit">{working === "supplier" ? <LoaderCircle className="spin" size={16} /> : <Plus size={16} />}Create supplier</button></form></div> : null}

    {showPurchaseOrderForm ? <div className="dialog-backdrop" role="presentation"><form className="dialog wide" onSubmit={createPurchaseOrder}><div className="dialog-heading"><div><p className="eyebrow">Procurement intent</p><h2>New purchase order</h2></div><button className="icon-button" aria-label="Close purchase order form" title="Close" type="button" onClick={() => setShowPurchaseOrderForm(false)}><X size={18} /></button></div><label>Supplier<select required value={selectedSupplierId} onChange={(event) => setSelectedSupplierId(event.target.value)}><option value="">Choose supplier</option>{suppliers.items.filter((supplier) => supplier.active).map((supplier) => <option key={supplier.id} value={supplier.id}>{supplier.name}</option>)}</select></label><div className="po-line-entry"><label>Catalog product<select value={draftLine.productId} onChange={(event) => setDraftLine((line) => ({ ...line, productId: event.target.value }))}><option value="">Choose product</option>{products.map((product) => <option key={product.id} value={product.id}>{product.name}</option>)}</select></label><label>Quantity<input min="1" step="1" type="number" value={draftLine.quantity} onChange={(event) => setDraftLine((line) => ({ ...line, quantity: Number(event.target.value) }))} /></label><label>Unit cost (USD)<input min="0" step="0.01" type="number" value={draftLine.unitCost} onChange={(event) => setDraftLine((line) => ({ ...line, unitCost: Number(event.target.value) }))} /></label><button className="command" type="button" onClick={addDraftLine}><Plus size={16} />Add line</button></div><ul className="po-lines">{draftLines.map((line) => <li key={line.productId}><span>{products.find((product) => product.id === line.productId)?.name ?? line.productId}</span><span>{line.quantity} x {formatMoney(line.unitCost)}</span><button className="icon-button" aria-label="Remove purchase order line" title="Remove line" type="button" onClick={() => setDraftLines((lines) => lines.filter((item) => item.productId !== line.productId))}><X size={17} /></button></li>)}</ul><button className="command primary" disabled={working === "purchase-order" || draftLines.length === 0} type="submit">{working === "purchase-order" ? <LoaderCircle className="spin" size={16} /> : <PackageCheck size={16} />}Create draft</button></form></div> : null}
  </main></OperationsWorkspace>;
}

function Actions({ order, working, confirming, onSubmit, onAskReceive, onCancelReceive, onReceive }: { order: PurchaseOrder; working: string | null; confirming: boolean; onSubmit: () => void; onAskReceive: () => void; onCancelReceive: () => void; onReceive: () => void }) {
  if (order.status === "DRAFT") return <button className="command" disabled={working !== null} onClick={onSubmit} type="button">Submit</button>;
  if (order.status !== "SUBMITTED") return <span className="muted-copy">No action</span>;
  if (confirming) return <div className="action-row"><span className="muted-copy">Receipt will increase inventory.</span><button className="command primary" disabled={working !== null} onClick={onReceive} type="button">{working === `receive:${order.id}` ? <LoaderCircle className="spin" size={16} /> : <PackageCheck size={16} />}Confirm receipt</button><button className="command" disabled={working !== null} onClick={onCancelReceive} type="button">Cancel</button></div>;
  return <button className="command primary" disabled={working !== null} onClick={onAskReceive} type="button">Receive goods</button>;
}
function Pager({ page, onPrevious, onNext }: { page: Page<unknown>; onPrevious: () => void; onNext: () => void }) { if (page.totalPages <= 1) return null; return <div className="action-row" style={{ marginTop: 12 }}><button className="command" disabled={page.page === 0} onClick={onPrevious} type="button">Previous</button><span className="muted-copy">Page {page.page + 1} of {page.totalPages}</span><button className="command" disabled={page.page + 1 >= page.totalPages} onClick={onNext} type="button">Next</button></div>; }
function Metric({ label, value, tone }: { label: string; value: string; tone?: string }) { return <div className={`metric ${tone ?? ""}`}><span>{label}</span><strong>{value}</strong></div>; }
function Status({ value }: { value: string }) { const className = value === "RECEIVED" || value === "Active" ? "paid" : value === "DRAFT" || value === "SUBMITTED" || value === "RECEIPT_PENDING" ? "pending" : "neutral"; return <span className={`status ${className}`}>{value.replaceAll("_", " ")}</span>; }
async function request<T = unknown>(url: string, method: "POST", body?: unknown): Promise<T> { const response = await fetch(url, { method, headers: { "Content-Type": "application/json" }, body: body ? JSON.stringify(body) : undefined }); const payload: unknown = await response.json().catch(() => null); if (!response.ok) throw new Error(problemMessage(payload) ?? "The operation was rejected by the server."); return payload as T; }
function messageOf(value: unknown, fallback: string) { return value instanceof Error ? value.message : fallback; }
function formatDate(value: string) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? "Unknown" : date.toLocaleString(); }
function formatMoney(value: number) { return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value); }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null; }
function isPage<T>(value: unknown, itemCheck: (item: unknown) => item is T): value is Page<T> { return isRecord(value) && Array.isArray(value.items) && value.items.every(itemCheck) && typeof value.page === "number" && typeof value.pageSize === "number" && typeof value.totalItems === "number" && typeof value.totalPages === "number"; }
function isSupplier(value: unknown): value is Supplier { return isRecord(value) && typeof value.id === "string" && typeof value.name === "string" && (value.contactEmail === null || typeof value.contactEmail === "string") && typeof value.active === "boolean" && typeof value.createdAtUtc === "string" && typeof value.updatedAtUtc === "string"; }
function isPurchaseOrder(value: unknown): value is PurchaseOrder { return isRecord(value) && typeof value.id === "string" && typeof value.number === "string" && typeof value.supplierId === "string" && typeof value.status === "string" && typeof value.currency === "string" && Array.isArray(value.lines) && typeof value.createdAtUtc === "string"; }
function isAuditEvent(value: unknown): value is AuditEvent { return isRecord(value) && typeof value.id === "string" && (value.purchaseOrderId === null || typeof value.purchaseOrderId === "string") && (value.receiptId === null || typeof value.receiptId === "string") && typeof value.action === "string" && typeof value.actor === "string" && (value.correlationId === null || typeof value.correlationId === "string") && typeof value.occurredAtUtc === "string"; }
function isProducts(value: unknown): value is Product[] { return Array.isArray(value) && value.every((item) => isRecord(item) && typeof item.id === "string" && typeof item.name === "string" && typeof item.price === "number"); }