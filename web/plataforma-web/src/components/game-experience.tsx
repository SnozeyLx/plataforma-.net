"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useState } from "react";
import {
  formatPrice,
  type GamePlatform,
  type StoreGame,
} from "@/lib/store-catalog";

const platformOptions: {
  id: GamePlatform;
  label: string;
  buttonLabel: string;
  symbol: string;
}[] = [
  { id: "pc", label: "Computador", buttonLabel: "Jogar no PC", symbol: "PC" },
  {
    id: "mobile",
    label: "Celular",
    buttonLabel: "Baixar para celular",
    symbol: "MOB",
  },
];

const launcherPlatforms = [
  { id: "windows", label: "Windows x64" },
  { id: "macos-x64", label: "macOS Intel" },
  { id: "macos-arm64", label: "macOS Apple Silicon" },
  { id: "linux-x64", label: "Linux x64" },
] as const;
type LauncherPlatform = (typeof launcherPlatforms)[number]["id"];
type LauncherPlatformOption = (typeof launcherPlatforms)[number];

interface GameLibraryItem {
  slug: string;
  isOwned: boolean;
  windowsBuildAvailable: boolean;
  webGlBuildAvailable: boolean;
}

function isGameLibraryItem(value: unknown): value is GameLibraryItem {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const item = value as Record<string, unknown>;
  return (
    typeof item.slug === "string" &&
    typeof item.isOwned === "boolean" &&
    typeof item.windowsBuildAvailable === "boolean" &&
    typeof item.webGlBuildAvailable === "boolean"
  );
}

function readApiMessage(value: unknown): string | null {
  if (typeof value !== "object" || value === null) {
    return null;
  }

  const body = value as Record<string, unknown>;
  return typeof body.title === "string" ? body.title : null;
}

function isGameBuildAccess(value: unknown): value is { url: string } {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as Record<string, unknown>).url === "string"
  );
}

function isLauncherDownloadAccess(value: unknown): value is { url: string } {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as Record<string, unknown>).url === "string"
  );
}

export function GameExperience({ game }: { game: StoreGame }) {
  const isConnectedGame = game.slug === "dash-and-crash";
  const [demoOwnership, setDemoOwnership] = useState(false);
  const [serverOwnership, setServerOwnership] = useState(false);
  const [serverWindowsAvailable, setServerWindowsAvailable] = useState(false);
  const [serverWebGlAvailable, setServerWebGlAvailable] = useState(false);
  const [isLoadingLibrary, setIsLoadingLibrary] = useState(isConnectedGame);
  const [isLoadingBuildAccess, setIsLoadingBuildAccess] = useState(false);
  const [isWebGlOpen, setIsWebGlOpen] = useState(false);
  const [webGlUrl, setWebGlUrl] = useState("");
  const [actionMessage, setActionMessage] = useState("");
  const [selectedLauncherPlatform, setSelectedLauncherPlatform] =
    useState<LauncherPlatformOption | null>(null);

  const hasGame = isConnectedGame ? serverOwnership : demoOwnership;
  const hasWebGl = isConnectedGame
    ? serverWebGlAvailable
    : game.availablePlatforms.includes("webgl");
  const canPlayWebGl = hasGame && hasWebGl && !isLoadingLibrary && !isLoadingBuildAccess;

  useEffect(() => {
    if (!isConnectedGame) {
      return;
    }

    const controller = new AbortController();
    fetch("/api/platform/library", {
      cache: "no-store",
      signal: controller.signal,
    })
      .then(async (response) => {
        const body: unknown = await response.json().catch(() => null);
        if (!response.ok) {
          throw new Error(
            readApiMessage(body) ?? "Não foi possível consultar sua biblioteca.",
          );
        }

        if (!Array.isArray(body)) {
          throw new Error("A API retornou uma biblioteca inválida.");
        }

        const ownedGame = body.find(
          (item): item is GameLibraryItem =>
            isGameLibraryItem(item) && item.slug === game.slug,
        );
        setServerOwnership(ownedGame?.isOwned ?? false);
        setServerWindowsAvailable(ownedGame?.windowsBuildAvailable ?? false);
        setServerWebGlAvailable(ownedGame?.webGlBuildAvailable ?? false);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setActionMessage(
            error instanceof Error
              ? error.message
              : "Não foi possível consultar sua biblioteca.",
          );
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoadingLibrary(false);
        }
      });

    return () => controller.abort();
  }, [game.slug, isConnectedGame]);

  function toggleOwnership() {
    setDemoOwnership((current) => !current);
    if (hasGame) {
      setIsWebGlOpen(false);
      setWebGlUrl("");
    }
    setActionMessage("");
  }

  async function getBuildUrl(platform: "windows" | "webgl"): Promise<string> {
    const response = await fetch(
      `/api/platform/games/${encodeURIComponent(game.slug)}/access?platform=${platform}`,
      { cache: "no-store" },
    );
    const body: unknown = await response.json().catch(() => null);
    if (!response.ok) {
      throw new Error(readApiMessage(body) ?? "A API recusou o acesso ao build.");
    }

    if (!isGameBuildAccess(body)) {
      throw new Error("A API retornou um link de build inválido.");
    }

    return body.url;
  }

  async function startWebGl() {
    setActionMessage("");
    if (!isConnectedGame) {
      setIsWebGlOpen(true);
      setActionMessage(
        "Player de demonstração aberto. O build WebGL real ainda não foi conectado.",
      );
      return;
    }

    setIsLoadingBuildAccess(true);
    try {
      setWebGlUrl(await getBuildUrl("webgl"));
      setIsWebGlOpen(true);
    } catch (error) {
      setActionMessage(
        error instanceof Error ? error.message : "Não foi possível abrir o jogo.",
      );
    } finally {
      setIsLoadingBuildAccess(false);
    }
  }

  function playOnPc() {
    setActionMessage("");
    if (!isConnectedGame) {
      setActionMessage("O launcher ainda não está conectado a este jogo de demonstração.");
      return;
    }

    if (!hasGame) {
      setActionMessage("Adicione o jogo à sua biblioteca para jogar no PC.");
      return;
    }

    if (!serverWindowsAvailable) {
      setActionMessage(
        "A build para Windows ainda não está publicada. Use o WebGL quando estiver disponível.",
      );
      return;
    }

    window.open(
      `playlab://game/${encodeURIComponent(game.slug)}`,
      "_blank",
      "noopener,noreferrer",
    );
    setActionMessage(
      "Abrindo o launcher. Se o jogo não estiver instalado, o download começará nele. Se o launcher não estiver instalado, baixe-o abaixo.",
    );
  }

  function handlePlatformAction(platform: (typeof platformOptions)[number]) {
    if (platform.id === "pc") {
      playOnPc();
      return;
    }

    setActionMessage(
      `A opção ${platform.label} está liberada para demonstração; o arquivo ainda não está conectado.`,
    );
  }

  async function downloadLauncher(platform: LauncherPlatform) {
    setActionMessage("");
    setIsLoadingBuildAccess(true);
    try {
      const response = await fetch(`/api/platform/launcher/${platform}/access`, {
        cache: "no-store",
      });
      const body: unknown = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(
          readApiMessage(body) ?? "A API recusou o download do launcher.",
        );
      }

      if (!isLauncherDownloadAccess(body)) {
        throw new Error("A API retornou um link de launcher inválido.");
      }

      window.location.assign(body.url);
    } catch (error) {
      setActionMessage(
        error instanceof Error ? error.message : "Não foi possível solicitar o download.",
      );
    } finally {
      setIsLoadingBuildAccess(false);
    }
  }

  return (
    <div className="game-page">
      <header className="store-topbar">
        <Link
          aria-label="Playlab — voltar para minha conta"
          className="brand store-brand"
          href="/"
        >
          <Image
            alt=""
            aria-hidden="true"
            className="brand-logo"
            height={44}
            src="/logo_teste.svg"
            unoptimized
            width={44}
          />
          <span>
            <span className="brand-name">PLAYLAB</span>
            <span className="brand-caption">Página do jogo</span>
          </span>
        </Link>
        <nav aria-label="Navegação da página do jogo" className="store-nav">
          <Link className="store-nav-link" href="/loja">
            Catálogo
          </Link>
          <Link className="store-nav-link store-account-link" href="/">
            Minha conta
          </Link>
        </nav>
      </header>

      <main className="game-main">
        <nav aria-label="Breadcrumb" className="game-breadcrumb">
          <Link href="/loja">Loja</Link>
          <span aria-hidden="true">/</span>
          <span aria-current="page">{game.title}</span>
        </nav>

        <section aria-labelledby="game-title" className="game-detail-hero">
          <div className="game-detail-art">
            <Image
              alt=""
              className="game-detail-image"
              fill
              priority
              sizes="(max-width: 820px) 100vw, 55vw"
              src={game.image}
            />
            <span className="game-detail-art-badge">
              {game.sandboxAccess ? "JOGO DE TESTE" : "JOGO DE DEMONSTRAÇÃO"}
            </span>
          </div>
          <div className="game-detail-summary">
            <p className="store-eyebrow">{game.category} · BIBLIOTECA PLAYLAB</p>
            <h1 id="game-title">{game.title}</h1>
            <p>{game.description}</p>
            <div className="game-detail-meta">
              <span>Desenvolvido por {game.developer}</span>
              <strong>
                {game.sandboxAccess ? "Acesso de teste" : formatPrice(game.price)}
              </strong>
            </div>
            <span
              className={
                hasGame
                  ? "game-ownership-badge owned"
                  : "game-ownership-badge not-owned"
              }
            >
              {hasGame ? "Na sua biblioteca" : "Ainda não está na sua biblioteca"}
            </span>
          </div>
        </section>

        {actionMessage && (
          <p className="game-action-message" role="status">
            {actionMessage}
          </p>
        )}

        <div className="game-content-grid">
          <div className="game-content-main">
            <section aria-labelledby="game-player-title" className="game-player-card">
              <div className="game-section-heading">
                <div>
                  <p className="store-eyebrow">JOGUE SEM INSTALAR</p>
                  <h2 id="game-player-title">Jogar no navegador</h2>
                </div>
                <button
                  className="primary-button game-webgl-button"
                  disabled={!canPlayWebGl}
                  onClick={startWebGl}
                  type="button"
                >
                  {isWebGlOpen ? "Abrir novamente" : "Jogar WebGL"}
                </button>
              </div>

              <div className={isWebGlOpen ? "game-player-screen running" : "game-player-screen"}>
                {webGlUrl ? (
                  <iframe
                    allow="autoplay; fullscreen"
                    className="game-player-frame"
                    referrerPolicy="no-referrer"
                    src={webGlUrl}
                    title={`${game.title} — WebGL`}
                  />
                ) : (
                  <>
                    <Image
                      alt=""
                      className="game-player-image"
                      fill
                      sizes="(max-width: 820px) 100vw, 65vw"
                      src={game.image}
                    />
                    <div className="game-player-overlay">
                      <span aria-hidden="true" className="game-player-mark">
                        {isWebGlOpen ? "▶" : hasGame && hasWebGl ? "◇" : "⌑"}
                      </span>
                      <p className="store-eyebrow">PLAYER WEBGL</p>
                      <h3>
                        {isWebGlOpen
                          ? "Espaço do jogo preparado"
                          : "Sua próxima partida começa aqui"}
                      </h3>
                      <p>
                        {isLoadingLibrary
                          ? "Consultando sua licença na plataforma."
                          : !hasGame
                            ? "Entre e aguarde a licença sandbox ser confirmada pela API."
                            : hasWebGl
                              ? "Acesse o build WebGL publicado com um link temporário."
                              : "O build WebGL ainda não foi publicado ou configurado."}
                      </p>
                    </div>
                  </>
                )}
              </div>
              <p className="game-player-footnote">
                {webGlUrl
                  ? "Acesso temporário protegido pela Bunny CDN."
                  : "O jogo só abre quando a API confirma a propriedade e o build está configurado."}
              </p>
            </section>

            <section aria-labelledby="game-about-title" className="game-about-card">
              <p className="store-eyebrow">SOBRE O JOGO</p>
              <h2 id="game-about-title">Uma nova aventura espera por você</h2>
              <p>{game.about}</p>
              <div className="game-feature-list">
                <span>Conteúdo de exemplo</span>
                <span>Progresso sincronizável</span>
                <span>Multiplataforma</span>
              </div>
            </section>
          </div>

          <aside aria-label="Biblioteca e plataformas" className="game-sidebar">
            <section aria-labelledby="game-library-title" className="game-library-card">
              <p className="store-eyebrow">SUA BIBLIOTECA</p>
              <h2 id="game-library-title">
                {hasGame ? "Este jogo é seu" : "Adicione à sua biblioteca"}
              </h2>
              <p>
                {hasGame
                  ? "As opções compatíveis ficam disponíveis enquanto esta licença estiver ativa."
                  : "Os recursos do jogo ficam bloqueados até que ele faça parte da sua biblioteca."}
              </p>
              {!isConnectedGame && !hasGame && (
                <Link className="game-store-link" href="/loja">
                  Ver catálogo e loja
                </Link>
              )}
              {isConnectedGame ? (
                <p>
                  {isLoadingLibrary
                    ? "Consultando a licença na API…"
                    : hasGame
                      ? "Licença confirmada pela API. O acesso aos builds exige propriedade."
                      : "A licença sandbox só é concedida quando o modo de teste está ativo."}
                </p>
              ) : (
                <details className="game-demo-controls">
                  <summary>Controles de demonstração</summary>
                  <p>
                    Simula a propriedade somente nesta página; não altera a conta nem a biblioteca.
                  </p>
                  <button
                    aria-pressed={hasGame}
                    className="secondary-button game-ownership-toggle"
                    onClick={toggleOwnership}
                    type="button"
                  >
                    {hasGame ? "Remover licença de teste" : "Simular que possuo o jogo"}
                  </button>
                </details>
              )}
            </section>

            <section aria-labelledby="game-platforms-title" className="game-platforms-card">
              <div className="game-section-heading compact">
                <div>
                  <p className="store-eyebrow">SUAS PLATAFORMAS</p>
                  <h2 id="game-platforms-title">Downloads</h2>
                </div>
              </div>
              {platformOptions.map((platform) => {
                const isAvailable = isConnectedGame
                  ? platform.id === "pc" && serverWindowsAvailable
                  : game.availablePlatforms.includes(platform.id);
                const isEnabled =
                  hasGame && isAvailable && !isLoadingLibrary && !isLoadingBuildAccess;

                return (
                  <div className="game-platform-option" key={platform.id}>
                    <span
                      aria-hidden="true"
                      className={
                        isAvailable
                          ? "game-platform-symbol available"
                          : "game-platform-symbol"
                      }
                    >
                      {platform.symbol}
                    </span>
                    <div className="game-platform-copy">
                      <strong>{platform.label}</strong>
                      <span>
                        {!isAvailable
                          ? isConnectedGame && platform.id === "pc"
                            ? "Build Windows não publicada"
                            : "Build não cadastrado"
                          : isLoadingLibrary
                            ? "Consultando a API…"
                            : !hasGame
                            ? "Adquira para liberar"
                            : isConnectedGame
                              ? "Abre o launcher e inicia a instalação"
                              : "Disponível para demonstração"}
                      </span>
                    </div>
                    <button
                      aria-label={platform.buttonLabel}
                      className="game-download-button"
                      disabled={!isEnabled}
                      onClick={() => handlePlatformAction(platform)}
                      type="button"
                    >
                      {platform.buttonLabel}
                    </button>
                  </div>
                );
              })}
              {isConnectedGame && (
                <div
                  aria-label="Instaladores do launcher"
                  className="game-launcher-downloads"
                >
                  <label
                    className="game-launcher-platform-label"
                    htmlFor="launcher-platform"
                  >
                    Sistema operacional
                  </label>
                  <select
                    className="game-launcher-platform-select"
                    disabled={isLoadingBuildAccess}
                    id="launcher-platform"
                    onChange={(event) =>
                      setSelectedLauncherPlatform(
                        launcherPlatforms.find(
                          (platform) => platform.id === event.target.value,
                        ) ?? null,
                      )
                    }
                    value={selectedLauncherPlatform?.id ?? ""}
                  >
                    <option value="">Selecione seu sistema</option>
                    {launcherPlatforms.map((platform) => (
                      <option key={platform.id} value={platform.id}>
                        {platform.label}
                      </option>
                    ))}
                  </select>
                  <button
                    className="game-download-button game-launcher-download-button"
                    disabled={!selectedLauncherPlatform || isLoadingBuildAccess}
                    onClick={() => {
                      if (selectedLauncherPlatform) {
                        void downloadLauncher(selectedLauncherPlatform.id);
                      }
                    }}
                    type="button"
                  >
                    {selectedLauncherPlatform
                      ? `Baixar para ${selectedLauncherPlatform.label}`
                      : "Selecione uma plataforma"}
                  </button>
                </div>
              )}
              <p className="game-platforms-note">
                {isConnectedGame
                  ? "Selecione o sistema para baixar o instalador correspondente. O launcher recebe atualizações incrementais; o download do jogo só começa após a validação da sua licença."
                  : "Builds e instaladores reais ainda não foram conectados."}
              </p>
            </section>
          </aside>
        </div>

        <footer className="store-footer game-footer">
          <Link href="/loja">PLAYLAB · CATÁLOGO</Link>
          <span>Conteúdo e licenças de demonstração</span>
        </footer>
      </main>
    </div>
  );
}
