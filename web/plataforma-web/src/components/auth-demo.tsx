"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import Image from "next/image";
import Link from "next/link";
import { FriendChat } from "@/components/friend-chat";
import { FriendManager } from "@/components/friend-manager";

interface CurrentUser {
  userId: string;
  email: string;
}

interface PlayerProfile {
  userId: string;
  playerName: string | null;
  profileIconId: number;
}

interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

type AuthMode = "login" | "register";
type ApiStatus = "checking" | "online" | "offline";

const profileIconTemplates = Array.from(
  { length: 8 },
  (_, index) => `/profile-icons/avatar-${index + 1}.svg`,
);

function getErrorMessage(problem: ApiProblem | null, fallback: string): string {
  if (problem?.detail) {
    return problem.detail;
  }

  if (problem?.title) {
    return problem.title;
  }

  const firstValidationError = Object.values(problem?.errors ?? {}).flat()[0];
  return firstValidationError ?? fallback;
}

async function readProblem(response: Response): Promise<ApiProblem | null> {
  try {
    return (await response.json()) as ApiProblem;
  } catch {
    return null;
  }
}

export function AuthDemo() {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [mode, setMode] = useState<AuthMode>("login");
  const [apiStatus, setApiStatus] = useState<ApiStatus>("checking");
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isFriendManagerOpen, setIsFriendManagerOpen] = useState(false);
  const [isChatOpen, setIsChatOpen] = useState(false);
  const [isProfileModalOpen, setIsProfileModalOpen] = useState(false);
  const [profile, setProfile] = useState<PlayerProfile | null>(null);
  const [playerNameDraft, setPlayerNameDraft] = useState("");
  const [profileIconDraft, setProfileIconDraft] = useState(1);
  const [message, setMessage] = useState("");
  const [messageType, setMessageType] = useState<"error" | "success">("error");

  const refreshProfile = useCallback(async () => {
    const response = await fetch("/api/platform/profile", { cache: "no-store" });
    if (!response.ok) {
      return false;
    }

    const data = (await response.json()) as PlayerProfile;
    setProfile(data);
    setPlayerNameDraft(data.playerName ?? "");
    setProfileIconDraft(data.profileIconId || 1);
    return true;
  }, []);

  const refreshUser = useCallback(async () => {
    const response = await fetch("/api/auth/me", { cache: "no-store" });
    if (!response.ok) {
      setUser(null);
      return false;
    }

    setUser((await response.json()) as CurrentUser);
    await refreshProfile();
    return true;
  }, []);

  useEffect(() => {
    let active = true;

    async function checkConnection() {
      const [healthResponse, sessionResponse] = await Promise.all([
        fetch("/api/health", { cache: "no-store" }),
        fetch("/api/auth/me", { cache: "no-store" }),
      ]);

      if (!active) {
        return;
      }

      setApiStatus(healthResponse.ok ? "online" : "offline");
      if (sessionResponse.ok) {
        setUser((await sessionResponse.json()) as CurrentUser);
        await refreshProfile();
      } else {
        setUser(null);
      }
      setIsLoading(false);
    }

    void checkConnection().catch(() => {
      if (active) {
        setApiStatus("offline");
        setUser(null);
        setIsLoading(false);
      }
    });

    return () => {
      active = false;
    };
  }, []);

  function setFeedback(text: string, type: "error" | "success") {
    setMessage(text);
    setMessageType(type);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setIsSubmitting(true);
    setMessage("");

    const formData = new FormData(event.currentTarget);
    const endpoint = mode === "login" ? "/api/auth/login" : "/api/auth/register";
    const payload = {
      email: String(formData.get("email") ?? "").trim(),
      password: String(formData.get("password") ?? ""),
      ...(mode === "register"
        ? { playerName: String(formData.get("playerName") ?? "").trim() }
        : {}),
    };

    try {
      const response = await fetch(endpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        setFeedback(
          getErrorMessage(
            await readProblem(response),
            "Não foi possível concluir a solicitação.",
          ),
          "error",
        );
        return;
      }

      if (mode === "register") {
        setMode("login");
        setFeedback("Conta criada. Agora entre com seu e-mail e senha.", "success");
        return;
      }

      const authenticated = await refreshUser();
      if (!authenticated) {
        setFeedback("Login aceito, mas não foi possível carregar o perfil.", "error");
        return;
      }

      setFeedback("Login realizado com sucesso.", "success");
    } catch {
      setApiStatus("offline");
      setFeedback("Não foi possível conectar à API. Verifique se o backend está ativo.", "error");
    } finally {
      setIsSubmitting(false);
    }
  }

  async function handleLogout() {
    setIsSubmitting(true);
    setMessage("");

    try {
      const response = await fetch("/api/auth/logout", { method: "POST" });
      if (!response.ok) {
        setFeedback("Não foi possível encerrar a sessão. Tente novamente.", "error");
        return;
      }

      setUser(null);
      setMode("login");
      setFeedback("Sessão encerrada.", "success");
    } catch {
      setFeedback("Não foi possível conectar à API para encerrar a sessão.", "error");
    } finally {
      setIsSubmitting(false);
    }
  }

  function openProfileModal() {
    setPlayerNameDraft(profile?.playerName ?? "");
    setProfileIconDraft(profile?.profileIconId || 1);
    setIsProfileModalOpen(true);
  }

  async function handleProfileSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setIsSubmitting(true);
    setMessage("");

    try {
      const response = await fetch("/api/platform/profile", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          playerName: playerNameDraft.trim(),
          profileIconId: profileIconDraft,
        }),
      });

      if (!response.ok) {
        setFeedback(
          getErrorMessage(await readProblem(response), "Não foi possível salvar o perfil."),
          "error",
        );
        return;
      }

      const updated = (await response.json()) as PlayerProfile;
      setProfile(updated);
      setPlayerNameDraft(updated.playerName ?? "");
      setProfileIconDraft(updated.profileIconId);
      setIsProfileModalOpen(false);
      setFeedback("Perfil atualizado.", "success");
    } catch {
      setFeedback("Não foi possível conectar à API para salvar o perfil.", "error");
    } finally {
      setIsSubmitting(false);
    }
  }

  function switchMode(nextMode: AuthMode) {
    setMode(nextMode);
    setMessage("");
  }

  const apiStatusLabel =
    apiStatus === "checking"
      ? "Verificando API"
      : apiStatus === "online"
        ? "Auth API conectada"
        : "Auth API indisponível";

  return (
    <div className="page-shell">
      <header className="topbar">
        <div className="brand">
          <Image
            alt=""
            aria-hidden="true"
            className="brand-logo"
            height={44}
            src="/logo_teste.svg"
            unoptimized
            width={44}
          />
          <div>
            <div className="brand-name">PLAYLAB</div>
            <div className="brand-caption">Plataforma de jogos</div>
          </div>
        </div>
        <div className="topbar-meta">
          <Link className="topbar-store-link" href="/loja">
            Loja
          </Link>
          <div className="api-status" aria-live="polite">
            <span className={`status-dot ${apiStatus}`} />
            <span>{apiStatusLabel}</span>
          </div>
          <span className="topbar-note">DEMO DE AUTENTICAÇÃO</span>
        </div>
      </header>

      <main className="main-content">
        {user ? (
          <section className="dashboard" aria-labelledby="dashboard-title">
            <div className="dashboard-heading">
              <div>
                <div className="eyebrow">
                  <span className="eyebrow-line" />
                  SESSÃO AUTENTICADA
                </div>
                <h1 className="dashboard-title" id="dashboard-title">
                  Bem-vindo à plataforma.
                </h1>
                <p className="dashboard-description">
                  A conexão com o serviço de identidade está funcionando.
                </p>
              </div>
              <span className="card-badge">ONLINE</span>
            </div>

            <div className="dashboard-card">
              <div className="account-line">
                <button
                  className="avatar profile-avatar-button"
                  type="button"
                  onClick={openProfileModal}
                  aria-label="Editar perfil"
                >
                  <img
                    src={profileIconTemplates[(profile?.profileIconId ?? 1) - 1]}
                    alt=""
                    width={48}
                    height={48}
                  />
                </button>
                <div>
                  <div className="account-label">Conta conectada</div>
                  <div className="account-email">
                    {profile?.playerName || user.email}
                  </div>
                </div>
                <button
                  className="profile-edit-button"
                  onClick={openProfileModal}
                  type="button"
                >
                  Editar perfil
                </button>
                <button
                  className="profile-logout-button"
                  disabled={isSubmitting}
                  onClick={() => void handleLogout()}
                  type="button"
                  title="Encerrar sessão"
                  aria-label="Encerrar sessão"
                >
                  ↪
                </button>
              </div>

              <div className="dashboard-grid">
                <div className="info-tile">
                  <div className="tile-label">Identidade</div>
                  <div className="tile-value">Verificada pela Auth API</div>
                </div>
                <div className="info-tile">
                  <div className="tile-label">Sessão</div>
                  <div className="tile-value">Access token protegido</div>
                </div>
              </div>

              <div className="next-step">
                <strong>Seu grupo de jogo começa aqui</strong>
                <p>
                  Encontre jogadores, envie solicitações e prepare sua lista para futuras
                  partidas cooperativas.
                </p>
              </div>

              {message && (
                <p className={`form-message ${messageType}`} role="status">
                  {message}
                </p>
              )}

              <div className="dashboard-actions">
                <button
                  className="primary-button"
                  onClick={() => setIsFriendManagerOpen(true)}
                  type="button"
                >
                  Gerenciar amigos
                </button>
                <button
                  className="secondary-button"
                  onClick={() => setIsChatOpen(true)}
                  type="button"
                >
                  Chat com amigos
                </button>
              </div>
            </div>
          </section>
        ) : (
          <section className="auth-layout">
            <div className="hero-copy">
              <div className="eyebrow">
                <span className="eyebrow-line" />
 SEU PRÓXIMO UNIVERSO
              </div>
              <h1 className="hero-title">
                Um lugar.
                <br />
                Muitos <span>jogos.</span>
              </h1>
              <p className="hero-description">
                Entre na sua conta e acompanhe o início da sua jornada pela plataforma.
                Esta tela de demonstração já conversa com o serviço de identidade.
              </p>
              <ul className="feature-list">
                <li>
                  <span className="feature-icon" aria-hidden="true">
                    ◈
                  </span>
                  Uma conta para os jogos da plataforma
                </li>
                <li>
                  <span className="feature-icon" aria-hidden="true">
                    ↗
                  </span>
                  Sessão protegida por cookies HttpOnly
                </li>
                <li>
                  <span className="feature-icon" aria-hidden="true">
                    ◎
                  </span>
                  Base pronta para conectar seu catálogo
                </li>
              </ul>
            </div>

            <div className="auth-card">
              <div className="card-heading">
                <div>
                  <h2 className="card-title">
                    {mode === "login" ? "Bom ter você de volta" : "Crie sua conta"}
                  </h2>
                  <p className="card-subtitle">
                    {mode === "login"
                      ? "Entre para continuar para a plataforma."
                      : "Um só perfil para começar sua jornada."}
                  </p>
                </div>
                <span className="card-badge">{mode === "login" ? "LOGIN" : "NOVO"}</span>
              </div>

              {isLoading ? (
                <p className="loading-state" role="status">
                  Verificando sua sessão…
                </p>
              ) : (
                <form className="auth-form" onSubmit={handleSubmit}>
                  {mode === "register" && (
                    <label className="field">
                      <span className="form-label">
                        Gamertag
                        <span className="field-hint">3–24 letras, números ou _</span>
                      </span>
                      <input
                        autoComplete="nickname"
                        className="form-input"
                        maxLength={24}
                        minLength={3}
                        name="playerName"
                        pattern="[A-Za-z0-9_]{3,24}"
                        placeholder="player_01"
                        required
                      />
                    </label>
                  )}

                  <label className="field">
                    <span className="form-label">E-mail</span>
                    <input
                      autoComplete="email"
                      className="form-input"
                      name="email"
                      placeholder="voce@exemplo.com"
                      required
                      type="email"
                    />
                  </label>

                  <label className="field">
                    <span className="form-label">
                      Senha
                      {mode === "register" && (
                        <span className="field-hint">Mínimo de 12 caracteres</span>
                      )}
                    </span>
                    <input
                      autoComplete={mode === "login" ? "current-password" : "new-password"}
                      className="form-input"
                      minLength={mode === "register" ? 12 : undefined}
                      name="password"
                      placeholder="••••••••••••"
                      required
                      type="password"
                    />
                  </label>

                  {message && (
                    <p className={`form-message ${messageType}`} role="status">
                      {message}
                    </p>
                  )}

                  <button className="primary-button" disabled={isSubmitting} type="submit">
                    {isSubmitting
                      ? "Aguarde…"
                      : mode === "login"
                        ? "Entrar na plataforma"
                        : "Criar conta"}
                    {!isSubmitting && <span aria-hidden="true">→</span>}
                  </button>
                </form>
              )}

              <p className="auth-switch">
                {mode === "login" ? "Ainda não tem uma conta?" : "Já possui uma conta?"}
                <button
                  className="text-button"
                  onClick={() => switchMode(mode === "login" ? "register" : "login")}
                  type="button"
                >
                  {mode === "login" ? "Criar conta" : "Fazer login"}
                </button>
              </p>

              <div className="security-note">
                <span className="security-icon" aria-hidden="true">
                  ◆
                </span>
                Os tokens ficam em cookies HttpOnly gerenciados pelo servidor Next.js, não em
                localStorage.
              </div>
            </div>
          </section>
        )}
      </main>

      {isProfileModalOpen && user && (
        <div
          className="profile-modal-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.currentTarget === event.target) {
              setIsProfileModalOpen(false);
            }
          }}
        >
          <section
            className="profile-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="profile-modal-title"
          >
            <div className="profile-modal-heading">
              <div>
                <span className="profile-modal-eyebrow">CONFIGURAÇÕES DA CONTA</span>
                <h2 id="profile-modal-title">Editar perfil</h2>
              </div>
              <button
                className="profile-modal-close"
                type="button"
                onClick={() => setIsProfileModalOpen(false)}
                aria-label="Fechar"
              >
                ×
              </button>
            </div>

            <form className="profile-modal-form" onSubmit={handleProfileSave}>
              <label className="field">
                <span className="form-label">
                  Gamertag
                  <span className="field-hint">3–24 letras, números ou _</span>
                </span>
                <input
                  className="form-input"
                  maxLength={24}
                  minLength={3}
                  pattern="[A-Za-z0-9_]{3,24}"
                  value={playerNameDraft}
                  onChange={(event) => setPlayerNameDraft(event.target.value)}
                  required
                />
              </label>

              <div>
                <span className="form-label">Ícone do perfil</span>
                <div className="profile-icon-grid" role="radiogroup" aria-label="Ícones do perfil">
                  {[1, 2, 3, 4, 5, 6, 7, 8].map((iconId) => (
                    <button
                      key={iconId}
                      className={`profile-icon-option ${profileIconDraft === iconId ? "selected" : ""}`}
                      type="button"
                      role="radio"
                      aria-checked={profileIconDraft === iconId}
                      onClick={() => setProfileIconDraft(iconId)}
                    >
                      <img
                        src={profileIconTemplates[iconId - 1]}
                        alt={`Ícone de perfil ${iconId}`}
                        width={44}
                        height={44}
                      />
                    </button>
                  ))}
                </div>
              </div>

              <div className="profile-modal-actions">
                <span className="profile-modal-email">{user.email}</span>
                <button
                  className="primary-button"
                  type="submit"
                  disabled={isSubmitting}
                >
                  Salvar perfil
                </button>
              </div>
            </form>
          </section>
        </div>
      )}

      {isFriendManagerOpen && <FriendManager onClose={() => setIsFriendManagerOpen(false)} />}
      {isChatOpen && user && (
        <FriendChat
          currentUserId={user.userId}
          onClose={() => setIsChatOpen(false)}
        />
      )}

      <footer className="footer">
        <span>
          <strong>PLAYLAB</strong> · Protótipo de conexão
        </span>
        <span>Next.js + ASP.NET Core Identity</span>
      </footer>
    </div>
  );
}
