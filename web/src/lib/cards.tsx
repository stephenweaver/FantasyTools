export type Category = 'ATTACK' | 'BOOST' | 'UNIQUE'
export type Card = { id: number; copyId?: string; serverId?: string; artworkUrl?: string; name: string; category: Category; amount: number; target: string; copy: string; icon: string }

/** The starter deck. Also what the sign-in screen fans out behind the form. */
export const cards: Card[] = [
  { id: 1, name: 'Crushing Blow', category: 'ATTACK', amount: -50, target: 'Opponent QB slot', copy: 'Cut the opposing starting QB slot score by 50%.', icon: '⚡' },
  { id: 2, name: 'End Zone Fever', category: 'BOOST', amount: 25, target: 'Your WR1 slot', copy: 'Boost your starting WR1 slot score by 25%.', icon: '🔥' },
  { id: 3, name: 'Lockdown', category: 'UNIQUE', amount: 50, target: 'Your QB slot', copy: 'Reduce attacks against your QB slot by 50%.', icon: '🛡' },
  { id: 4, name: 'Turf Monster', category: 'ATTACK', amount: -20, target: 'Opponent RB1 slot', copy: 'Cut the opposing starting RB1 slot score by 20%.', icon: '🕳' },
  { id: 5, name: 'Hail Mary', category: 'BOOST', amount: 8, target: 'Your team', copy: 'Add 8 points to your Chaos score.', icon: '🏈' },
  { id: 6, name: 'No Fly Zone', category: 'ATTACK', amount: -50, target: 'Opponent QB slot', copy: 'Reduce the opposing QB score by 50%.', icon: '✈' },
  { id: 7, name: 'Stiff Arm', category: 'BOOST', amount: 25, target: 'Your RB slot', copy: 'Increase your starting RB score by 25%.', icon: '💪' },
  { id: 8, name: 'Challenge Flag', category: 'UNIQUE', amount: 0, target: 'Opponent card', copy: 'After reveal, cancel one player-played opponent card.', icon: '🚩' },
]

export function ChaosCard({ card, selected, compact, onClick }: { card: Card; selected?: boolean; compact?: boolean; onClick?: () => void }) {
  return <button className={`chaos-card ${card.category.toLowerCase()} ${selected ? 'selected' : ''} ${compact ? 'compact' : ''}`} onClick={onClick} type="button">
    <div className="card-rarity">CHAOS • COMMON</div>
    <div className="card-art">{card.artworkUrl ? <img src={card.artworkUrl} alt={`${card.name} artwork`} /> : <span>{card.icon}</span>}<i /></div>
    <div className="card-name">{card.name}</div>
    <div className="card-category">{card.category}</div>
    {!compact && <><p>{card.copy}</p><div className="card-target">TARGET · {card.target}</div></>}
  </button>
}
