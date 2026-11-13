import { NextResponse } from "next/server";
import { proxyMassiveMatchmaking } from "@/lib/massive-matchmaking";

interface RouteContext {
  params: Promise<{ ticketId: string }>;
}

async function handleTicketRequest(
  request: Request,
  context: RouteContext,
  method: "GET" | "DELETE",
) {
  const { ticketId } = await context.params;
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(ticketId)) {
    return NextResponse.json({ title: "Ticket inválido." }, { status: 400 });
  }

  return proxyMassiveMatchmaking(request, method, ticketId);
}

export async function GET(request: Request, context: RouteContext) {
  return handleTicketRequest(request, context, "GET");
}

export async function DELETE(request: Request, context: RouteContext) {
  return handleTicketRequest(request, context, "DELETE");
}
