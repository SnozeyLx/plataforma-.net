import type { Metadata } from "next";
import { Storefront } from "@/components/storefront";

export const metadata: Metadata = {
  title: "Loja Playlab | Catálogo demonstrativo",
  description: "Explore a vitrine de jogos e itens da plataforma Playlab.",
};

export default function StorePage() {
  return <Storefront />;
}
