import { NextResponse } from "next/server";

export interface AuthTokensResponse {
  tokenType: string;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
}

export interface CurrentUserResponse {
  userId: string;
  email: string;
}

export const accessTokenCookie = "platform_access";
export const refreshTokenCookie = "platform_refresh";

export async function requestAuthApi(
  path: string,
  init?: RequestInit,
): Promise<Response | null> {
  const baseUrl = (process.env.AUTH_API_BASE_URL ?? "http://localhost:5000").replace(
    /\/+$/,
    "",
  );

  try {
    return await fetch(`${baseUrl}/api/auth${path}`, {
      ...init,
      cache: "no-store",
      redirect: "manual",
    });
  } catch {
    return null;
  }
}

export async function requestApiHealth(): Promise<Response | null> {
  const baseUrl = (process.env.AUTH_API_BASE_URL ?? "http://localhost:5000").replace(
    /\/+$/,
    "",
  );

  try {
    return await fetch(`${baseUrl}/health`, {
      cache: "no-store",
      redirect: "manual",
    });
  } catch {
    return null;
  }
}

export async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return null;
  }
}

export async function readRequestJson(request: Request): Promise<unknown> {
  try {
    return await request.json();
  } catch {
    return null;
  }
}

export function isAuthTokensResponse(value: unknown): value is AuthTokensResponse {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const tokens = value as Record<string, unknown>;
  return (
    typeof tokens.accessToken === "string" &&
    typeof tokens.refreshToken === "string" &&
    typeof tokens.accessTokenExpiresAtUtc === "string" &&
    typeof tokens.refreshTokenExpiresAtUtc === "string"
  );
}

export function setAuthCookies(
  response: NextResponse,
  tokens: AuthTokensResponse,
): void {
  const secure = process.env.NODE_ENV === "production";

  response.cookies.set(accessTokenCookie, tokens.accessToken, {
    httpOnly: true,
    secure,
    sameSite: "lax",
    path: "/",
    expires: new Date(tokens.accessTokenExpiresAtUtc),
  });
  response.cookies.set(refreshTokenCookie, tokens.refreshToken, {
    httpOnly: true,
    secure,
    sameSite: "lax",
    path: "/",
    expires: new Date(tokens.refreshTokenExpiresAtUtc),
  });
}

export function clearAuthCookies(response: NextResponse): void {
  response.cookies.set(accessTokenCookie, "", {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 0,
  });
  response.cookies.set(refreshTokenCookie, "", {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 0,
  });
}

export function isSameOriginMutation(request: Request): boolean {
  const origin = request.headers.get("origin");
  if (origin === null) {
    return true;
  }

  const forwardedHost = request.headers.get("x-forwarded-host")?.split(",")[0]?.trim();
  const host = forwardedHost ?? request.headers.get("host");
  const forwardedProtocol = request.headers.get("x-forwarded-proto")?.split(",")[0]?.trim();
  const protocol = forwardedProtocol ?? new URL(request.url).protocol.replace(/:$/, "");

  try {
    const originUrl = new URL(origin);
    if (host && originUrl.host.toLowerCase() === host.toLowerCase()) {
      return originUrl.protocol === `${protocol}:`;
    }

    return originUrl.origin === new URL(request.url).origin;
  } catch {
    return false;
  }
}

export function upstreamUnavailable(): NextResponse {
  return NextResponse.json(
    { title: "A API de autenticação está indisponível." },
    { status: 502 },
  );
}
