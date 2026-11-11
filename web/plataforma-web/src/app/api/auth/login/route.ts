import { NextResponse } from "next/server";
import {
  isAuthTokensResponse,
  isSameOriginMutation,
  readJson,
  readRequestJson,
  requestAuthApi,
  setAuthCookies,
  upstreamUnavailable,
} from "@/lib/auth-api";

export async function POST(request: Request): Promise<NextResponse> {
  if (!isSameOriginMutation(request)) {
    return NextResponse.json({ title: "Origem da requisição não permitida." }, { status: 403 });
  }

  const body = await readRequestJson(request);
  if (body === null) {
    return NextResponse.json({ title: "Envie um corpo JSON válido." }, { status: 400 });
  }

  const upstream = await requestAuthApi("/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  if (!upstream) {
    return upstreamUnavailable();
  }

  const payload = await readJson(upstream);
  if (!upstream.ok) {
    return NextResponse.json(payload, { status: upstream.status });
  }

  if (!isAuthTokensResponse(payload)) {
    return upstreamUnavailable();
  }

  const response = NextResponse.json({ authenticated: true });
  setAuthCookies(response, payload);
  return response;
}
