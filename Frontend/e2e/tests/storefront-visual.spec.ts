import { expect, test } from "@playwright/test";

const longProductName = "A very long product name that must not push actions out of alignment";

test("catalog cards stay aligned, expose categories, and recover from a failed image", async ({ page }, testInfo) => {
  await page.route("https://media.test/**", (route) => route.abort());
  await page.setViewportSize({ width: 1440, height: 960 });
  await page.goto("/");

  await expect(page.getByRole("button", { name: "Audio" })).toBeVisible();
  await expect(page.getByRole("heading", { name: longProductName }).first()).toBeVisible();
  await expect(page.getByTestId("product-image-fallback").first()).toBeVisible();

  const cards = page.getByTestId("product-card");
  await expect(cards).toHaveCount(4);
  const addButtons = page.getByRole("button", { name: "Add to cart" });
  const positions = await addButtons.evaluateAll((buttons) => buttons.map((button) => button.getBoundingClientRect().y));
  expect(Math.max(...positions) - Math.min(...positions)).toBeLessThanOrEqual(1);

  await page.getByRole("button", { name: "Audio" }).click();
  await expect(cards).toHaveCount(1);
  await expect(cards.getByRole("heading", { name: "Media fallback sample" })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("catalog-desktop.png"), fullPage: true });
});

test("catalog filters remain usable without horizontal overflow on mobile", async ({ page }, testInfo) => {
  await page.route("https://media.test/**", (route) => route.abort());
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/products");

  await expect(page.getByLabel("Product category")).toBeVisible();
  await page.getByLabel("Product category").selectOption("Travel");
  await page.getByRole("button", { name: "Apply" }).click();
  await expect(page.getByText("Compact travel organizer", { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath("catalog-mobile.png"), fullPage: true });
});
test("customer dialogs keep keyboard focus and return it to the triggering action", async ({ page }) => {
  await page.goto("/");

  const signIn = page.getByRole("button", { name: "Sign in" });
  await signIn.focus();
  await signIn.press("Enter");
  const accountDialog = page.getByRole("dialog", { name: "Sign in to continue" });
  await expect(accountDialog).toBeVisible();
  await expect(page.getByLabel("Username")).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(accountDialog).toBeHidden();
  await expect(signIn).toBeFocused();

  const viewProduct = page.getByRole("button", { name: "View Browser test desk lamp" });
  await viewProduct.focus();
  await viewProduct.press("Enter");
  const productDialog = page.getByRole("dialog", { name: "Browser test desk lamp" });
  await expect(productDialog).toBeVisible();
  await expect(productDialog.getByRole("button", { name: "Close product details" })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(productDialog).toBeHidden();
  await expect(viewProduct).toBeFocused();
});
test("cart is a keyboard dialog and address setup offers a country choice", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByLabel("Username").fill("browser-e2e-user");
  await page.getByLabel("Password").fill("BrowserE2E!2026");
  await page.getByRole("dialog").getByRole("button", { name: "Sign in", exact: true }).click();

  const cartTrigger = page.getByRole("button", { name: "Open cart and checkout, 0 items" });
  await cartTrigger.focus();
  await cartTrigger.press("Enter");
  const cartDialog = page.getByRole("dialog", { name: "Cart & checkout" });
  await expect(cartDialog).toBeVisible();
  await expect(cartDialog.getByRole("button", { name: "Close cart" })).toBeFocused();
  await cartDialog.getByRole("button", { name: "Add address" }).click();
  await expect(cartDialog.getByLabel("Country")).toBeVisible();
  await expect(cartDialog.getByLabel("Country")).toHaveValue("");
  await expect(cartDialog.getByLabel("Country").locator("option")).toHaveCount(8);
  await page.keyboard.press("Escape");
  await expect(cartDialog).toBeHidden();
  await expect(cartTrigger).toBeFocused();
});
test("shared store header exposes discovery, cart, and sign-in without rendering a browser token", async ({ page }) => {
  await page.goto("/products");
  await expect(page.getByRole("navigation", { name: "Store navigation" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Search products" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Cart and checkout" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
});

test("recently viewed products are resolved from the live Catalog contract", async ({ page }) => {
  await page.goto("/products/fa9dde50-d2cf-4565-92ff-e8e19df76603");
  await expect(page.getByRole("heading", { name: "Browser test desk lamp", level: 1 })).toBeVisible();
  await page.goto("/products/c2986cc6-9f51-4cef-a5ee-8303cc7bf48c");
  await expect(page.getByRole("heading", { name: "Recently viewed" })).toBeVisible();
  await expect(page.getByRole("link", { name: /Browser test desk lamp/ })).toBeVisible();
});