"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";

interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

interface PasswordResetFormProps {
  email: string;
  token: string;
}

function getErrorMessage(problem: ApiProblem | null): string {
  if (problem?.detail) {
    return problem.detail;
  }

  if (problem?.title) {
    return problem.title;
  }

  const firstValidationError = Object.values(problem?.errors ?? {}).flat()[0];
  return firstValidationError ?? "Não foi possível redefinir a senha. Solicite um novo link.";
}

async function readProblem(response: Response): Promise<ApiProblem | null> {
  try {
    return (await response.json()) as ApiProblem;
  } catch {
    return null;
  }
}

export function PasswordResetForm({ email, token }: PasswordResetFormProps) {
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [message, setMessage] = useState("");
  const [messageType, setMessageType] = useState<"error" | "success">("error");

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage("");

    if (password !== confirmation) {
      setMessageType("error");
      setMessage("As senhas não coincidem.");
      return;
    }

    setIsSubmitting(true);
    try {
      const response = await fetch("/api/auth/reset-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, token, newPassword: password }),
      });

      if (!response.ok) {
        setMessageType("error");
        setMessage(getErrorMessage(await readProblem(response)));
        return;
      }

      setMessageType("success");
      setMessage("Senha redefinida. Você já pode entrar na plataforma.");
      setPassword("");
      setConfirmation("");
    } catch {
      setMessageType("error");
      setMessage("Não foi possível conectar à plataforma. Tente novamente.");
    } finally {
      setIsSubmitting(false);
    }
  }

  const hasResetToken = email.length > 0 && token.length > 0;

  return (
    <main className="main-content">
      <section className="reset-layout">
        <div className="auth-card reset-card">
          <div className="eyebrow">
            <span className="eyebrow-line" />
            Segurança da conta
          </div>
          <h1 className="card-title reset-title">Redefinir senha</h1>
          <p className="card-subtitle">
            Escolha uma nova senha para acessar sua conta.
          </p>

          {hasResetToken ? (
            <form className="auth-form" onSubmit={handleSubmit}>
              <label className="field">
                <span className="form-label">Nova senha</span>
                <input
                  autoComplete="new-password"
                  className="form-input"
                  maxLength={128}
                  minLength={12}
                  onChange={(event) => setPassword(event.currentTarget.value)}
                  required
                  type="password"
                  value={password}
                />
              </label>
              <label className="field">
                <span className="form-label">Confirme a nova senha</span>
                <input
                  autoComplete="new-password"
                  className="form-input"
                  maxLength={128}
                  minLength={12}
                  onChange={(event) => setConfirmation(event.currentTarget.value)}
                  required
                  type="password"
                  value={confirmation}
                />
              </label>
              <p className="field-hint">Use pelo menos 12 caracteres.</p>
              {message && (
                <div className={`form-message ${messageType}`} role="status">
                  {message}
                </div>
              )}
              <button className="primary-button" disabled={isSubmitting} type="submit">
                {isSubmitting ? "Salvando..." : "Salvar nova senha"}
              </button>
            </form>
          ) : (
            <div className="form-message error reset-message" role="alert">
              Este link está incompleto ou inválido. Solicite uma nova redefinição de senha.
            </div>
          )}

          <p className="auth-switch">
            <Link href="/">Voltar para a plataforma</Link>
          </p>
        </div>
      </section>
    </main>
  );
}
