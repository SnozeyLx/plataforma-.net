import { proxyMassiveMatchmaking } from "@/lib/massive-matchmaking";

export async function POST(request: Request) {
  return proxyMassiveMatchmaking(request, "POST");
}
