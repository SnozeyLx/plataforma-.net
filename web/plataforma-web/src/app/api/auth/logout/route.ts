import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import {
  clearAuthCookies,
  isSameOriginMutation,
  readJson,
  refreshTokenCookie,
  requestAuthApi,
  upstreamUnavailable,
} from "@/lib/auth-api";

export async function POST(request: Request): Promise<NextResponse> {
  if (!isSameOriginMutation(request)) {
    return NextResponse.json({ title: "Origem da requisição não permitida." }, { status: 403 });
  }

  const cookieStore = await cookies();
  const refreshToken = cookieStore.get(refreshTokenCookie)?.value;
  let upstream: Response | null = null;

  if (refreshToken) {
    upstream = await requestAuthApi("/logout", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
  }

  if (refreshToken && !upstream) {
    return upstreamUnavailable();
  }

  const response = new NextResponse(null, { status: 204 });
  clearAuthCookies(response);

  if (upstream && !upstream.ok) {
    const errorResponse = NextResponse.json(await readJson(upstream), {
      status: upstream.status,
    });
    clearAuthCookies(errorResponse);
    return errorResponse;
  }

  return response;
}
