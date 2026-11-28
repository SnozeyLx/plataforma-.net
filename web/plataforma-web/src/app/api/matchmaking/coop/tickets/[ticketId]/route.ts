import { handleMatchmakingTicketItem } from "@/lib/massive-matchmaking";

interface RouteContext {
  params: Promise<{ ticketId: string }>;
}

export async function GET(request: Request, context: RouteContext) {
  return handleMatchmakingTicketItem(request, context.params, "GET", "coop");
}

export async function DELETE(request: Request, context: RouteContext) {
  return handleMatchmakingTicketItem(
    request,
    context.params,
    "DELETE",
    "coop",
  );
}
