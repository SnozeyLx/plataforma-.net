import { cookies } from "next/headers";
import {
  accessTokenCookie,
  isAuthTokensResponse,
  readJson,
  refreshTokenCookie,
  requestAuthApi,
  type AuthTokensResponse,
} from "@/lib/auth-api";

type GameSessionResult =
  | { kind: "authenticated"; accessToken: string; refreshedTokens?: AuthTokensResponse }
  | { kind: "unauthenticated" }
  | { kind: "unavailable" }
  | { kind: "upstream-error"; status: number; body: unknown };

async function validateAccessToken(accessToken: string): Promise<Response | null> {
  return requestAuthApi("/me", {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
}

export async function resolveGameSession(): Promise<GameSessionResult> {
  const cookieStore = await cookies();
  const accessToken = cookieStore.get(accessTokenCookie)?.value;
  const refreshToken = cookieStore.get(refreshTokenCookie)?.value;

  if (accessToken) {
    const userResponse = await validateAccessToken(accessToken);
    if (!userResponse) {
      return { kind: "unavailable" };
    }

    if (userResponse.ok) {
      return { kind: "authenticated", accessToken };
    }

    if (userResponse.status !== 401) {
      return {
        kind: "upstream-error",
        status: userResponse.status,
        body: await readJson(userResponse),
      };
    }
  }

  if (!refreshToken) {
    return { kind: "unauthenticated" };
  }

  const refreshResponse = await requestAuthApi("/refresh", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
  });
  if (!refreshResponse) {
    return { kind: "unavailable" };
  }

  if (!refreshResponse.ok) {
    if (refreshResponse.status === 401) {
      return { kind: "unauthenticated" };
    }

    return {
      kind: "upstream-error",
      status: refreshResponse.status,
      body: await readJson(refreshResponse),
    };
  }

  const refreshedTokens = await readJson(refreshResponse);
  if (!isAuthTokensResponse(refreshedTokens)) {
    return { kind: "unavailable" };
  }

  const userResponse = await validateAccessToken(refreshedTokens.accessToken);
  if (!userResponse) {
    return { kind: "unavailable" };
  }

  if (!userResponse.ok) {
    if (userResponse.status === 401) {
      return { kind: "unauthenticated" };
    }

    return {
      kind: "upstream-error",
      status: userResponse.status,
      body: await readJson(userResponse),
    };
  }

  return {
    kind: "authenticated",
    accessToken: refreshedTokens.accessToken,
    refreshedTokens,
  };
}
