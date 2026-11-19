"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useState } from "react";
import {
  formatPrice,
  gameCategories,
  gameItems,
  games,
  type StoreProduct,
} from "@/lib/store-catalog";

interface CartLine extends StoreProduct {
  quantity: number;
}


export function Storefront() {
  const [query, setQuery] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("Todos");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [isCartOpen, setIsCartOpen] = useState(false);
  const [notice, setNotice] = useState("");

  useEffect(() => {
    if (!isCartOpen) {
      return;
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setIsCartOpen(false);
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isCartOpen]);

  const normalizedQuery = query.trim().toLocaleLowerCase("pt-BR");
  const visibleGames = games.filter((game) => {
    const matchesCategory =
      selectedCategory === "Todos" || game.category === selectedCategory;
    const searchableText =
      `${game.title} ${game.description} ${game.category}`.toLocaleLowerCase("pt-BR");
    return matchesCategory && searchableText.includes(normalizedQuery);
  });
  const cartCount = cart.reduce((total, item) => total + item.quantity, 0);
  const cartTotal = cart.reduce((total, item) => total + item.price * item.quantity, 0);
  const featuredGame = games.find((game) => !game.sandboxAccess) ?? games[0];

  function addToCart(product: StoreProduct) {
    setCart((current) => {
      const existing = current.find((item) => item.id === product.id);
      if (existing) {
        return current.map((item) =>
          item.id === product.id ? { ...item, quantity: item.quantity + 1 } : item,
        );
      }

      return [...current, { ...product, quantity: 1 }];
    });
    setNotice(`${product.title} foi adicionado à sacola de demonstração.`);
  }

  function removeFromCart(productId: string) {
    setCart((current) => current.filter((item) => item.id !== productId));
  }

  return (
    <div className="store-page">
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
            <span className="brand-caption">Loja da plataforma</span>
          </span>
        </Link>

        <nav aria-label="Navegação da loja" className="store-nav">
          <a className="store-nav-link" href="#jogos">
            Jogos
          </a>
          <a className="store-nav-link" href="#itens">
            Itens
          </a>
          <Link className="store-nav-link store-account-link" href="/">
            Minha conta
          </Link>
          <button
            aria-expanded={isCartOpen}
            className="store-cart-trigger"
            onClick={() => {
              setNotice("");
              setIsCartOpen(true);
            }}
            type="button"
          >
            Sacola <span aria-live="polite">{cartCount}</span>
          </button>
        </nav>
      </header>

      <main className="store-main">
        <p className="store-demo-note">
          <span>VITRINE DE DEMONSTRAÇÃO</span>
          Catálogo e preços fictícios. Nenhuma compra ou cobrança é processada.
        </p>

        <section aria-labelledby="store-featured-title" className="store-featured">
          <div aria-hidden="true" className="store-featured-art">
            <Image
              alt=""
              className="store-featured-image"
              fill
              priority
              sizes="(max-width: 820px) 100vw, 1180px"
              src="/store/feature-circuito-celeste.svg"
            />
          </div>
          <div aria-hidden="true" className="store-featured-shade" />
          <div className="store-featured-content">
            <p className="store-eyebrow">JOGUE ALÉM DO HORIZONTE</p>
            <span className="store-featured-badge">DESTAQUE DA VITRINE</span>
            <h1 id="store-featured-title">
              <Link href={`/jogos/${featuredGame.slug}`}>{featuredGame.title}</Link>
            </h1>
            <p>
              Corridas de alta velocidade, pistas impossíveis e mundos para
              descobrir. Uma amostra de como os jogos podem aparecer no catálogo.
            </p>
            <div className="store-featured-actions">
              <span className="store-featured-price">{formatPrice(featuredGame.price)}</span>
              <button
                className="primary-button store-featured-button"
                onClick={() => addToCart(featuredGame)}
                type="button"
              >
                Adicionar à sacola
              </button>
              <Link
                className="store-featured-detail-link"
                href={`/jogos/${featuredGame.slug}`}
              >
                Ver página do jogo
              </Link>
            </div>
          </div>
        </section>

        {notice && (
          <p className="store-notice" role="status">
            {notice}
          </p>
        )}

        <section aria-labelledby="store-games-title" className="store-section" id="jogos">
          <div className="store-section-heading">
            <div>
              <p className="store-eyebrow">EXPLORE A VITRINE</p>
              <h2 id="store-games-title">Jogos em destaque</h2>
              <p className="store-section-description">
                Exemplos visuais para testar a apresentação do catálogo.
              </p>
            </div>
            <span className="store-result-count">{visibleGames.length} jogos</span>
          </div>

          <div className="store-catalog-tools">
            <label className="store-search">
              <span className="store-search-icon" aria-hidden="true">
                ⌕
              </span>
              <span className="visually-hidden">Buscar jogos</span>
              <input
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Buscar no catálogo"
                type="search"
                value={query}
              />
            </label>
            <div aria-label="Filtrar jogos por gênero" className="store-category-filters">
              {gameCategories.map((category) => (
                <button
                  aria-pressed={selectedCategory === category}
                  className={
                    selectedCategory === category
                      ? "store-category-filter active"
                      : "store-category-filter"
                  }
                  key={category}
                  onClick={() => setSelectedCategory(category)}
                  type="button"
                >
                  {category}
                </button>
              ))}
            </div>
          </div>

          {visibleGames.length > 0 ? (
            <div className="store-product-grid">
              {visibleGames.map((game) => (
                <article className="store-product-card" key={game.id}>
                  <div className="store-product-art">
                    <Image
                      alt=""
                      className="store-product-image"
                      height={675}
                      sizes="(max-width: 520px) 45vw, (max-width: 820px) 44vw, 25vw"
                      src={game.image}
                      width={1200}
                    />
                    {game.badge && (
                      <span className="store-product-badge">{game.badge}</span>
                    )}
                  </div>
                  <div className="store-product-body">
                    <p className="store-product-category">
                      {game.category} <span>·</span> Jogo digital
                    </p>
                    <h3>
                      <Link href={`/jogos/${game.slug}`}>{game.title}</Link>
                    </h3>
                    <p className="store-product-description">{game.description}</p>
                    <div className="store-product-footer">
                      {game.sandboxAccess ? (
                        <>
                          <span className="store-price">Acesso de teste</span>
                          <Link
                            aria-label={`Ver acesso de teste de ${game.title}`}
                            className="store-add-button"
                            href={`/jogos/${game.slug}`}
                          >
                            Ver jogo
                          </Link>
                        </>
                      ) : (
                        <>
                          <span className="store-price">{formatPrice(game.price)}</span>
                          <button
                            aria-label={`Adicionar ${game.title} à sacola`}
                            className="store-add-button"
                            onClick={() => addToCart(game)}
                            type="button"
                          >
                            <span aria-hidden="true">+</span> Adicionar
                          </button>
                        </>
                      )}
                    </div>
                  </div>
                </article>
              ))}
            </div>
          ) : (
            <p className="store-empty-results">
              Nenhum jogo encontrado. Tente outra busca ou gênero.
            </p>
          )}
        </section>

        <section aria-labelledby="store-items-title" className="store-section" id="itens">
          <div className="store-section-heading">
            <div>
              <p className="store-eyebrow">PERSONALIZE SUA JORNADA</p>
              <h2 id="store-items-title">Itens dos jogos</h2>
              <p className="store-section-description">
                Uma segunda vitrine para cosméticos e conteúdos associados a cada jogo.
              </p>
            </div>
            <span className="store-result-count">{gameItems.length} itens</span>
          </div>

          <div className="store-product-grid store-item-grid">
            {gameItems.map((item) => (
              <article className="store-product-card store-item-card" key={item.id}>
                <div className="store-product-art">
                  <Image
                    alt=""
                    className="store-product-image"
                    height={480}
                    sizes="(max-width: 520px) 45vw, (max-width: 820px) 44vw, 25vw"
                    src={item.image}
                    width={720}
                  />
                  {item.badge && <span className="store-product-badge">{item.badge}</span>}
                </div>
                <div className="store-product-body">
                  <p className="store-product-category">
                    {item.category} <span>·</span> {item.gameTitle}
                  </p>
                  <h3>{item.title}</h3>
                  <p className="store-product-description">{item.description}</p>
                  <div className="store-product-footer">
                    <span className="store-price">{formatPrice(item.price)}</span>
                    <button
                      aria-label={`Adicionar ${item.title} à sacola`}
                      className="store-add-button"
                      onClick={() => addToCart(item)}
                      type="button"
                    >
                      <span aria-hidden="true">+</span> Adicionar
                    </button>
                  </div>
                </div>
              </article>
            ))}
          </div>
          <p className="store-items-footnote">
            A entrega de itens na conta do jogo não está conectada nesta demonstração.
          </p>
        </section>

        <footer className="store-footer">
          <Link href="/">PLAYLAB</Link>
          <span>Catálogo demonstrativo · sem checkout ou pagamentos reais</span>
        </footer>
      </main>

      {isCartOpen && (
        <div className="store-cart-backdrop" onClick={() => setIsCartOpen(false)}>
          <section
            aria-labelledby="store-cart-title"
            aria-modal="true"
            className="store-cart-dialog"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
          >
            <div className="store-cart-heading">
              <div>
                <p className="store-eyebrow">PEDIDO DE TESTE</p>
                <h2 id="store-cart-title">Sua sacola</h2>
              </div>
              <button
                aria-label="Fechar sacola"
                className="store-cart-close"
                onClick={() => setIsCartOpen(false)}
                type="button"
              >
                ×
              </button>
            </div>

            {cart.length === 0 ? (
              <p className="store-cart-empty">
                Sua sacola está vazia. Explore os jogos e itens da vitrine.
              </p>
            ) : (
              <>
                <ul className="store-cart-list">
                  {cart.map((item) => (
                    <li className="store-cart-line" key={item.id}>
                      <div>
                        <strong>{item.title}</strong>
                        <span>
                          {item.quantity} × {formatPrice(item.price)}
                        </span>
                      </div>
                      <button
                        aria-label={`Remover ${item.title} da sacola`}
                        className="store-cart-remove"
                        onClick={() => removeFromCart(item.id)}
                        type="button"
                      >
                        Remover
                      </button>
                    </li>
                  ))}
                </ul>
                <div className="store-cart-total">
                  <span>Total demonstrativo</span>
                  <strong>{formatPrice(cartTotal)}</strong>
                </div>
              </>
            )}

            <p className="store-cart-disclaimer">
              Nenhum pagamento será realizado e nenhum conteúdo será concedido.
            </p>
            <button
              className="primary-button store-checkout-button"
              disabled={cart.length === 0}
              onClick={() =>
                setNotice(
                  "Checkout demonstrativo: nenhuma cobrança ou concessão foi realizada.",
                )
              }
              type="button"
            >
              Finalizar pedido de teste
            </button>
          </section>
        </div>
      )}
    </div>
  );
}
