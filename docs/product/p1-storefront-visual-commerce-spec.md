# P1 Storefront Visual Commerce Specification

- Feature ID: P1-STOREFRONT-EXPERIENCE-2026-09
- Owner: Product Owner, Commerce Tech Lead, Design Lead
- Status: Reopened; discovery/conversion surfaces remain partial. P1F realtime foundation implemented (2026-09-09).
- Depends on: P0 Customer Commerce Experience Specification and Catalog API discovery contract

## 1. Outcome

MicroShop Storefront must feel like a modern product-led commerce experience while keeping
the current trusted purchase journey: browse real products, inspect a product, add it to a
customer-owned cart, review an order, pay through the supported provider, and follow the
confirmed lifecycle.

This specification borrows interaction principles from high-quality product storefronts:
one clear message per visual band, direct product imagery, restrained navigation, deliberate
space, and two clear choices. It must not copy any other brand's visual identity, copy, or
assets.

## 2. Source Of Truth

| Experience data | Owner | Storefront rule |
| --- | --- | --- |
| Name, description, price, stock, SKU, brand, category, image URL | CatalogService | Render only values returned by Catalog through the BFF/Gateway. |
| Cart count, items and subtotal | BasketService | Never infer or persist a browser-only cart. |
| Quote, discount, final total and idempotency result | OrderingService | Show only server-confirmed review/order results. |
| Payment provider session and state | PaymentService | A redirect or return URL is not payment confirmation. |
| Address and customer profile | IdentityService | Show only the authenticated owner's data. |

No review score, percentage discount, delivery promise, comparison score, urgency counter,
or recommendation may be rendered until an owned backend contract exists.

## 3. Information Architecture

### Customer routes

| Route | Purpose | Primary action |
| --- | --- | --- |
| `/` | Curated, data-backed discovery home | Explore collection or inspect featured product |
| `/products` | Searchable, filterable catalog | Open a product detail |
| `/products/{id}` | Product decision page | Add confirmed item to cart |
| `/checkout` | Focused checkout journey | Review then create idempotent order |
| `/account` | Orders, addresses, lifecycle preferences | Open an owned order |
| `/account/orders/{id}` | Order, payment and shipment timeline | Retry/pay/cancel only when server permits |

The cart remains an efficient drawer for quick quantity changes. It must always provide an
explicit link to the dedicated checkout route; it is not the sole checkout experience.

## 4. Home And Catalog Design

1. A compact sticky header exposes product discovery, search, account and cart. It has no
   marketing navigation that lacks a real destination.
2. An announcement strip may state only verified system facts: current catalog pricing and
   availability are revalidated during checkout.
3. The hero uses one in-stock Catalog product as its image and source of name, category,
   brand, price and detail link. It has one primary product CTA and one discovery CTA.
4. Category navigation is derived from non-empty Catalog `category` values. Selecting a
   category filters current catalog results and remains visibly selected.
5. Product cards have stable media ratio, product name, category/brand where available,
   current price, availability advisory, detail action and add action. Cards do not invent
   product facts.
6. The discovery page keeps keyword, category, sort and cursor in the URL so a filtered
   catalog can be reloaded and shared.

## 5. Product Detail Design

- Media is the first decision surface. Use the product's `imageUrl`; show a neutral honest
  fallback when unavailable.
- Keep price and availability adjacent to the add-to-cart action.
- Show SKU/brand/category only when supplied by Catalog.
- Explain that checkout performs the final price and stock verification.
- Related products are deferred until Catalog owns a related-product contract. Category
  cards may link back to `/products?category=...` but must not claim personalization.

## 6. Visual System

- Product-led neutral palette: near-white surfaces, ink foreground, quiet warm-gray lines,
  and one evergreen action color. Reserve red for failure/destructive actions.
- Display type is large only for hero/product titles. Compact operational surfaces use
  regular text and stable controls.
- Use direct product media with `object-fit: contain` inside a light neutral stage; never
  blur or darken product imagery to create atmosphere.
- Sections are full-width bands with a constrained inner grid. A card is only for an
  individual product, dialog, cart or framed tool; no card-inside-card composition.
- Border radius is small (8px or less). No gradient/blob/bokeh decoration.

## 7. Interaction And Accessibility

- Search has a visible label or accessible name and preserves keyboard focus.
- Cart, authentication and product dialogs trap focus, close with Escape, and restore focus
  to the trigger.
- Add, review, create-order and payment actions show working states and prevent duplicate
  submission. Success follows server confirmation only.
- Empty, unavailable, unauthorized, out-of-stock, price-change and stale-quote states each
  explain the recovery action.
- Test desktop, tablet and narrow mobile. Text, sticky actions and quantity controls must not
  overflow or overlap.

## 8. Performance And Media

- The first visible hero image is eagerly loaded only when it is a real current Catalog item.
  Card images are lazy-loaded with fixed aspect ratios to prevent layout shift.
- Catalog uses the existing cursor discovery API; the home does not load an unbounded client
  catalog merely to decorate the page.
- Images remain remote Catalog media. A future production pass may introduce owned CDN media
  transformations and editorial collection content through a Catalog-owned contract.

## 9. Acceptance Evidence

- Storefront build and lint pass.
- Browser E2E covers unauthenticated discovery, sign-in, product detail, cart and checkout
  entry; existing business smoke continues to cover idempotency, payment, cancellation and
  fulfillment through the BFF/Gateway.
- Screenshots are reviewed at desktop and mobile for hierarchy, media loading, keyboard focus,
  overflow, empty/error states and real-data integrity.
- No frontend file contains hard-coded product inventory, pricing, reviews, promotions or
  delivery claims.

## 10. Implementation Evidence

- `pnpm --dir Frontend/apps/storefront build` passes with production route generation.
- `pnpm --dir Frontend/e2e test` covers unauthenticated discovery, BFF session sign-in/sign-out,
  product detail, authenticated add-to-cart, cart drawer, and dedicated checkout entry.
- `scripts/portfolio-catalog-discovery-real-data-smoke.ps1` verifies persisted Catalog metadata,
  category filtering, cursor paging, and Gateway/Storefront BFF agreement against the portfolio runtime.
- Desktop and narrow viewport screenshots were captured against the public Storefront with real Catalog data.
- scripts/test-portfolio-catalog-media.ps1 verifies seed completeness, category diversity and reachable product media before deployment.
- docs/qa/storefront-browser-qa-test-cases.md records repeatable browser cases and screenshot evidence.

## 11. P1F - Realtime Customer Experience

Realtime is a **best-effort customer experience signal**, not the source of truth for any order, payment, inventory, or fulfillment decision.

- Browser connects only to same-origin `/realtime/customer-events` through Caddy and ApiGateway. It never connects to a worker, broker, database, or internal endpoint directly.
- `NotificationWorker` hosts the SignalR delivery module and receives existing RabbitMQ order events after their durable notification handling succeeds. ApiGateway only authenticates and proxies the route; it does not implement commerce rules.
- The hub requires a valid authenticated customer session. It derives the customer group only from validated JWT claims. Group membership is delivery routing, not authorization; order/payment REST endpoints continue enforcing ownership.
- A signal contains only `eventId`, `eventType`, `resourceId`, `status`, `occurredAtUtc`, and `correlationId`. It contains no address, contact, provider secret, webhook payload, or payment instrument data.
- Storefront deduplicates `eventId`, then re-fetches the owned REST resource before changing visible state. On disconnect/reconnect/failure, the page remains usable and REST refresh is the recovery path.
- Initial supported signals are `order.created` and `order.status-changed`; Order Detail and Account refresh confirmed data. Cart quotes, provider payment state, shipment-specific payloads, operations queues, and marketing notifications require their own owned event/REST contracts before being added.
- Single-node portfolio runs in-memory SignalR. Multi-replica deployment requires a same-datacenter Redis backplane or a managed SignalR service, plus connection/load testing; do not claim cross-replica delivery before that is configured.

**Acceptance evidence:** anonymous negotiation returns `401`; a signed-in same-origin BFF session negotiates and opens a SignalR connection through Caddy -> Gateway -> NotificationWorker; duplicate signals do not trigger repeated refresh; reconnect preserves the REST recovery path; customer A cannot obtain customer B data through either Hub or REST.