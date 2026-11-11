import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import {
  accessTokenCookie,
  clearAuthCookies,
  isAuthTokensResponse,
  readJson,
  refreshTokenCookie,
  requestAuthApi,
  setAuthCookies,
  upstreamUnavailable,
} from "@/lib/auth-api";

export const dynamic = "force-dynamic";

async function requestCurrentUser(accessToken: string): Promise<Response | null> {
  return requestAuthApi("/me", {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
}

export async function GET(): Promise<NextResponse> {
  const cookieStore = await cookies();
  const accessToken = cookieStore.get(accessTokenCookie)?.value;
  const refreshToken = cookieStore.get(refreshTokenCookie)?.value;

  if (!accessToken && !refreshToken) {
    return NextResponse.json(null, {
      headers: { "Cache-Control": "no-store" },
    });
  }

  let upstream = accessToken ? await requestCurrentUser(accessToken) : null;
  if (accessToken && !upstream) {
    return upstreamUnavailable();
  }

  if (upstream?.ok) {
    return NextResponse.json(await readJson(upstream), {
      headers: { "Cache-Control": "no-store" },
    });
  }

  if (upstream && upstream.status !== 401) {
    return NextResponse.json(await readJson(upstream), { status: upstream.status });
  }

  if (!refreshToken) {
    const response = NextResponse.json(null, {
      headers: { "Cache-Control": "no-store" },
    });
    clearAuthCookies(response);
    return response;
  }

  const refreshResponse = await requestAuthApi("/refresh", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
  });

  if (!refreshResponse) {
    return upstreamUnavailable();
  }

  const refreshedTokens = await readJson(refreshResponse);
  if (!refreshResponse.ok || !isAuthTokensResponse(refreshedTokens)) {
    const response = NextResponse.json(null, {
      headers: { "Cache-Control": "no-store" },
    });
    clearAuthCookies(response);
    return response;
  }

  upstream = await requestCurrentUser(refreshedTokens.accessToken);
  if (!upstream) {
    return upstreamUnavailable();
  }

  if (!upstream.ok) {
    const response = NextResponse.json(null, {
      headers: { "Cache-Control": "no-store" },
    });
    clearAuthCookies(response);
    return response;
  }

  const response = NextResponse.json(await readJson(upstream), {
    headers: { "Cache-Control": "no-store" },
  });
  setAuthCookies(response, refreshedTokens);
  return response;
}
