import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, ArrowRight, Box } from "lucide-react";
import { ProductPurchaseActions } from "@/components/product-purchase-actions";
import { RecentlyViewedProducts } from "@/components/recently-viewed-products";
import { StorefrontFooter, StorefrontHeader } from "@/components/storefront-shell";
import { getCatalogProduct } from "@/lib/gateway/catalog-server";
import type { CatalogProduct } from "@/lib/gateway/catalog";
import { productImageSource } from "@/lib/storefront/product-media";

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

export default async function ProductDetailPage({ params }: { params: Promise<{ productId: string }> }) {
  const { productId } = await params;
  const result = await getCatalogProduct(productId);

  return <main className="flex min-h-screen flex-col bg-[var(--background)]">
    <StorefrontHeader />
    <section className="mx-auto max-w-7xl flex-1 px-4 py-8 sm:px-6 lg:px-8"><Link className="inline-flex items-center gap-2 text-sm font-semibold text-[var(--accent)] hover:underline" href="/products"><ArrowLeft aria-hidden="true" size={16} />Back to catalog</Link>{result.status === "not-found" ? notFound() : result.status === "unavailable" ? <Unavailable /> : <ProductDetail product={result.product} />}</section>
      <StorefrontFooter />
  </main>;
}

function ProductDetail({ product }: { product: CatalogProduct }) {
  const source = productImageSource(product.imageUrl);
  return <article className="mt-7"><div className="grid gap-9 lg:grid-cols-[minmax(0,1.15fr)_minmax(340px,0.85fr)] lg:gap-14"><div className="grid min-h-[360px] place-items-center overflow-hidden rounded-sm bg-[#edf1ee] p-8 sm:min-h-[580px] sm:p-14">{source ? <img alt={product.name} className="h-full max-h-[38rem] w-full object-contain" fetchPriority="high" referrerPolicy="no-referrer" src={source} /> : <div aria-hidden="true" className="grid place-items-center text-[var(--muted)]"><Box size={52} strokeWidth={1.25} /></div>}</div><div className="self-center py-2">{product.category ? <p className="eyebrow">{product.category}</p> : null}<h1 className="mt-4 text-4xl font-semibold tracking-tight sm:text-5xl">{product.name}</h1>{product.brand ? <p className="mt-3 text-sm font-medium text-[var(--muted)]">by {product.brand}</p> : null}<p className="mt-6 text-base leading-7 text-[var(--muted)] sm:text-lg">{product.description || "No product description is available."}</p><div className="mt-8 border-y border-[var(--line)] py-5"><p className="text-2xl font-semibold">{money.format(product.price)}</p><p className={`mt-2 text-sm font-medium ${product.stockQuantity > 0 ? "text-[var(--accent)]" : "text-[var(--danger)]"}`}>{product.stockQuantity > 0 ? `${product.stockQuantity} currently available` : "Currently out of stock"}</p></div><div className="mt-6"><ProductPurchaseActions product={product} /></div><p className="mt-6 text-xs leading-5 text-[var(--muted)]">Price and availability are current Catalog values. The server verifies both again when an order is created.</p>{product.sku ? <p className="mt-3 text-xs text-[var(--muted)]">SKU {product.sku}</p> : null}</div></div><section className="mt-14 border-t border-[var(--line)] py-10 sm:mt-18 sm:py-14"><div className="grid gap-7 md:grid-cols-[1fr_1fr]"><div><p className="eyebrow">Before you buy</p><h2 className="mt-3 text-2xl font-semibold tracking-tight">A clear view of the current product.</h2></div><div className="space-y-4 text-sm leading-6 text-[var(--muted)]"><p>Your cart stores your selected quantity. A checkout review confirms the latest catalog price, promotion and availability before an order is created.</p>{product.category ? <Link className="store-text-action" href={`/products?category=${encodeURIComponent(product.category)}`}>Browse more {product.category} products <ArrowRight aria-hidden="true" size={15} /></Link> : null}</div></div></section><RecentlyViewedProducts productId={product.id} /></article>;
}

function Unavailable() { return <div className="mt-8 border border-[#f3c5c1] bg-[#fff7f6] p-5"><h1 className="font-semibold">Product details are temporarily unavailable</h1><p className="mt-1 text-sm text-[var(--muted)]">Please retry shortly. No cached or sample product is shown in its place.</p></div>; }