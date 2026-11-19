import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import {
  accessTokenCookie,
  clearAuthCookies,
  isAuthTokensResponse,
  isSameOriginMutation,
  readJson,
  refreshTokenCookie,
  requestAuthApi,
  requestPlatformApi,
  setAuthCookies,
  upstreamUnavailable,
} from "@/lib/auth-api";

interface RouteContext {
  params: Promise<{ path: string[] }>;
}

type PlatformMethod = "GET" | "POST" | "PUT" | "DELETE";

export async function GET(request: Request, context: RouteContext) {
  return proxyPlatformRequest(request, context, "GET");
}

export async function POST(request: Request, context: RouteContext) {
  return proxyPlatformRequest(request, context, "POST");
}

export async function PUT(request: Request, context: RouteContext) {
  return proxyPlatformRequest(request, context, "PUT");
}

export async function DELETE(request: Request, context: RouteContext) {
  return proxyPlatformRequest(request, context, "DELETE");
}

async function proxyPlatformRequest(
  request: Request,
  context: RouteContext,
  method: PlatformMethod,
): Promise<NextResponse> {
  if (method !== "GET" && !isSameOriginMutation(request)) {
    return NextResponse.json({ title: "Origem da requisição não permitida." }, { status: 403 });
  }

  const { path } = await context.params;
  const apiPath = `/${path.map(encodeURIComponent).join("/")}${new URL(request.url).search}`;
  if (method === "GET" && path.join("/") === "launcher/windows/access") {
    const upstream = await requestPlatformApi(apiPath, { method });
    return upstream ? forwardResponse(upstream) : upstreamUnavailable();
  }

  const requestBody = method === "GET" ? undefined : await request.arrayBuffer();
  const cookieStore = await cookies();
  const accessToken = cookieStore.get(accessTokenCookie)?.value;
  const refreshToken = cookieStore.get(refreshTokenCookie)?.value;

  if (!accessToken && !refreshToken) {
    return NextResponse.json({ title: "Autenticação necessária." }, { status: 401 });
  }

  let activeAccessToken = accessToken;
  let refreshedTokens: Awaited<ReturnType<typeof refreshSession>> = null;
  if (!activeAccessToken && refreshToken) {
    refreshedTokens = await refreshSession(refreshToken);
    if (!refreshedTokens) {
      const response = NextResponse.json({ title: "Sessão expirada." }, { status: 401 });
      clearAuthCookies(response);
      return response;
    }

    activeAccessToken = refreshedTokens.accessToken;
  }

  let upstream = await sendRequest(apiPath, method, request.headers, requestBody, activeAccessToken!);
  if (!upstream) {
    return upstreamUnavailable();
  }

  if (upstream.status === 401 && refreshToken && !refreshedTokens) {
    refreshedTokens = await refreshSession(refreshToken);
    if (refreshedTokens) {
      activeAccessToken = refreshedTokens.accessToken;
      upstream = await sendRequest(apiPath, method, request.headers, requestBody, activeAccessToken);
      if (!upstream) {
        return upstreamUnavailable();
      }
    }
  }

  const response = await forwardResponse(upstream);
  if (upstream.status === 401) {
    clearAuthCookies(response);
  } else if (refreshedTokens) {
    setAuthCookies(response, refreshedTokens);
  }

  return response;
}

async function sendRequest(
  path: string,
  method: PlatformMethod,
  originalHeaders: Headers,
  body: ArrayBuffer | undefined,
  accessToken: string,
): Promise<Response | null> {
  const headers = new Headers(originalHeaders);
  headers.delete("cookie");
  headers.delete("host");
  headers.set("Authorization", `Bearer ${accessToken}`);

  return requestPlatformApi(path, {
    method,
    headers,
    body,
  });
}

async function refreshSession(
  refreshToken: string,
): Promise<{
  tokenType: string;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
} | null> {
  const response = await requestAuthApi("/refresh", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
  });
  if (!response?.ok) {
    return null;
  }

  const payload = await readJson(response);
  return isAuthTokensResponse(payload) ? payload : null;
}

async function forwardResponse(upstream: Response): Promise<NextResponse> {
  const headers = new Headers();
  headers.set("Cache-Control", "no-store");
  const contentType = upstream.headers.get("content-type");
  if (contentType) {
    headers.set("Content-Type", contentType);
  }

  const body = upstream.status === 204 ? null : await upstream.arrayBuffer();
  return new NextResponse(body, { status: upstream.status, headers });
}
