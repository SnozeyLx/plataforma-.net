import type { Metadata } from "next";
import Link from "next/link";
import { PasswordResetForm } from "@/components/password-reset-form";

export const metadata: Metadata = {
  title: "Redefinir senha | Playlab",
  description: "Escolha uma nova senha para acessar sua conta Playlab.",
};

interface ResetPasswordSearchParams {
  email?: string | string[];
  token?: string | string[];
}

export default async function ResetPasswordPage({
  searchParams,
}: {
  searchParams: Promise<ResetPasswordSearchParams>;
}) {
  const params = await searchParams;

  return (
    <div className="page-shell">
      <header className="topbar">
        <Link aria-label="Playlab" className="brand" href="/">
          <span className="brand-name">PLAYLAB</span>
        </Link>
        <div className="topbar-meta">Conta segura</div>
      </header>
      <PasswordResetForm
        email={typeof params.email === "string" ? params.email : ""}
        token={typeof params.token === "string" ? params.token : ""}
      />
      <footer className="footer">
        <span>Playlab</span>
        <span>Redefinição segura de senha</span>
      </footer>
    </div>
  );
}
