"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

interface CurrentUser {
  userId: string;
  email: string;
}

interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

type AuthMode = "login" | "register";
type ApiStatus = "checking" | "online" | "offline";

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
  const [message, setMessage] = useState("");
  const [messageType, setMessageType] = useState<"error" | "success">("error");

  const refreshUser = useCallback(async () => {
    const response = await fetch("/api/auth/me", { cache: "no-store" });
    if (!response.ok) {
      setUser(null);
      return false;
    }

    setUser((await response.json()) as CurrentUser);
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
          <div className="brand-mark" aria-hidden="true">
            PL
          </div>
          <div>
            <div className="brand-name">PLAYLAB</div>
            <div className="brand-caption">Plataforma de jogos</div>
          </div>
        </div>
        <div className="topbar-meta">
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
                <div className="avatar" aria-hidden="true">
                  {user.email.charAt(0).toUpperCase()}
                </div>
                <div>
                  <div className="account-label">Conta conectada</div>
                  <div className="account-email">{user.email}</div>
                </div>
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
                <strong>O catálogo é o próximo capítulo</strong>
                <p>
                  A conexão com a conta está pronta. O portal ainda não lista jogos; esse será
                  um próximo passo do desenvolvimento.
                </p>
              </div>

              {message && (
                <p className={`form-message ${messageType}`} role="status">
                  {message}
                </p>
              )}

              <div className="dashboard-actions">
                <button
                  className="secondary-button"
                  disabled={isSubmitting}
                  onClick={() => void handleLogout()}
                  type="button"
                >
                  Encerrar sessão
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

      <footer className="footer">
        <span>
          <strong>PLAYLAB</strong> · Protótipo de conexão
        </span>
        <span>Next.js + ASP.NET Core Identity</span>
      </footer>
    </div>
  );
}
