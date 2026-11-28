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

export type MatchmakingServerMode = "massive" | "coop";

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function getMatchmakingBaseUrl(mode: MatchmakingServerMode): string {
  const configured =
    mode === "coop"
      ? (process.env.COOP_API_BASE_URL ?? "http://localhost:5002")
      : (process.env.MASSIVE_API_BASE_URL ?? "http://localhost:5001");
  return configured.replace(/\/+$/, "");
}

export async function handleMatchmakingTicketItem(
  request: Request,
  params: Promise<{ ticketId: string }>,
  method: "GET" | "DELETE",
  mode: MatchmakingServerMode = "massive",
): Promise<NextResponse> {
  const { ticketId } = await params;
  if (!UUID_PATTERN.test(ticketId)) {
    return NextResponse.json({ title: "Ticket inválido." }, { status: 400 });
  }

  return proxyMatchmaking(request, method, mode, ticketId);
}

export async function proxyMassiveMatchmaking(
  request: Request,
  method: "POST" | "GET" | "DELETE",
  ticketId?: string,
): Promise<NextResponse> {
  return proxyMatchmaking(request, method, "massive", ticketId);
}

export async function proxyMatchmaking(
  request: Request,
  method: "POST" | "GET" | "DELETE",
  mode: MatchmakingServerMode,
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

  const baseUrl = getMatchmakingBaseUrl(mode);
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
