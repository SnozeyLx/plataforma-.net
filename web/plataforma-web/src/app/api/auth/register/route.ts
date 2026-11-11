import { NextResponse } from "next/server";
import {
  isSameOriginMutation,
  readJson,
  readRequestJson,
  requestAuthApi,
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

  const upstream = await requestAuthApi("/register", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  if (!upstream) {
    return upstreamUnavailable();
  }

  return NextResponse.json(await readJson(upstream), { status: upstream.status });
}
