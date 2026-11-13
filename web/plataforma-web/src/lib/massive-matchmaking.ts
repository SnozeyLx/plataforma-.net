import { NextResponse } from "next/server";
import {
  clearAuthCookies,
  readJson,
  setAuthCookies,
  upstreamUnavailable,
  isSameOriginMutation,
} from "@/lib/auth-api";
import { resolveGameSession } from "@/lib/game-session";

function preventCaching(response: NextResponse): NextResponse {
  response.headers.set("Cache-Control", "no-store");
  return response;
}

export async function proxyMassiveMatchmaking(
  request: Request,
  method: "POST" | "GET" | "DELETE",
  ticketId?: string,
): Promise<NextResponse> {
  if (method !== "GET" && !isSameOriginMutation(request)) {
    return preventCaching(
      NextResponse.json(
        { title: "Origem da requisição não permitida." },
        { status: 403 },
      ),
    );
  }

  const session = await resolveGameSession();
  if (session.kind === "unavailable") {
    return preventCaching(upstreamUnavailable());
  }
  if (session.kind === "upstream-error") {
    return preventCaching(
      NextResponse.json(session.body, { status: session.status }),
    );
  }
  if (session.kind === "unauthenticated") {
    const response = preventCaching(
      NextResponse.json(
        { title: "Entre na plataforma para jogar online." },
        { status: 401 },
      ),
    );
    clearAuthCookies(response);
    return response;
  }

  const baseUrl = (
    process.env.MASSIVE_API_BASE_URL ?? "http://localhost:5001"
  ).replace(/\/+$/, "");
  const path = ticketId
    ? `/api/matchmaking/tickets/${encodeURIComponent(ticketId)}`
    : "/api/matchmaking/tickets";

  let upstream: Response | null;
  try {
    upstream = await fetch(`${baseUrl}${path}`, {
      method,
      headers: { Authorization: `Bearer ${session.accessToken}` },
      cache: "no-store",
    });
  } catch {
    upstream = null;
  }

  if (!upstream) {
    return preventCaching(upstreamUnavailable());
  }

  const response =
    upstream.status === 204
      ? new NextResponse(null, { status: 204 })
      : NextResponse.json(await readJson(upstream), { status: upstream.status });
  preventCaching(response);
  if (session.refreshedTokens) {
    setAuthCookies(response, session.refreshedTokens);
  }
  return response;
}
