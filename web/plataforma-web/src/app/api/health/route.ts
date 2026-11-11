import { NextResponse } from "next/server";
import { requestApiHealth } from "@/lib/auth-api";

export const dynamic = "force-dynamic";

export async function GET(): Promise<NextResponse> {
  const response = await requestApiHealth();

  return response?.ok
    ? NextResponse.json({ status: "online" })
    : NextResponse.json(
        { status: "offline", title: "Não foi possível conectar à Auth API." },
        { status: 503 },
      );
}
