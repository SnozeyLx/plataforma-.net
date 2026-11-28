import { proxyMatchmaking } from "@/lib/massive-matchmaking";

export async function POST(request: Request) {
  return proxyMatchmaking(request, "POST", "coop");
}
