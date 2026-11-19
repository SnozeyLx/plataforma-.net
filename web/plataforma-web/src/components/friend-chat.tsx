"use client";

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from "@microsoft/signalr";
import { type FormEvent, type KeyboardEvent, useEffect, useRef, useState } from "react";

interface Friend {
  userId: string;
  playerName: string;
}

interface ChatMessage {
  messageId: string;
  senderUserId: string;
  recipientUserId: string;
  content: string;
  createdAtUtc: string;
}

interface ChatHistory {
  messages: ChatMessage[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

type ConnectionStatus = "connecting" | "connected" | "reconnecting" | "offline";

function getErrorMessage(problem: ApiProblem | null, fallback: string): string {
  if (problem?.detail) {
    return problem.detail;
  }

  if (problem?.title) {
    return problem.title;
  }

  return Object.values(problem?.errors ?? {}).flat()[0] ?? fallback;
}

async function requestApi<T>(path: string): Promise<T> {
  const response = await fetch(`/api/platform${path}`, {
    cache: "no-store",
  });
  if (!response.ok) {
    let problem: ApiProblem | null = null;
    try {
      problem = (await response.json()) as ApiProblem;
    } catch {
      problem = null;
    }

    throw new Error(
      getErrorMessage(problem, "Não foi possível carregar a conversa."),
    );
  }

  return (await response.json()) as T;
}

function mergeMessages(current: ChatMessage[], incoming: ChatMessage[]): ChatMessage[] {
  const messages = new Map(current.map((message) => [message.messageId, message]));
  for (const message of incoming) {
    messages.set(message.messageId, message);
  }

  return [...messages.values()].sort(
    (first, second) =>
      Date.parse(first.createdAtUtc) - Date.parse(second.createdAtUtc) ||
      first.messageId.localeCompare(second.messageId),
  );
}

const connectionStatusLabels: Record<ConnectionStatus, string> = {
  connecting: "Conectando ao chat",
  connected: "Chat em tempo real",
  reconnecting: "Reconectando",
  offline: "Chat indisponível",
};

export function FriendChat({
  currentUserId,
  onClose,
}: {
  currentUserId: string;
  onClose: () => void;
}) {
  const [friends, setFriends] = useState<Friend[]>([]);
  const [selectedFriend, setSelectedFriend] = useState<Friend | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [connection, setConnection] = useState<HubConnection | null>(null);
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>("connecting");
  const [isLoadingFriends, setIsLoadingFriends] = useState(true);
  const [isLoadingHistory, setIsLoadingHistory] = useState(false);
  const [isLoadingOlder, setIsLoadingOlder] = useState(false);
  const [isSending, setIsSending] = useState(false);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [draft, setDraft] = useState("");
  const [error, setError] = useState("");
  const connectionRef = useRef<HubConnection | null>(null);
  const selectedFriendIdRef = useRef<string | null>(null);
  const messagesEndRef = useRef<HTMLDivElement | null>(null);
  const composerRef = useRef<HTMLTextAreaElement | null>(null);
  const lastMessageIdRef = useRef<string | null>(null);

  useEffect(() => {
    let active = true;

    void requestApi<Friend[]>("/friends")
      .then((loadedFriends) => {
        if (active) {
          setFriends(loadedFriends);
        }
      })
      .catch((requestError: unknown) => {
        if (active) {
          setError(
            requestError instanceof Error
              ? requestError.message
              : "Não foi possível carregar seus amigos.",
          );
        }
      })
      .finally(() => {
        if (active) {
          setIsLoadingFriends(false);
        }
      });

    const hubConnection = new HubConnectionBuilder()
      .withUrl("/api/platform/chat/hub", {
        transport: HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build();
    connectionRef.current = hubConnection;

    hubConnection.on("MessageReceived", (message: ChatMessage) => {
      const selectedFriendId = selectedFriendIdRef.current;
      if (
        selectedFriendId &&
        (message.senderUserId === selectedFriendId ||
          message.recipientUserId === selectedFriendId)
      ) {
        setMessages((current) => mergeMessages(current, [message]));
      }
    });
    hubConnection.onreconnecting(() => {
      if (active) {
        setConnectionStatus("reconnecting");
      }
    });
    hubConnection.onreconnected(() => {
      if (!active) {
        return;
      }

      setConnectionStatus("connected");
      const friendUserId = selectedFriendIdRef.current;
      if (friendUserId) {
        void hubConnection.invoke("JoinConversation", friendUserId).catch(() => {
          setError("Não foi possível reabrir a conversa após reconectar.");
        });
      }
    });
    hubConnection.onclose(() => {
      if (active) {
        setConnectionStatus("offline");
      }
    });

    void hubConnection
      .start()
      .then(() => {
        if (active) {
          setConnection(hubConnection);
          setConnectionStatus("connected");
        }
      })
      .catch((connectionError: unknown) => {
        if (active) {
          setConnectionStatus("offline");
          setIsLoadingHistory(false);
          setError(
            connectionError instanceof Error
              ? connectionError.message
              : "Não foi possível conectar ao chat.",
          );
        }
      });

    function handleKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      active = false;
      selectedFriendIdRef.current = null;
      window.removeEventListener("keydown", handleKeyDown);
      connectionRef.current = null;
      void hubConnection.stop();
    };
  }, [onClose]);

  useEffect(() => {
    if (!selectedFriend || !connection || connection.state !== HubConnectionState.Connected) {
      return;
    }

    const friendUserId = selectedFriend.userId;
    let active = true;

    void (async () => {
      try {
        await connection.invoke("JoinConversation", friendUserId);
        const history = await requestApi<ChatHistory>(
          `/chat/conversations/${friendUserId}/messages?page=1&pageSize=50`,
        );
        if (active) {
          setMessages((current) => mergeMessages(current, history.messages));
          setPage(history.page);
          setHasMore(history.hasMore);
        }
      } catch (requestError: unknown) {
        if (active) {
          setError(
            requestError instanceof Error
              ? requestError.message
              : "Não foi possível abrir a conversa.",
          );
        }
      } finally {
        if (active) {
          setIsLoadingHistory(false);
        }
      }
    })();

    return () => {
      active = false;
      if (connection.state === HubConnectionState.Connected) {
        void connection.invoke("LeaveConversation", friendUserId).catch(() => {});
      }
    };
  }, [connection, selectedFriend]);

  useEffect(() => {
    const lastMessageId = messages[messages.length - 1]?.messageId ?? null;
    if (lastMessageId !== lastMessageIdRef.current) {
      messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
      lastMessageIdRef.current = lastMessageId;
    }
  }, [messages]);

  function selectFriend(friend: Friend) {
    selectedFriendIdRef.current = friend.userId;
    setSelectedFriend(friend);
    setMessages([]);
    setIsLoadingHistory(true);
    setPage(1);
    setHasMore(false);
    setError("");
    setDraft("");
    if (composerRef.current) {
      composerRef.current.style.height = "auto";
      composerRef.current.style.overflowY = "hidden";
    }
  }

  function handleDraftChange(value: string, textarea: HTMLTextAreaElement) {
    const maxHeight = 144;
    textarea.style.height = "auto";
    textarea.style.height = `${Math.min(textarea.scrollHeight, maxHeight)}px`;
    textarea.style.overflowY = textarea.scrollHeight > maxHeight ? "auto" : "hidden";
    setDraft(value);
  }

  async function loadOlderMessages() {
    if (!selectedFriend || isLoadingOlder || !hasMore) {
      return;
    }

    setIsLoadingOlder(true);
    try {
      const nextPage = page + 1;
      const history = await requestApi<ChatHistory>(
        `/chat/conversations/${selectedFriend.userId}/messages?page=${nextPage}&pageSize=50`,
      );
      setMessages((current) => mergeMessages(current, history.messages));
      setPage(history.page);
      setHasMore(history.hasMore);
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Não foi possível carregar mensagens anteriores.",
      );
    } finally {
      setIsLoadingOlder(false);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const content = draft.trim();
    if (!selectedFriend || !content || !connection) {
      return;
    }

    if (connection.state !== HubConnectionState.Connected) {
      setError("Aguarde a conexão com o chat para enviar uma mensagem.");
      return;
    }

    setIsSending(true);
    setError("");
    try {
      await connection.invoke("SendMessage", selectedFriend.userId, content);
      setDraft("");
      if (composerRef.current) {
        composerRef.current.style.height = "auto";
        composerRef.current.style.overflowY = "hidden";
      }
    } catch (sendError: unknown) {
      setError(
        sendError instanceof Error
          ? sendError.message
          : "Não foi possível enviar a mensagem.",
      );
    } finally {
      setIsSending(false);
    }
  }

  function handleComposerKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      event.currentTarget.form?.requestSubmit();
    }
  }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <section
        aria-labelledby="friend-chat-title"
        aria-modal="true"
        className="chat-dialog"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
      >
        <div className="chat-dialog-heading">
          <div>
            <div className="eyebrow">
              <span className="eyebrow-line" />
              SOCIAL DA PLATAFORMA
            </div>
            <h2 className="friends-dialog-title" id="friend-chat-title">
              Chat com amigos
            </h2>
          </div>
          <div className={`chat-connection-status ${connectionStatus}`} aria-live="polite">
            <span className="status-dot" />
            {connectionStatusLabels[connectionStatus]}
          </div>
          <button
            aria-label="Fechar chat"
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

        <div className="chat-layout">
          <aside aria-label="Lista de amigos" className="chat-friends">
            <h3 className="chat-section-title">Conversas</h3>
            {isLoadingFriends ? (
              <p className="loading-state" role="status">
                Carregando amigos…
              </p>
            ) : friends.length === 0 ? (
              <p className="friend-empty-state">
                Adicione amigos para começar uma conversa.
              </p>
            ) : (
              <ul className="chat-friend-list">
                {friends.map((friend) => (
                  <li key={friend.userId}>
                    <button
                      aria-current={selectedFriend?.userId === friend.userId ? "true" : undefined}
                      className={
                        selectedFriend?.userId === friend.userId
                          ? "chat-friend-button active"
                          : "chat-friend-button"
                      }
                      onClick={() => selectFriend(friend)}
                      type="button"
                    >
                      <span className="friend-avatar" aria-hidden="true">
                        {friend.playerName.charAt(0).toUpperCase()}
                      </span>
                      <span className="friend-name">{friend.playerName}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </aside>

          <section aria-label="Conversa" className="chat-conversation">
            {selectedFriend ? (
              <>
                <header className="chat-conversation-heading">
                  <span className="friend-avatar" aria-hidden="true">
                    {selectedFriend.playerName.charAt(0).toUpperCase()}
                  </span>
                  <h3 className="chat-section-title">{selectedFriend.playerName}</h3>
                </header>

                <div aria-live="polite" className="chat-messages">
                  {hasMore && (
                    <button
                      className="small-button chat-load-older"
                      disabled={isLoadingOlder}
                      onClick={() => void loadOlderMessages()}
                      type="button"
                    >
                      {isLoadingOlder ? "Carregando…" : "Carregar mensagens anteriores"}
                    </button>
                  )}
                  {isLoadingHistory && (
                    <p className="loading-state" role="status">
                      Carregando conversa…
                    </p>
                  )}
                  {!isLoadingHistory && messages.length === 0 && (
                    <p className="friend-empty-state">
                      Ainda não há mensagens. Diga oi!
                    </p>
                  )}
                  {messages.map((message) => {
                    const isMine = message.senderUserId === currentUserId;
                    return (
                      <article
                        className={isMine ? "chat-message-row mine" : "chat-message-row"}
                        key={message.messageId}
                      >
                        <div className="chat-message-bubble">
                          <p>{message.content}</p>
                          <time dateTime={message.createdAtUtc}>
                            {new Date(message.createdAtUtc).toLocaleTimeString([], {
                              hour: "2-digit",
                              minute: "2-digit",
                            })}
                          </time>
                        </div>
                      </article>
                    );
                  })}
                  <div ref={messagesEndRef} />
                </div>

                <form className="chat-composer" onSubmit={handleSubmit}>
                  <textarea
                    aria-label="Mensagem"
                    enterKeyHint="send"
                    maxLength={2000}
                    onChange={(event) =>
                      handleDraftChange(event.currentTarget.value, event.currentTarget)
                    }
                    onKeyDown={handleComposerKeyDown}
                    placeholder="Escreva uma mensagem…"
                    ref={composerRef}
                    rows={1}
                    value={draft}
                  />
                  <button
                    className="primary-button"
                    disabled={
                      isSending ||
                      !draft.trim() ||
                      connectionStatus !== "connected"
                    }
                    type="submit"
                  >
                    {isSending ? "Enviando…" : "Enviar"}
                  </button>
                </form>
                <p className="chat-composer-hint">Enter envia · Shift+Enter quebra a linha</p>
              </>
            ) : (
              <div className="chat-empty-conversation">
                <span className="friend-avatar" aria-hidden="true">
                  ↗
                </span>
                <h3>Escolha um amigo</h3>
                <p>As conversas ficam disponíveis enquanto a amizade estiver ativa.</p>
              </div>
            )}
          </section>
        </div>
      </section>
    </div>
  );
}
