import { NextResponse } from "next/server";
import { getSessionFromRequest } from "@/app/api/session/route";
import { gatewayUrl } from "@/lib/gateway/server";
import { hasSameOrigin } from "@/lib/http/same-origin";

export const dynamic = "force-dynamic";

export async function GET(request: Request) { return forward(request); }
export async function POST(request: Request) {
  if (!hasSameOrigin(request)) return NextResponse.json({ message: "Cross-site requests are not accepted." }, { status: 403 });
  return forward(request);
}

async function forward(request: Request) {
  const session = await getSessionFromRequest(request);
  if (!session) return NextResponse.json({ message: "Sign in is required." }, { status: 401 });
  const body = request.method === "GET" ? undefined : await request.text();
  try {
    const upstream = await fetch(gatewayUrl("/me/saved-items"), { method: request.method, headers: { Authorization: `Bearer ${session.accessToken}`, Accept: "application/json", ...(body ? { "Content-Type": "application/json" } : {}) }, body, cache: "no-store" });
    return new NextResponse(await upstream.text(), { status: upstream.status, headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json" } });
  } catch { return NextResponse.json({ message: "Saved items are temporarily unavailable." }, { status: 503 }); }
}