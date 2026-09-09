import { NextResponse } from "next/server";
import { getSessionFromRequest } from "@/app/api/session/route";
import { gatewayUrl } from "@/lib/gateway/server";
import { hasSameOrigin } from "@/lib/http/same-origin";

export const dynamic = "force-dynamic";

export async function DELETE(request: Request, context: { params: Promise<{ productId: string }> }) {
  if (!hasSameOrigin(request)) return NextResponse.json({ message: "Cross-site requests are not accepted." }, { status: 403 });
  const session = await getSessionFromRequest(request);
  if (!session) return NextResponse.json({ message: "Sign in is required." }, { status: 401 });
  const { productId } = await context.params;
  try {
    const upstream = await fetch(gatewayUrl(`/me/saved-items/${encodeURIComponent(productId)}`), { method: "DELETE", headers: { Authorization: `Bearer ${session.accessToken}`, Accept: "application/json" }, cache: "no-store" });
    return new NextResponse(await upstream.text(), { status: upstream.status, headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json" } });
  } catch { return NextResponse.json({ message: "Saved items are temporarily unavailable." }, { status: 503 }); }
}