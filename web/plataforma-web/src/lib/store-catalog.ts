export type GamePlatform = "webgl" | "pc" | "mobile";

export interface StoreProduct {
  id: string;
  title: string;
  description: string;
  price: number;
  image: string;
  category: string;
  gameTitle?: string;
  badge?: string;
}

export interface StoreGame extends StoreProduct {
  slug: string;
  developer: string;
  about: string;
  availablePlatforms: GamePlatform[];
  sandboxAccess?: boolean;
}

export const games: StoreGame[] = [
  {
    id: "game-dash-and-crash",
    slug: "dash-and-crash",
    title: "Dash & Crash",
    description: "Corrida e ação cooperativa no jogo de teste da plataforma.",
    about:
      "Dash & Crash está conectado à biblioteca da plataforma. Durante os testes, a API concede a licença sandbox sem cobrança; builds só ficam disponíveis depois de publicados e configurados.",
    price: 0,
    image: "/store/dash-and-crash.svg",
    category: "Corrida",
    badge: "ACESSO DE TESTE",
    developer: "Dash & Crash",
    availablePlatforms: ["webgl", "pc"],
    sandboxAccess: true,
  },
  {
    id: "game-circuito-celeste",
    slug: "circuito-celeste",
    title: "Circuito Celeste",
    description: "Corridas arcade em pistas que cruzam o espaço.",
    about:
      "Acelere por circuitos suspensos entre planetas, ajuste seu veículo e descubra atalhos em uma experiência de corrida arcade criada para partidas rápidas.",
    price: 79.9,
    image: "/store/game-circuito-celeste.svg",
    category: "Corrida",
    badge: "DESTAQUE",
    developer: "Estúdio Horizonte",
    availablePlatforms: ["webgl", "pc"],
  },
  {
    id: "game-ecos-de-arken",
    slug: "ecos-de-arken",
    title: "Ecos de Arken",
    description: "Explore ruínas antigas em uma aventura fantástica.",
    about:
      "Atravesse ruínas, descubra histórias esquecidas e prepare-se para uma aventura fantástica. Esta página usa conteúdo fictício para demonstrar a futura biblioteca de jogos.",
    price: 119.9,
    image: "/store/game-ecos-de-arken.svg",
    category: "Aventura",
    developer: "Estúdio Raiz",
    availablePlatforms: ["pc", "mobile"],
  },
  {
    id: "game-protocolo-eclipse",
    slug: "protocolo-eclipse",
    title: "Protocolo Eclipse",
    description: "Estratégia e ação em uma colônia distante.",
    about:
      "Coordene sua equipe em missões táticas por uma colônia distante. Explore o protótipo de apresentação multiplataforma com opções fictícias de build.",
    price: 89.9,
    image: "/store/game-protocolo-eclipse.svg",
    category: "Ação",
    badge: "NOVO",
    developer: "Estúdio Órbita",
    availablePlatforms: ["webgl", "pc", "mobile"],
  },
  {
    id: "game-fronteira-neon",
    slug: "fronteira-neon",
    title: "Fronteira Neon",
    description: "Uma jornada por cidades iluminadas e desconhecidas.",
    about:
      "Explore uma cidade futurista, encontre caminhos entre seus distritos e descubra os segredos escondidos sob as luzes de neon.",
    price: 99.9,
    image: "/store/game-fronteira-neon.svg",
    category: "Aventura",
    developer: "Estúdio Prisma",
    availablePlatforms: ["webgl"],
  },
];

export const gameItems: StoreProduct[] = [
  {
    id: "item-modulo-pulse",
    title: "Módulo Pulse",
    description: "Visual reativo para seu equipamento.",
    price: 12.9,
    image: "/store/item-modulo-pulse.svg",
    category: "Cosmético",
    gameTitle: "Protocolo Eclipse",
    badge: "VISUAL",
  },
  {
    id: "item-lamina-alvorecer",
    title: "Lâmina do Alvorecer",
    description: "Um acabamento especial para sua coleção.",
    price: 8.9,
    image: "/store/item-lamina-alvorecer.svg",
    category: "Equipamento",
    gameTitle: "Ecos de Arken",
  },
  {
    id: "item-rastro-estelar",
    title: "Rastro Estelar",
    description: "Deixe um rastro de luz a cada volta.",
    price: 5.9,
    image: "/store/item-rastro-estelar.svg",
    category: "Efeito",
    gameTitle: "Circuito Celeste",
  },
];

export const gameCategories = ["Todos", "Ação", "Aventura", "Corrida"];

const currency = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
});

export function formatPrice(price: number): string {
  return currency.format(price);
}
