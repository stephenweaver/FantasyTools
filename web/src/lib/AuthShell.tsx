import type { ReactNode } from 'react'
import { cards, ChaosCard } from './cards'

/**
 * The signed-out frame: the Chaos Cards pitch on the left, whatever the page needs on the right.
 *
 * The wordmark is a div rather than an h1 on purpose. Every auth page states its own purpose in the
 * one heading it owns ("Sign in", "Check your inbox"), and a second heading here would make that
 * ambiguous for a screen reader -- and for the e2e suite, which addresses each page by its heading.
 */
export function AuthShell({ title, blurb, children }: { title: string; blurb?: string; children: ReactNode }) {
  return (
    <main className="login-screen">
      <div className="noise" />

      <section className="login-copy">
        <div className="eyebrow">FANTASY FOOTBALL, UNHINGED</div>
        <div className="wordmark">CHAOS<br /><span>CARDS</span></div>
        <p>Your fantasy league is already a battle. Now bring weapons.</p>
        <div className="login-cards">
          <ChaosCard card={cards[0]} compact />
          <ChaosCard card={cards[2]} compact />
          <ChaosCard card={cards[1]} compact />
        </div>
      </section>

      <section className="login-panel">
        <div className="logo-mark">CC</div>
        <h1>{title}</h1>
        {blurb && <p>{blurb}</p>}
        {children}
      </section>
    </main>
  )
}
