import { NextResponse } from "next/server";
import { getSessionFromRequest } from "@/app/api/session/route";
import { gatewayUrl } from "@/lib/gateway/server";

export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const session = await getSessionFromRequest(request);
  if (!session) {
    return NextResponse.json({ message: "Sign in is required." }, { status: 401 });
  }

  try {
    const upstream = await fetch(gatewayUrl("/payments/providers"), {
      headers: { Authorization: `Bearer ${session.accessToken}`, Accept: "application/json" },
      cache: "no-store",
    });

    return new NextResponse(await upstream.text(), {
      status: upstream.status,
      headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json" },
    });
  } catch {
    return NextResponse.json({ message: "Payment methods are unavailable. Please try again." }, { status: 503 });
  }
}
