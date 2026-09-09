"use client";

import Link from "next/link";
import { Clock3 } from "lucide-react";
import { useEffect, useState } from "react";
import { ProductImage } from "@/components/product-image";
import type { CatalogProduct } from "@/lib/gateway/catalog";

const storageKey = "microshop_recent_product_ids";
const limit = 6;

export function RecentlyViewedProducts({ productId }: { productId: string }) {
  const [products, setProducts] = useState<CatalogProduct[]>([]);

  useEffect(() => {
    const ids = readRecentIds();
    const nextIds = [productId, ...ids.filter((id) => id !== productId)].slice(0, limit);
    window.localStorage.setItem(storageKey, JSON.stringify(nextIds));
    const previousIds = nextIds.filter((id) => id !== productId);
    if (previousIds.length === 0) return;

    let active = true;
    Promise.all(previousIds.map(async (id) => {
      const response = await fetch(`/api/catalog/products/${encodeURIComponent(id)}`, { headers: { Accept: "application/json" } });
      const payload: unknown = await response.json().catch(() => null);
      return response.ok && isCatalogProduct(payload) ? payload : null;
    })).then((result) => {
      if (active) setProducts(result.filter((item): item is CatalogProduct => item !== null));
    }).catch(() => {
      if (active) setProducts([]);
    });

    return () => { active = false; };
  }, [productId]);

  if (products.length === 0) return null;

  return <section aria-labelledby="recently-viewed-heading" className="border-t border-[var(--line)] py-10 sm:py-14">
    <div className="flex items-end justify-between gap-4"><div><p className="eyebrow"><Clock3 aria-hidden="true" size={14} /> Your browsing</p><h2 className="mt-2 text-2xl font-semibold tracking-tight" id="recently-viewed-heading">Recently viewed</h2></div><Link className="store-text-action" href="/products">Shop all</Link></div>
    <div className="mt-6 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">{products.map((product) => <Link className="group min-w-0" href={`/products/${encodeURIComponent(product.id)}`} key={product.id}><div className="aspect-[4/5] overflow-hidden bg-[#edf1ee] p-3"><ProductImage alt={product.name} className="h-full w-full object-contain transition duration-300 group-hover:scale-[1.025]" fallbackClassName="grid h-full w-full place-items-center text-sm text-[var(--muted)]" imageUrl={product.imageUrl} /></div><p className="mt-3 line-clamp-2 text-sm font-semibold group-hover:underline">{product.name}</p><p className="mt-1 text-sm text-[var(--muted)]">{formatMoney(product.price)}</p></Link>)}</div>
  </section>;
}

function readRecentIds(): string[] {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(storageKey) ?? "[]");
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === "string" && id.length > 0 && id.length <= 128).slice(0, limit) : [];
  } catch { return []; }
}

function isCatalogProduct(value: unknown): value is CatalogProduct {
  if (typeof value !== "object" || value === null) return false;
  const product = value as Record<string, unknown>;
  return typeof product.id === "string" && typeof product.name === "string" && typeof product.description === "string" && typeof product.price === "number" && typeof product.stockQuantity === "number";
}

function formatMoney(value: number) { return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value); }
