import Link from "next/link";
import { ArrowLeft, ArrowRight, Box, Search } from "lucide-react";
import { ProductImage } from "@/components/product-image";
import { StorefrontFooter, StorefrontHeader } from "@/components/storefront-shell";
import { getCatalogDiscovery, type CatalogSort } from "@/lib/gateway/catalog-server";

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const validSorts = new Set<CatalogSort>(["name_asc", "name_desc", "price_asc", "price_desc"]);
type SearchParams = Promise<{ keyword?: string; category?: string; sort?: string; cursor?: string }>;

export default async function ProductsPage({ searchParams }: { searchParams: SearchParams }) {
  const params = await searchParams;
  const keyword = params.keyword?.trim() ?? "";
  const category = params.category?.trim() ?? "";
  const sort = validSorts.has(params.sort as CatalogSort) ? params.sort as CatalogSort : "name_asc";
  const [discovery, categorySource] = await Promise.all([
    getCatalogDiscovery({ keyword, category, sort, cursor: params.cursor, pageSize: 12 }),
    getCatalogDiscovery({ sort: "name_asc", pageSize: 48 }),
  ]);
  const categories = Array.from(new Set((categorySource?.items ?? []).map((product) => product.category?.trim()).filter((value): value is string => Boolean(value)))).sort();

  return (
    <main className="flex min-h-screen flex-col bg-[var(--background)]">
      <StorefrontHeader />

      <section className="mx-auto max-w-7xl flex-1 px-4 py-8 sm:px-6 lg:px-8">
        <Link className="inline-flex items-center gap-2 text-sm font-semibold text-[var(--accent)] hover:underline" href="/"><ArrowLeft aria-hidden="true" size={16} />Home</Link>
        <div className="mt-7 border-b border-[var(--line)] pb-7"><p className="eyebrow">Catalog</p><h1 className="mt-3 text-4xl font-semibold tracking-tight sm:text-5xl">Explore the current collection.</h1><p className="mt-4 max-w-2xl text-base leading-7 text-[var(--muted)]">Prices, descriptions and availability are supplied by the live Catalog. Checkout reviews them again before an order is created.</p></div>
        <form className="mt-8 grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_190px_auto]" method="get">
          <label className="store-search max-w-none"><span className="sr-only">Search products</span><Search aria-hidden="true" size={18} /><input defaultValue={keyword} name="keyword" placeholder="Search name or description" type="search" /></label>
          <label className="sr-only" htmlFor="catalog-category">Product category</label>
          <select className="store-filter-select" defaultValue={category} id="catalog-category" name="category"><option value="">All categories</option>{categories.map((item) => <option key={item} value={item}>{item}</option>)}</select>
          <label className="sr-only" htmlFor="catalog-sort">Sort products</label>
          <select className="store-filter-select" defaultValue={sort} id="catalog-sort" name="sort"><option value="name_asc">Name: A to Z</option><option value="name_desc">Name: Z to A</option><option value="price_asc">Price: low to high</option><option value="price_desc">Price: high to low</option></select>
          <button className="store-primary-button px-5" type="submit">Apply</button>
        </form>
        {!discovery ? <Unavailable /> : <CatalogResults category={category} discovery={discovery} keyword={keyword} sort={sort} />}
      </section>
      <StorefrontFooter />
    </main>
  );
}

function CatalogResults({ discovery, keyword, category, sort }: { discovery: NonNullable<Awaited<ReturnType<typeof getCatalogDiscovery>>>; keyword: string; category: string; sort: CatalogSort }) {
  if (discovery.items.length === 0) return <div className="mt-10 border border-dashed border-[var(--line)] bg-white px-6 py-16 text-center"><Box aria-hidden="true" className="mx-auto text-[var(--muted)]" size={28} /><h2 className="mt-4 text-xl font-semibold">No matching products</h2><p className="mt-2 text-sm text-[var(--muted)]">Change your search or category and try again.</p></div>;
  const next = discovery.nextCursor ? new URLSearchParams({ ...(keyword ? { keyword } : {}), ...(category ? { category } : {}), sort, cursor: discovery.nextCursor }).toString() : null;
  return <><p className="mt-7 text-sm text-[var(--muted)]" role="status">Showing {discovery.items.length} live Catalog products.</p><div className="mt-6 grid grid-cols-1 gap-x-4 gap-y-8 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">{discovery.items.map((product) => <article className="store-product-card" data-testid="product-card" key={product.id}><Link aria-label={`View ${product.name}`} className="store-product-media" href={`/products/${encodeURIComponent(product.id)}`}><ProductImage alt={product.name} className="h-full w-full object-cover" fallbackClassName="grid h-full w-full place-items-center text-[var(--muted)]" imageUrl={product.imageUrl} /></Link><div className="mt-5 flex items-start justify-between gap-3"><div><p className="text-xs font-semibold uppercase tracking-wide text-[var(--accent)]">{product.category ?? product.brand ?? "Catalog"}</p><h2 className="mt-2 text-lg font-semibold tracking-tight"><Link className="hover:underline" href={`/products/${encodeURIComponent(product.id)}`}>{product.name}</Link></h2></div><Stock quantity={product.stockQuantity} /></div><p className="mt-2 line-clamp-2 text-sm leading-6 text-[var(--muted)]">{product.description || "No product description is available."}</p><div className="mt-5 flex items-center justify-between gap-3"><p className="text-lg font-semibold">{money.format(product.price)}</p><Link className="store-text-action" href={`/products/${encodeURIComponent(product.id)}`}>Details <ArrowRight aria-hidden="true" size={15} /></Link></div><Link aria-label={`View ${product.name}`} className="store-secondary-button mt-4 w-full" href={`/products/${encodeURIComponent(product.id)}`}>View product</Link></article>)}</div>{next ? <div className="mt-10 flex justify-center"><Link className="store-primary-button px-5" href={`/products?${next}`}>Next products <ArrowRight aria-hidden="true" size={16} /></Link></div> : null}</>;
}

function Stock({ quantity }: { quantity: number }) { return quantity <= 0 ? <span className="shrink-0 text-xs font-semibold text-[var(--danger)]">Out of stock</span> : <span className="shrink-0 text-xs font-semibold text-[var(--accent)]">{quantity} in stock</span>; }
function Unavailable() { return <div className="mt-10 border border-[#f3c5c1] bg-[#fff7f6] p-5"><h2 className="font-semibold">Catalog is temporarily unavailable</h2><p className="mt-1 text-sm text-[var(--muted)]">Please retry shortly. No cached or sample products are shown in its place.</p></div>; }