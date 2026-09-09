import { chromium } from "@playwright/test";

const baseUrl = (process.env.STOREFRONT_VISUAL_SMOKE_BASE_URL ?? "http://localhost:5027").replace(/\/$/, "");
const screenshotDirectory = process.env.STOREFRONT_VISUAL_SMOKE_SCREENSHOT_DIR;

const browser = await chromium.launch({ headless: true });
try {
  const desktop = await browser.newPage({ viewport: { width: 1440, height: 960 } });
  await desktop.goto(`${baseUrl}/`, { waitUntil: "domcontentloaded", timeout: 30_000 });
  await desktop.waitForFunction(() => document.querySelectorAll("[data-testid=product-card]").length > 0, undefined, { timeout: 15_000 });
  const cardLocator = desktop.locator("[data-testid=product-card]");
  const cardCount = await cardLocator.count();
  for (let index = 0; index < cardCount; index++) {
    await cardLocator.nth(index).scrollIntoViewIfNeeded();
    await desktop.waitForTimeout(120);
  }
  await desktop.waitForFunction(() => [...document.querySelectorAll("[data-testid=product-card] img")].every((image) => image.complete), undefined, { timeout: 15_000 }).catch(() => undefined);
  await desktop.evaluate(() => window.scrollTo(0, 0));
  const desktopCheck = await desktop.evaluate(() => {
    const cards = [...document.querySelectorAll("[data-testid=product-card]")];
    const rows = new Map();
    for (const card of cards) {
      const rowTop = Math.round(card.getBoundingClientRect().top);
      const actionTop = Math.round(card.querySelector(".store-add-button")?.getBoundingClientRect().top ?? -1);
      rows.set(rowTop, [...(rows.get(rowTop) ?? []), actionTop]);
    }

    const imageMetrics = [...document.querySelectorAll("[data-testid=product-card] img")].map((image) => ({
      complete: image.complete,
      naturalHeight: image.naturalHeight,
      naturalWidth: image.naturalWidth,
    }));

    const heroHeading = document.querySelector("[data-testid=catalog-hero] h1");
    const heroMedia = document.querySelector("[data-testid=catalog-hero-media]");
    const heroCopyOverlapsMedia = heroHeading instanceof HTMLElement && heroMedia instanceof HTMLElement
      ? heroHeading.getBoundingClientRect().bottom > heroMedia.getBoundingClientRect().top
      : true;

    return {
      heroCopyOverlapsMedia,
      cards: cards.length,
      categories: document.querySelectorAll(".store-category-chip").length,
      fallbackCount: document.querySelectorAll("[data-testid=product-image-fallback]").length,
      horizontalOverflow: document.documentElement.scrollWidth > window.innerWidth,
      unloadedImages: imageMetrics.filter((image) => !image.complete || image.naturalWidth === 0 || image.naturalHeight === 0).length,
      rowActionDeltas: [...rows.values()].filter((values) => values.length > 1).map((values) => Math.max(...values) - Math.min(...values)),
    };
  });
  if (screenshotDirectory) await desktop.screenshot({ path: `${screenshotDirectory}/storefront-live-desktop.png`, fullPage: true });

  const mobile = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await mobile.goto(`${baseUrl}/products`, { waitUntil: "domcontentloaded", timeout: 30_000 });
  await mobile.waitForTimeout(1_000);
  const mobileCheck = await mobile.evaluate(() => ({
    categorySelectPresent: Boolean(document.querySelector("#catalog-category")),
    horizontalOverflow: document.documentElement.scrollWidth > window.innerWidth,
  }));
  if (screenshotDirectory) await mobile.screenshot({ path: `${screenshotDirectory}/storefront-live-mobile.png`, fullPage: true });

  const result = { baseUrl, desktopCheck, mobileCheck };
  console.log(JSON.stringify(result, null, 2));
  const actionMisaligned = desktopCheck.rowActionDeltas.some((delta) => delta > 1);
  if (desktopCheck.cards === 0 || desktopCheck.categories === 0 || desktopCheck.heroCopyOverlapsMedia || desktopCheck.fallbackCount > 0 || desktopCheck.horizontalOverflow || desktopCheck.unloadedImages > 0 || actionMisaligned || !mobileCheck.categorySelectPresent || mobileCheck.horizontalOverflow) process.exitCode = 1;
} finally {
  await browser.close();
}
