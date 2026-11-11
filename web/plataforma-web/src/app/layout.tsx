import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Playlab | Plataforma de jogos",
  description: "Demonstração de autenticação para a plataforma de jogos.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="pt-BR">
      <body>{children}</body>
    </html>
  );
}
