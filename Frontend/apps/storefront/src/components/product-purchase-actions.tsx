"use client";

import { Heart, LoaderCircle, ShoppingBag } from "lucide-react";
import { useState } from "react";
import { AuthDialog } from "@/components/auth-dialog";
import { problemMessage } from "@/lib/http/problem-details";
import type { CatalogProduct } from "@/lib/gateway/catalog";
import type { CurrentUser } from "@/lib/storefront/types";

type Feedback = { tone: "error" | "success"; text: string };

export function ProductPurchaseActions({ product }: { product: CatalogProduct }) {
  const [isWorking, setIsWorking] = useState(false);
  const [isAuthOpen, setIsAuthOpen] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  async function addToCart(userId?: string) {
    if (product.stockQuantity <= 0) return;

    setIsWorking(true);
    setFeedback(null);
    try {
      let activeUserId: string | null | undefined = userId;
      if (!activeUserId) {
        const sessionResponse = await fetch("/api/session", { headers: { Accept: "application/json" } });
        const sessionPayload: unknown = await sessionResponse.json().catch(() => null);
        activeUserId = sessionUserId(sessionPayload);
      }

      if (!activeUserId) {
        setIsAuthOpen(true);
        return;
      }

      const response = await fetch(`/api/cart/${encodeURIComponent(activeUserId)}/items`, {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({ productId: product.id, quantity: 1 }),
      });
      const payload: unknown = await response.json().catch(() => null);

      if (response.status === 401) {
        setIsAuthOpen(true);
        return;
      }
      if (!response.ok) throw new Error(messageOf(payload) ?? "This product could not be added to your cart.");

      setFeedback({ tone: "success", text: "Added to your cart. Review quantities and checkout from the cart." });
    } catch (error) {
      setFeedback({ tone: "error", text: error instanceof Error ? error.message : "This product could not be added to your cart." });
    } finally {
      setIsWorking(false);
    }
  }

  async function saveItem() {
    setIsSaving(true);
    setFeedback(null);
    try {
      const sessionResponse = await fetch("/api/session", { headers: { Accept: "application/json" } });
      const sessionPayload: unknown = await sessionResponse.json().catch(() => null);
      if (!sessionUserId(sessionPayload)) { setIsAuthOpen(true); return; }
      const response = await fetch("/api/saved-items", { method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify({ productId: product.id }) });
      const payload: unknown = await response.json().catch(() => null);
      if (response.status === 401) { setIsAuthOpen(true); return; }
      if (!response.ok && response.status !== 204) throw new Error(messageOf(payload) ?? "This product could not be saved.");
      setFeedback({ tone: "success", text: "Saved to your account. You can manage it from Account." });
    } catch (error) {
      setFeedback({ tone: "error", text: error instanceof Error ? error.message : "This product could not be saved." });
    } finally { setIsSaving(false); }
  }
  function handleSignedIn(user: CurrentUser) {
    setIsAuthOpen(false);
    void addToCart(user.userId);
  }

  if (product.stockQuantity <= 0) {
    return <p className="border border-[#f3c5c1] bg-[#fff7f6] px-4 py-3 text-sm text-[var(--danger)]">This product is currently out of stock.</p>;
  }

  return <div className="space-y-3">
    <div className="flex flex-wrap gap-3"><button className="store-primary-button disabled:cursor-not-allowed disabled:bg-[#8ba89b]" disabled={isWorking} onClick={() => void addToCart()} type="button">
      {isWorking ? <LoaderCircle aria-hidden="true" className="animate-spin" size={17} /> : <ShoppingBag aria-hidden="true" size={17} />}
      {isWorking ? "Adding to cart" : "Add to cart"}
    </button>
    <button className="store-secondary-button" disabled={isSaving} onClick={() => void saveItem()} type="button">{isSaving ? <LoaderCircle aria-hidden="true" className="animate-spin" size={17} /> : <Heart aria-hidden="true" size={17} />}{isSaving ? "Saving" : "Save"}</button>
    </div>
    {feedback ? <p className={feedback.tone === "success" ? "border-l-2 border-[var(--accent)] bg-[#f4fbf6] px-3 py-2 text-sm text-[var(--accent-strong)]" : "border-l-2 border-[var(--danger)] bg-[#fff7f6] px-3 py-2 text-sm text-[var(--danger)]"} role={feedback.tone === "success" ? "status" : "alert"}>{feedback.text}</p> : null}
    <AuthDialog notice="Sign in to add products to your cart." onClose={() => setIsAuthOpen(false)} onSignedIn={handleSignedIn} open={isAuthOpen} />
  </div>;
}

function sessionUserId(value: unknown): string | null {
  if (typeof value !== "object" || value === null) return null;
  const user = (value as { user?: unknown }).user;
  if (typeof user !== "object" || user === null) return null;
  const userId = (user as Record<string, unknown>).userId;
  return typeof userId === "string" ? userId : null;
}

const messageOf = problemMessage;
