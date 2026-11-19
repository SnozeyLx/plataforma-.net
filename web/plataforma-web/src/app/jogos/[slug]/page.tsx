import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { GameExperience } from "@/components/game-experience";
import { games } from "@/lib/store-catalog";

interface GamePageProps {
  params: Promise<{ slug: string }>;
}

export function generateStaticParams() {
  return games.map((game) => ({ slug: game.slug }));
}

export async function generateMetadata({ params }: GamePageProps): Promise<Metadata> {
  const { slug } = await params;
  const game = games.find((item) => item.slug === slug);

  return {
    title: game ? `${game.title} | Playlab` : "Jogo não encontrado | Playlab",
    description: game?.description ?? "Página demonstrativa de jogo da plataforma.",
  };
}

export default async function GamePage({ params }: GamePageProps) {
  const { slug } = await params;
  const game = games.find((item) => item.slug === slug);

  if (!game) {
    notFound();
  }

  return <GameExperience game={game} />;
}
