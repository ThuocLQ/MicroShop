import { expect, test } from "@playwright/test";

const productId = "fa9dde50-d2cf-4565-92ff-e8e19df76603";
const productName = "Browser test desk lamp";

async function signIn(page: import("@playwright/test").Page) {
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByLabel("Username").fill("browser-e2e-user");
  await page.getByLabel("Password").fill("BrowserE2E!2026");
  await page.getByRole("dialog").getByRole("button", { name: "Sign in", exact: true }).click();
}

test("customer can sign in and sign out through the BFF session boundary", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Built around the things you use." })).toBeVisible();
  await expect(page.getByRole("heading", { name: productName, level: 1 })).toBeVisible();

  await signIn(page);
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
});

test("customer can inspect a product, add it to the cart, and enter checkout", async ({ page }) => {
  await page.goto(`/products/${productId}`);
  await expect(page.getByRole("heading", { name: productName, level: 1 })).toBeVisible();
  await expect(page.getByText("Workspace", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Add to cart" }).click();
  await expect(page.getByRole("dialog")).toBeVisible();

  await page.getByLabel("Username").fill("browser-e2e-user");
  await page.getByLabel("Password").fill("BrowserE2E!2026");
  await page.getByRole("dialog").getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Added to your cart");

  await page.goto("/");
  await expect(page.getByRole("button", { name: "Open cart and checkout, 1 items" })).toBeVisible();
  await page.getByRole("button", { name: "Open cart and checkout, 1 items" }).click();
  await expect(page.getByRole("heading", { name: "Cart & checkout" })).toBeVisible();
  await expect(page.getByRole("dialog", { name: "Cart & checkout" }).getByRole("heading", { name: productName })).toBeVisible();
  await page.getByRole("link", { name: "Open full checkout" }).click();
  await expect(page.getByRole("heading", { name: "Review your order" })).toBeVisible();
  await expect(page.getByRole("complementary").getByText(productName, { exact: false })).toBeVisible();
});
