"use client";

import { type FormEvent, type ReactNode, useCallback, useEffect, useState } from "react";

interface PlayerProfile {
  userId: string;
  playerName: string | null;
}

interface Player {
  userId: string;
  playerName: string;
}

interface Friend extends Player {
  friendsSinceUtc: string;
}

interface FriendRequest extends Player {
  requestId: string;
  status: "Pending" | "Accepted";
  createdAtUtc: string;
}

interface BlockedPlayer extends Player {
  blockedAtUtc: string;
}

interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

type FriendTab = "friends" | "search" | "requests" | "blocked";

const tabs: { id: FriendTab; label: string }[] = [
  { id: "friends", label: "Amigos" },
  { id: "search", label: "Encontrar" },
  { id: "requests", label: "Solicitações" },
  { id: "blocked", label: "Bloqueados" },
];

function getErrorMessage(problem: ApiProblem | null, fallback: string): string {
  if (problem?.detail) {
    return problem.detail;
  }

  if (problem?.title) {
    return problem.title;
  }

  return Object.values(problem?.errors ?? {}).flat()[0] ?? fallback;
}

async function readProblem(response: Response): Promise<ApiProblem | null> {
  try {
    return (await response.json()) as ApiProblem;
  } catch {
    return null;
  }
}

async function requestApi<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api/platform${path}`, {
    ...init,
    cache: "no-store",
    headers: {
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...init?.headers,
    },
  });

  if (!response.ok) {
    throw new Error(
      getErrorMessage(await readProblem(response), "Não foi possível concluir a solicitação."),
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function fetchFriendData() {
  const [profile, friends, incoming, outgoing, blocked] = await Promise.all([
    requestApi<PlayerProfile>("/profile"),
    requestApi<Friend[]>("/friends"),
    requestApi<FriendRequest[]>("/friends/requests/incoming"),
    requestApi<FriendRequest[]>("/friends/requests/outgoing"),
    requestApi<BlockedPlayer[]>("/friends/blocks"),
  ]);

  return { profile, friends, incoming, outgoing, blocked };
}

export function FriendManager({ onClose }: { onClose: () => void }) {
  const [activeTab, setActiveTab] = useState<FriendTab>("friends");
  const [profile, setProfile] = useState<PlayerProfile | null>(null);
  const [friends, setFriends] = useState<Friend[]>([]);
  const [incoming, setIncoming] = useState<FriendRequest[]>([]);
  const [outgoing, setOutgoing] = useState<FriendRequest[]>([]);
  const [blocked, setBlocked] = useState<BlockedPlayer[]>([]);
  const [searchResults, setSearchResults] = useState<Player[]>([]);
  const [searchText, setSearchText] = useState("");
  const [playerName, setPlayerName] = useState("");
  const [showOutgoing, setShowOutgoing] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  useEffect(() => {
    let active = true;

    void fetchFriendData()
      .then((data) => {
        if (!active) {
          return;
        }

        setProfile(data.profile);
        setFriends(data.friends);
        setIncoming(data.incoming);
        setOutgoing(data.outgoing);
        setBlocked(data.blocked);
        setPlayerName(data.profile.playerName ?? "");
        setIsLoading(false);
      })
      .catch((requestError: unknown) => {
        if (!active) {
          return;
        }

        setError(
          requestError instanceof Error
            ? requestError.message
            : "Não foi possível carregar sua lista de amigos.",
        );
        setIsLoading(false);
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  const loadData = useCallback(async (): Promise<boolean> => {
    try {
      const data = await fetchFriendData();
      setProfile(data.profile);
      setFriends(data.friends);
      setIncoming(data.incoming);
      setOutgoing(data.outgoing);
      setBlocked(data.blocked);
      setPlayerName(data.profile.playerName ?? "");
      setIsLoading(false);
      return true;
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Não foi possível carregar sua lista de amigos.",
      );
      setIsLoading(false);
      return false;
    }
  }, []);

  async function runAction(
    action: () => Promise<unknown>,
    successMessage: string,
  ): Promise<boolean> {
    setIsSubmitting(true);
    setError("");
    setSuccess("");

    try {
      await action();
      if (!(await loadData())) {
        return false;
      }

      setSuccess(successMessage);
      return true;
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Não foi possível concluir a ação.",
      );
      return false;
    } finally {
      setIsSubmitting(false);
    }
  }

  async function handlePlayerNameSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await runAction(
      () =>
        requestApi<PlayerProfile>("/profile/player-name", {
          method: "PUT",
          body: JSON.stringify({ playerName: playerName.trim() }),
        }),
      "Gamertag salvo.",
    );
  }

  async function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setIsSubmitting(true);
    setError("");
    setSuccess("");

    try {
      const result = await requestApi<Player[]>(
        `/friends/search?q=${encodeURIComponent(searchText.trim())}`,
      );
      setSearchResults(result);
      if (result.length === 0) {
        setSuccess("Nenhuma conta encontrada com esse início de gamertag.");
      }
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Não foi possível buscar jogadores.",
      );
    } finally {
      setIsSubmitting(false);
    }
  }

  function handleRequest(userId: string) {
    void runAction(
      () =>
        requestApi<FriendRequest>("/friends/requests", {
          method: "POST",
          body: JSON.stringify({ targetUserId: userId }),
        }),
      "Solicitação enviada.",
    ).then((succeeded) => {
      if (succeeded) {
        setSearchResults((current) => current.filter((player) => player.userId !== userId));
      }
    });
  }

  function handleAccept(requestId: string) {
    void runAction(
      () => requestApi(`/friends/requests/${requestId}/accept`, { method: "POST" }),
      "Solicitação aceita.",
    );
  }

  function handleDecline(requestId: string) {
    void runAction(
      () => requestApi(`/friends/requests/${requestId}/decline`, { method: "POST" }),
      "Solicitação recusada.",
    );
  }

  function handleCancel(requestId: string) {
    void runAction(
      () => requestApi(`/friends/requests/${requestId}`, { method: "DELETE" }),
      "Solicitação cancelada.",
    );
  }

  function handleRemoveFriend(userId: string) {
    void runAction(
      () => requestApi(`/friends/${userId}`, { method: "DELETE" }),
      "Amizade removida.",
    );
  }

  function handleBlock(userId: string) {
    void runAction(
      () => requestApi(`/friends/blocks/${userId}`, { method: "PUT" }),
      "Jogador bloqueado.",
    );
  }

  function handleUnblock(userId: string) {
    void runAction(
      () => requestApi(`/friends/blocks/${userId}`, { method: "DELETE" }),
      "Jogador desbloqueado.",
    );
  }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <section
        aria-labelledby="friend-manager-title"
        aria-modal="true"
        className="friends-dialog"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
      >
        <div className="friends-dialog-heading">
          <div>
            <div className="eyebrow">
              <span className="eyebrow-line" />
              SOCIAL DA PLATAFORMA
            </div>
            <h2 className="friends-dialog-title" id="friend-manager-title">
              Amigos
            </h2>
          </div>
          <button
            aria-label="Fechar janela de amigos"
            className="modal-close"
            onClick={onClose}
            type="button"
          >
            ×
          </button>
        </div>

        {error && (
          <p className="form-message error" role="alert">
            {error}
          </p>
        )}
        {success && (
          <p className="form-message success" role="status">
            {success}
          </p>
        )}

        {isLoading ? (
          <p className="loading-state" role="status">
            Carregando amigos…
          </p>
        ) : (
          <>
            {!profile?.playerName && (
              <form className="player-name-setup" onSubmit={handlePlayerNameSubmit}>
                <div>
                  <strong>Escolha seu gamertag</strong>
                  <p>Ele será público e permitirá que outras contas encontrem você.</p>
                </div>
                <div className="player-name-controls">
                  <input
                    autoComplete="nickname"
                    className="form-input"
                    maxLength={24}
                    minLength={3}
                    onChange={(event) => setPlayerName(event.target.value)}
                    pattern="[A-Za-z0-9_]{3,24}"
                    placeholder="Ex.: player_01"
                    required
                    value={playerName}
                  />
                  <button className="primary-button" disabled={isSubmitting} type="submit">
                    Salvar
                  </button>
                </div>
              </form>
            )}

            <nav aria-label="Seções de amigos" className="friend-tabs">
              {tabs.map((tab) => (
                <button
                  aria-current={activeTab === tab.id ? "page" : undefined}
                  className={activeTab === tab.id ? "friend-tab active" : "friend-tab"}
                  key={tab.id}
                  onClick={() => {
                    setActiveTab(tab.id);
                    setError("");
                    setSuccess("");
                  }}
                  type="button"
                >
                  {tab.label}
                </button>
              ))}
            </nav>

            <div className="friends-content">
              {activeTab === "friends" && (
                <PlayerList
                  emptyMessage="Sua lista ainda está vazia. Encontre alguém para jogar!"
                  isSubmitting={isSubmitting}
                  players={friends}
                  renderActions={(friend) => (
                    <>
                      <button
                        className="small-button"
                        disabled={isSubmitting}
                        onClick={() => handleRemoveFriend(friend.userId)}
                        type="button"
                      >
                        Remover
                      </button>
                      <button
                        className="small-button danger"
                        disabled={isSubmitting}
                        onClick={() => handleBlock(friend.userId)}
                        type="button"
                      >
                        Bloquear
                      </button>
                    </>
                  )}
                />
              )}

              {activeTab === "search" && (
                <>
                  <form className="friend-search-form" onSubmit={handleSearch}>
                    <label className="field">
                      <span className="form-label">Gamertag</span>
                      <input
                        autoComplete="off"
                        className="form-input"
                        maxLength={24}
                        minLength={3}
                        onChange={(event) => setSearchText(event.target.value)}
                        pattern="[A-Za-z0-9_]{3,24}"
                        placeholder="Busque pelas primeiras letras"
                        required
                        value={searchText}
                      />
                    </label>
                    <button className="primary-button" disabled={isSubmitting} type="submit">
                      Buscar
                    </button>
                  </form>
                  <PlayerList
                    emptyMessage="Busque por pelo menos três caracteres para encontrar jogadores."
                    isSubmitting={isSubmitting}
                    players={searchResults}
                    renderActions={(player) => (
                      <>
                        <button
                          className="small-button"
                          disabled={isSubmitting}
                          onClick={() => handleRequest(player.userId)}
                          type="button"
                        >
                          Adicionar
                        </button>
                        <button
                          className="small-button danger"
                          disabled={isSubmitting}
                          onClick={() => handleBlock(player.userId)}
                          type="button"
                        >
                          Bloquear
                        </button>
                      </>
                    )}
                  />
                </>
              )}

              {activeTab === "requests" && (
                <>
                  <div className="request-switch">
                    <button
                      className={showOutgoing ? "friend-tab" : "friend-tab active"}
                      onClick={() => setShowOutgoing(false)}
                      type="button"
                    >
                      Recebidas ({incoming.length})
                    </button>
                    <button
                      className={showOutgoing ? "friend-tab active" : "friend-tab"}
                      onClick={() => setShowOutgoing(true)}
                      type="button"
                    >
                      Enviadas ({outgoing.length})
                    </button>
                  </div>
                  {showOutgoing ? (
                    <PlayerList
                      emptyMessage="Você não tem solicitações enviadas."
                      isSubmitting={isSubmitting}
                      players={outgoing}
                      renderActions={(request) => (
                        <button
                          className="small-button"
                          disabled={isSubmitting}
                          onClick={() => handleCancel(request.requestId)}
                          type="button"
                        >
                          Cancelar
                        </button>
                      )}
                    />
                  ) : (
                    <PlayerList
                      emptyMessage="Você não tem solicitações recebidas."
                      isSubmitting={isSubmitting}
                      players={incoming}
                      renderActions={(request) => (
                        <>
                          <button
                            className="small-button"
                            disabled={isSubmitting}
                            onClick={() => handleAccept(request.requestId)}
                            type="button"
                          >
                            Aceitar
                          </button>
                          <button
                            className="small-button danger"
                            disabled={isSubmitting}
                            onClick={() => handleDecline(request.requestId)}
                            type="button"
                          >
                            Recusar
                          </button>
                        </>
                      )}
                    />
                  )}
                </>
              )}

              {activeTab === "blocked" && (
                <PlayerList
                  emptyMessage="Você não bloqueou nenhum jogador."
                  isSubmitting={isSubmitting}
                  players={blocked}
                  renderActions={(player) => (
                    <button
                      className="small-button"
                      disabled={isSubmitting}
                      onClick={() => handleUnblock(player.userId)}
                      type="button"
                    >
                      Desbloquear
                    </button>
                  )}
                />
              )}
            </div>
          </>
        )}
      </section>
    </div>
  );
}

function PlayerList<TPlayer extends Player>({
  players,
  emptyMessage,
  isSubmitting,
  renderActions,
}: {
  players: TPlayer[];
  emptyMessage: string;
  isSubmitting: boolean;
  renderActions: (player: TPlayer) => ReactNode;
}) {
  if (players.length === 0) {
    return <p className="friend-empty-state">{emptyMessage}</p>;
  }

  return (
    <ul className="friend-list">
      {players.map((player) => (
        <li className="friend-row" key={player.userId}>
          <div className="friend-avatar" aria-hidden="true">
            {player.playerName.charAt(0).toUpperCase()}
          </div>
          <span className="friend-name">{player.playerName}</span>
          <div className="friend-row-actions" aria-busy={isSubmitting}>
            {renderActions(player)}
          </div>
        </li>
      ))}
    </ul>
  );
}
