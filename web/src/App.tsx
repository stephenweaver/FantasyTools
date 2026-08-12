import { useEffect, useMemo, useState } from 'react'
import { apiFetch } from './lib/api'
import { useAuth } from './lib/auth'
import { cards, ChaosCard, type Card, type Category } from './lib/cards'

type Team = { id: number; manager: string; name: string; initials: string; record: string; score: number; chaos: number; hand: number; accent: string; sleeperUserId?: string }
type RosterAssignment = { rosterId: number; sleeperUserId: string; sleeperManagerName: string; sleeperTeamName: string; fantasyToolsUserId?: string; fantasyToolsEmail: string; fantasyToolsName?: string }
type CardStatus = 'IDEA' | 'ARTWORK READY' | 'NEEDS REVIEW' | 'ACTIVE' | 'ARCHIVED'
type UploadedCard = Card & { serverId?: string; artwork: string; rarity: string; copies: number; active: boolean; status: CardStatus; notes: string; updatedBy: string; updatedAt: string; effectType: string; special: boolean; sourcePlayer?: string; sourcePlayerId?: string; destinationSlot?: string; multiplier?: number }

const CHAOS_LEAGUE_ID = '1301750882777456640'
const fromServerCard = (card: any): UploadedCard => ({ id: Number(String(card.id).replace(/\D/g,'').slice(0,12)) || Date.now(), serverId: card.id, name: card.name, category: card.category, amount: card.amount, target: card.target, copy: card.officialDescription, icon: card.category === 'ATTACK' ? '⚡' : card.category === 'BOOST' ? '🔥' : '🛡', artwork: card.artworkUrl || '', rarity: card.rarity, copies: card.copies, active: card.status === 'ACTIVE', status: card.status, notes: card.commissionerNotes || '', updatedBy: card.updatedByName || 'Commissioner', updatedAt: card.updatedAt, effectType: card.effectType, special: card.isSpecial, sourcePlayer: card.sourcePlayer, sourcePlayerId: card.sourcePlayerId, destinationSlot: card.destinationSlot, multiplier: card.multiplier })
const toServerCard = (card: UploadedCard, submitForReview: boolean) => ({ name: card.name, category: card.category, rarity: card.rarity, isSpecial: card.special, artworkUrl: card.artwork, officialDescription: card.copy, commissionerNotes: card.notes, target: card.target, effectType: card.effectType, amount: card.amount, copies: card.copies, sourcePlayer: card.sourcePlayer, sourcePlayerId: card.sourcePlayerId, destinationSlot: card.destinationSlot, multiplier: card.multiplier, submitForReview })

const mockTeams: Team[] = [
  { id: 1, manager: 'Matthew', name: 'Gridiron Goblins', initials: 'GG', record: '5–2', score: 117.4, chaos: 124.7, hand: 5, accent: '#9cff57' },
  { id: 2, manager: 'Stephen', name: 'Sunday Scaries', initials: 'SS', record: '4–3', score: 113.8, chaos: 108.2, hand: 4, accent: '#ff4e79' },
  { id: 3, manager: 'Jordan', name: 'Fourth & Wrong', initials: 'FW', record: '6–1', score: 132.1, chaos: 139.6, hand: 3, accent: '#ffd452' },
  { id: 4, manager: 'Chris', name: 'Waiver Wiretap', initials: 'WW', record: '3–4', score: 126.7, chaos: 119.2, hand: 5, accent: '#54d6ff' },
  { id: 5, manager: 'Alex', name: 'Bench Warmers', initials: 'BW', record: '2–5', score: 98.4, chaos: 106.4, hand: 4, accent: '#b783ff' },
  { id: 6, manager: 'Taylor', name: 'Red Zone Renegades', initials: 'RR', record: '4–3', score: 105.8, chaos: 105.8, hand: 5, accent: '#ff9f43' },
  { id: 7, manager: 'Sam', name: 'Auto Drafted', initials: 'AD', record: '1–6', score: 89.3, chaos: 97.3, hand: 4, accent: '#56e3c2' },
  { id: 8, manager: 'Drew', name: 'Touchdown Town', initials: 'TT', record: '5–2', score: 119.6, chaos: 116.1, hand: 3, accent: '#fb7185' },
  { id: 9, manager: 'Casey', name: 'Purple Reign', initials: 'PR', record: '3–4', score: 110.2, chaos: 116.2, hand: 5, accent: '#a78bfa' },
  { id: 10, manager: 'Morgan', name: 'Bye Week Bandits', initials: 'BB', record: '2–5', score: 101.7, chaos: 95.7, hand: 4, accent: '#38bdf8' },
]

const initialLog = [
  ['2:41 PM', '🔥', 'Matthew revealed END ZONE FEVER on his WR1 slot.'],
  ['1:18 PM', '🛡', 'Stephen’s LOCKDOWN reduced an incoming QB attack by 50%.'],
  ['12:55 PM', '⚡', 'Stephen played CRUSHING BLOW against Matthew’s QB slot.'],
  ['THU', '👁', 'Pre-week cards revealed across the Chaos League.'],
]

function TeamBadge({ team, small }: { team: Team; small?: boolean }) {
  return <div className={`team-badge ${small ? 'small' : ''}`} style={{ '--team': team.accent } as React.CSSProperties}>{team.initials}</div>
}

function RosterAssignments({ teams, assignments, onSave, onRemove }: { teams: Team[]; assignments: RosterAssignment[]; onSave: (team: Team, email: string) => Promise<void>; onRemove: (team: Team) => Promise<void> }) {
  const [emails, setEmails] = useState<Record<number,string>>(() => Object.fromEntries(assignments.map(item => [item.rosterId,item.fantasyToolsEmail])))
  const [busy, setBusy] = useState<number | null>(null)
  const [message, setMessage] = useState('')
  useEffect(() => setEmails(Object.fromEntries(assignments.map(item => [item.rosterId,item.fantasyToolsEmail]))), [assignments])
  const save = async (team: Team) => {
    setBusy(team.id); setMessage('')
    try { await onSave(team, emails[team.id] || ''); setMessage(`${team.manager}'s roster is connected.`) }
    catch (error) { setMessage((error as Error).message) }
    finally { setBusy(null) }
  }
  const remove = async (team: Team) => {
    setBusy(team.id); setMessage('')
    try { await onRemove(team); setEmails(current => ({...current,[team.id]:''})); setMessage(`${team.manager}'s roster is unassigned.`) }
    catch (error) { setMessage((error as Error).message) }
    finally { setBusy(null) }
  }
  return <main className="access-page roster-page"><div className="admin-heading"><div><div className="eyebrow">PRIMARY COMMISSIONER SETUP</div><h1>Connect Player Accounts</h1><p>Match each verified FantasyTools account to exactly one Sleeper roster. Players will sign in with email and automatically enter their own team.</p></div><div className="roster-progress"><b>{assignments.length} / {teams.length}</b><span>ROSTERS CONNECTED</span></div></div>
    {message && <div className="setup-message">{message}</div>}
    <section className="roster-assignment-list">{teams.map(team => { const assignment=assignments.find(item=>item.rosterId===team.id); return <article className={assignment?'connected':''} key={team.id}>
      <TeamBadge team={team}/><div className="roster-identity"><span>SLEEPER ROSTER {team.id}</span><h2>{team.name}</h2><p>{team.manager}</p></div>
      <label>PLAYER'S FANTASYTOOLS EMAIL<input type="email" value={emails[team.id] || ''} onChange={event=>setEmails(current=>({...current,[team.id]:event.target.value}))} placeholder="player@example.com" /></label>
      <div className="roster-actions"><button className="primary" disabled={busy===team.id || !(emails[team.id] || '').trim()} onClick={()=>save(team)}>{busy===team.id?'SAVING…':assignment?'UPDATE CONNECTION':'CONNECT ACCOUNT'}</button>{assignment&&<button className="secondary" disabled={busy===team.id} onClick={()=>remove(team)}>REMOVE</button>}<small>{assignment?`✓ Connected to ${assignment.fantasyToolsName || assignment.fantasyToolsEmail}`:'Waiting for this player to create and verify an account'}</small></div>
    </article>})}</section>
  </main>
}

function CardCreator({ initialCard, onSave, onCancel }: { initialCard: UploadedCard | null; onSave: (card: UploadedCard) => Promise<void> | void; onCancel: () => void }) {
  const [name, setName] = useState(initialCard?.name || '')
  const [category, setCategory] = useState<Category>(initialCard?.category || 'ATTACK')
  const [description, setDescription] = useState(initialCard?.copy || '')
  const [target, setTarget] = useState(initialCard?.target || 'Opponent QB slot')
  const [rarity, setRarity] = useState(initialCard?.rarity || 'Common')
  const [effectType, setEffectType] = useState(initialCard?.effectType || 'Percentage decrease')
  const [amount, setAmount] = useState(initialCard?.amount ?? 25)
  const [copies, setCopies] = useState(initialCard?.copies ?? 4)
  const [special, setSpecial] = useState(initialCard?.special || false)
  const [sourcePlayer, setSourcePlayer] = useState(initialCard?.sourcePlayer || 'Patrick Mahomes')
  const [sourcePlayerId, setSourcePlayerId] = useState(initialCard?.sourcePlayerId || '4046')
  const [destinationSlot, setDestinationSlot] = useState(initialCard?.destinationSlot || 'Your starting QB slot')
  const [multiplier, setMultiplier] = useState(initialCard?.multiplier ?? 2)
  const [artwork, setArtwork] = useState(initialCard?.artwork || '')
  const [notes, setNotes] = useState(initialCard?.notes || '')
  const [error, setError] = useState('')
  const [uploading, setUploading] = useState(false)

  // The image goes to the images bucket first and the card stores only the URL it comes back with.
  // The endpoint requires a bearer token, so this is the point where an expired session shows up.
  const upload = async (file?: File) => {
    if (!file) return
    if (!['image/png','image/jpeg','image/webp'].includes(file.type)) { setError('Please choose a PNG, JPG, or WebP image.'); return }
    if (file.size > 8 * 1024 * 1024) { setError('Artwork must be smaller than 8 MB.'); return }
    const body = new FormData()
    body.append('file', file)
    setUploading(true); setError('')
    try { setArtwork((await apiFetch<{url:string}>('/api/images',{method:'POST',body})).url) }
    catch (ex) { setError((ex as Error).message) }
    finally { setUploading(false) }
  }

  const save = async (submitForReview: boolean) => {
    if (!name.trim() && !artwork) { setError('Add a working name or artwork before saving this draft.'); return }
    if (submitForReview && (!name.trim() || !description.trim() || !artwork)) { setError('A name, artwork, and complete game-rules summary are required before review.'); return }
    const status: CardStatus = submitForReview ? 'NEEDS REVIEW' : artwork ? 'ARTWORK READY' : 'IDEA'
    try {
      await onSave({ id: initialCard?.id || Date.now(), serverId: initialCard?.serverId, name: name.trim() || 'Untitled card idea', category, amount, target, copy: description.trim(), icon: category === 'ATTACK' ? '⚡' : category === 'BOOST' ? '🔥' : '🛡', artwork, rarity, copies, active: false, status, notes: notes.trim(), updatedBy: 'Matthew', updatedAt: new Date().toISOString(), effectType, special, ...(effectType === 'Referenced player replaces slot' ? { sourcePlayer, sourcePlayerId, destinationSlot, multiplier } : {}) })
    } catch (ex) { setError((ex as Error).message) }
  }

  return <main className="admin-page"><div className="admin-heading"><div><div className="eyebrow">SHARED COMMISSIONER WORKSPACE</div><h1>{initialCard ? 'Edit Card Draft' : 'Card Creator'}</h1><p>Save artwork and ideas now. Finish the engine rules together before approving the card for the deck.</p></div><button className="secondary" onClick={onCancel}>VIEW CARD LIBRARY →</button></div>
    <div className="creator-layout"><section className="creator-form">
      <div className="form-section"><h3>01 · CARD IDENTITY & SEARCH DATA</h3><div className="form-grid"><label className="wide">CARD NAME<input value={name} onChange={e=>setName(e.target.value)} placeholder="e.g. Crushing Blow" /></label><label>CATEGORY<select value={category} onChange={e=>setCategory(e.target.value as Category)}><option>ATTACK</option><option>BOOST</option><option>DEFENSE</option></select></label><label>RARITY<select value={rarity} onChange={e=>setRarity(e.target.value)}><option>Common</option><option>Uncommon</option><option>Rare</option><option>Legendary</option></select></label><label className="wide">RULES SUMMARY FOR THE GAME LOG<textarea value={description} onChange={e=>setDescription(e.target.value)} placeholder="Store the card effect as searchable text. This will not be printed over the artwork." /></label></div></div>
      <div className="form-section"><h3>02 · COMPLETE FINISHED CARD</h3><label className={`upload-zone ${artwork?'has-art':''}`}><input type="file" accept="image/png,image/jpeg,image/webp" disabled={uploading} onChange={e=>upload(e.target.files?.[0])}/>{uploading?<><b>⋯</b><strong>UPLOADING…</strong><span>SENDING THE IMAGE TO THE CARD IMAGE STORE</span></>:artwork?<><img src={artwork}/><span>CHOOSE A DIFFERENT FINISHED CARD</span></>:<><b>↑</b><strong>UPLOAD THE COMPLETE CARD IMAGE</strong><span>NAME, ARTWORK AND PRINTED DESCRIPTION SHOULD ALREADY BE INCLUDED</span></>}</label></div>
      <div className="form-section"><h3>03 · GAME RULES</h3><div className="form-grid"><label>TARGET TYPE<select value={target} onChange={e=>setTarget(e.target.value)}><option>Opponent QB slot</option><option>Opponent RB1 slot</option><option>Opponent WR1 slot</option><option>Your QB slot</option><option>Your WR1 slot</option><option>Your team</option><option>Opponent team</option><option>Dynamic target</option></select></label><label>EFFECT TYPE<select value={effectType} onChange={e=>setEffectType(e.target.value)}><option>Percentage decrease</option><option>Percentage increase</option><option>Add flat points</option><option>Subtract flat points</option><option>Block attack</option><option>Reduce attack</option><option>Referenced player replaces slot</option><option>Custom handler</option></select></label>{effectType === 'Referenced player replaces slot' ? <><label>SCORE SOURCE PLAYER<input value={sourcePlayer} onChange={e=>setSourcePlayer(e.target.value)} placeholder="Patrick Mahomes" /></label><label>SLEEPER PLAYER ID<input value={sourcePlayerId} onChange={e=>setSourcePlayerId(e.target.value)} placeholder="4046" /></label><label>DESTINATION SLOT<select value={destinationSlot} onChange={e=>setDestinationSlot(e.target.value)}><option>Your starting QB slot</option><option>Your starting RB1 slot</option><option>Your starting WR1 slot</option><option>Your starting TE slot</option><option>Your Flex slot</option></select></label><label>SCORE MULTIPLIER<input type="number" step="0.25" value={multiplier} onChange={e=>setMultiplier(Number(e.target.value))}/></label><div className="resolution-example wide"><b>RESOLUTION FORMULA</b><code>Chaos team score − actual slot points + ({sourcePlayer || 'source player'} points × {multiplier})</code><small>The source player does not need to be on the manager’s roster.</small></div></> : <label>EFFECT AMOUNT<input type="number" value={amount} onChange={e=>setAmount(Number(e.target.value))}/></label>}<label>COPIES IN DECK<input type="number" min="1" max="99" value={copies} onChange={e=>setCopies(Number(e.target.value))}/></label><label className="check wide"><input type="checkbox" checked={special} onChange={e=>setSpecial(e.target.checked)}/><span><b>SPECIAL CARD</b><small>Marks this card as unusual or powerful. Draw odds are unchanged.</small></span></label></div></div>
      <div className="form-section"><h3>COMMISSIONER NOTES</h3><div className="form-grid"><label className="wide">BRAINSTORMING, QUESTIONS, AND TODO ITEMS<textarea value={notes} onChange={e=>setNotes(e.target.value)} placeholder="Example: Confirm the timing. Stephen will finish the multiplier." /></label></div></div>
      {error && <div className="form-error">⚠ {error}</div>}<div className="draft-actions"><button className="secondary" disabled={uploading} onClick={()=>save(false)}>SAVE DRAFT</button><button className="primary save-card" disabled={uploading} onClick={()=>save(true)}>SUBMIT FOR REVIEW <span>→</span></button></div>
    </section><aside className="creator-preview"><div className="preview-label">FINISHED CARD PREVIEW</div><div className={`uploaded-preview final-card ${category.toLowerCase()}`}>{artwork?<img src={artwork}/>:<div className="empty-art"><span>{category==='ATTACK'?'⚡':category==='BOOST'?'🔥':'🛡'}</span><small>UPLOAD YOUR COMPLETE CARD</small></div>}</div><div className="engine-stats"><h3>GAME ENGINE STATS</h3><p><span>Card</span><b>{name || 'Not named yet'}</b></p><p><span>Category</span><b>{category}</b></p><p><span>Target</span><b>{target}</b></p><p><span>Effect</span><b>{effectType === 'Referenced player replaces slot' ? `${sourcePlayer} × ${multiplier} → ${destinationSlot}` : `${effectType} · ${amount}`}</b></p><p><span>Deck copies</span><b>{copies}</b></p></div><p className="preview-note">These stats are stored separately. They will never cover or alter your finished card image.</p></aside></div>
  </main>
}

function CardLibrary({ uploaded, onCreate, onDelete }: { uploaded: UploadedCard[]; onCreate: () => void; onDelete: (id: number) => void }) {
  return <main className="library-page"><div className="admin-heading"><div><div className="eyebrow">GLOBAL CHAOS DECK</div><h1>Card Library</h1><p>{cards.length + uploaded.length} designs · {uploaded.reduce((sum,c)=>sum+c.copies,0) + 20} physical cards in the shared deck</p></div><button className="primary create-button" onClick={onCreate}>+ CREATE NEW CARD</button></div>
    {uploaded.length===0 && <div className="empty-library"><span>🂠</span><h2>No uploaded cards yet</h2><p>Create your first finished Chaos Card and it will appear here.</p><button className="primary" onClick={onCreate}>CREATE FIRST CARD →</button></div>}
    <section className="library-grid">{uploaded.map(card=><article className={`library-card ${card.category.toLowerCase()}`} key={card.id}><div className="library-art"><img src={card.artwork}/><span>{card.rarity}{card.special?' · SPECIAL':''}</span><button className="delete-card" onClick={()=>onDelete(card.id)} aria-label={`Remove ${card.name}`}>×</button></div><div className="library-info"><div><b>{card.category}</b><em>{card.copies} COPIES</em></div><h2>{card.name}</h2><p>{card.copy}</p><small>{card.effectType === 'Referenced player replaces slot' ? `${card.sourcePlayer} × ${card.multiplier} replaces ${card.destinationSlot}` : `${card.effectType} · ${card.amount}${card.effectType.includes('Percentage')||card.effectType.includes('Reduce')?'%':' PTS'} · ${card.target}`}</small></div></article>)}</section>
  </main>
}

function SharedCardLibrary({ uploaded, onCreate, onEdit, onStatus, onDelete }: { uploaded: UploadedCard[]; onCreate: () => void; onEdit: (card: UploadedCard) => void; onStatus: (id: number, status: CardStatus) => void; onDelete: (id: number) => void }) {
  const active = uploaded.filter(card => card.status === 'ACTIVE')
  return <main className="library-page"><div className="admin-heading"><div><div className="eyebrow">SHARED CARD WORKSPACE</div><h1>Card Library</h1><p>{uploaded.length} commissioner designs · {active.reduce((sum,card)=>sum+card.copies,0)} approved copies active in the deck</p></div><button className="primary create-button" onClick={onCreate}>+ CREATE NEW DRAFT</button></div>
    {uploaded.length===0 && <div className="empty-library"><span>🂠</span><h2>No card drafts yet</h2><p>Upload artwork or save a card idea. The game rules can be completed later.</p><button className="primary" onClick={onCreate}>CREATE FIRST DRAFT →</button></div>}
    <section className="library-grid">{uploaded.map(card=><article className={`library-card ${card.category.toLowerCase()}`} key={card.id}><div className="library-art">{card.artwork?<img src={card.artwork}/>:<div className="draft-art-placeholder">ARTWORK<br/>NOT UPLOADED</div>}<span>{card.rarity}{card.special?' · SPECIAL':''}</span><button className="delete-card" onClick={()=>onDelete(card.id)} aria-label={`Remove ${card.name}`}>×</button></div><div className="library-info"><div><b>{card.category}</b><em className={`status-pill status-${card.status.toLowerCase().replaceAll(' ','-')}`}>{card.status}</em></div><h2>{card.name}</h2><p>{card.copy || 'Game-engine rules have not been completed yet.'}</p>{card.notes&&<p className="card-notes">NOTES · {card.notes}</p>}<small>{card.effectType === 'Referenced player replaces slot' ? `${card.sourcePlayer} × ${card.multiplier} replaces ${card.destinationSlot}` : `${card.effectType} · ${card.amount} · ${card.target}`}</small><div className="library-actions"><button onClick={()=>onEdit(card)}>EDIT DRAFT</button>{card.status==='NEEDS REVIEW'&&<button className="approve" onClick={()=>onStatus(card.id,'ACTIVE')}>APPROVE & ADD TO DECK</button>}{card.status==='ACTIVE'&&<button onClick={()=>onStatus(card.id,'ARCHIVED')}>REMOVE FROM DECK</button>}{card.status==='ARCHIVED'&&<button onClick={()=>onStatus(card.id,'NEEDS REVIEW')}>RETURN TO REVIEW</button>}</div><div className="updated-by">LAST EDITED BY {card.updatedBy || 'COMMISSIONER'}</div></div></article>)}</section>
  </main>
}

const commissionerPermissions = [
  ['create_card_drafts','Create card drafts','Upload artwork and save incomplete ideas.'],
  ['edit_card_rules','Edit card rules','Finish engine statistics and commissioner notes.'],
  ['approve_cards','Approve cards','Approve reviewed cards before they enter the deck.'],
  ['manage_deck','Manage deck quantities','Change the number of card copies in the shared deck.'],
  ['invite_managers','Invite managers','Send league invitations and resend them.'],
  ['assign_rosters','Assign Sleeper rosters','Connect Chaos accounts to Sleeper teams.'],
  ['manage_deadlines','Manage deadlines','Set the weekly selection deadline.'],
  ['lock_weeks','Lock and unlock weeks','Control selection locks and Thursday reveals.'],
  ['correct_scores','Correct scores','Apply explained commissioner score corrections.'],
  ['view_private_hands','View private hands','Troubleshoot player hands. This is sensitive access.'],
  ['manage_co_commissioners','Manage co-commissioners','Grant permissions to other managers. Primary only.'],
] as const

function CommissionerAccess({ teams, grants, onChange }: { teams: Team[]; grants: Record<number,string[]>; onChange: (next: Record<number,string[]>) => void }) {
  const [selectedTeam, setSelectedTeam] = useState(teams[1]?.id || teams[0].id)
  const selected = teams.find(team=>team.id===selectedTeam) || teams[0]
  const selectedGrants = grants[selected.id] || []
  const toggle = (permission: string) => {
    if (permission === 'manage_co_commissioners') return
    const current = grants[selected.id] || []
    onChange({...grants,[selected.id]:current.includes(permission)?current.filter(item=>item!==permission):[...current,permission]})
  }
  return <main className="access-page"><div className="admin-heading"><div><div className="eyebrow">PRIMARY COMMISSIONER ONLY</div><h1>Commissioner Access</h1><p>Give trusted managers only the tools they need. Permission changes are permanently logged.</p></div><div className="primary-owner"><TeamBadge team={teams[0]} small/><span>PRIMARY COMMISSIONER<b>{teams[0].manager}</b></span></div></div>
    <div className="access-layout"><aside className="manager-list"><h3>LEAGUE MANAGERS</h3>{teams.map((team,index)=><button className={selected.id===team.id?'selected':''} onClick={()=>setSelectedTeam(team.id)} key={team.id}><TeamBadge team={team} small/><span><b>{team.manager}</b><small>{team.name}</small></span>{index===0?<em>OWNER</em>:(grants[team.id]?.length||0)>0?<em className="co">CO-COMMISSIONER</em>:null}</button>)}</aside>
      <section className="permission-panel"><div className="permission-person"><TeamBadge team={selected}/><div><span>EDITING ACCESS FOR</span><h2>{selected.manager}</h2><p>{selected.name}</p></div>{selected.id===teams[0].id&&<b>PRIMARY COMMISSIONER · FULL ACCESS</b>}</div>
        <div className="permission-list">{commissionerPermissions.map(([key,title,description])=>{const primary=selected.id===teams[0].id;const primaryOnly=key==='manage_co_commissioners';const checked=primary||selectedGrants.includes(key);return <label className={`${checked?'granted':''} ${primaryOnly&&!primary?'locked-permission':''}`} key={key}><input type="checkbox" checked={checked} disabled={primary||primaryOnly} onChange={()=>toggle(key)}/><span><b>{title}</b><small>{description}</small></span>{primaryOnly&&!primary&&<em>PRIMARY ONLY</em>}</label>})}</div>
        {selected.id!==teams[0].id&&<div className="access-summary"><b>{selectedGrants.length?`${selected.manager} IS A CO-COMMISSIONER`:`${selected.manager} IS A REGULAR PLAYER`}</b><span>{selectedGrants.length?`${selectedGrants.length} permission${selectedGrants.length===1?'':'s'} granted`:'No administrative access'}</span></div>}
      </section></div>
  </main>
}

export default function App() {
  // Only ever rendered behind the auth gate in main.tsx, so there is no signed-out screen here.
  const { user: account, logout } = useAuth()
  const [screen, setScreen] = useState<'room' | 'battle' | 'admin' | 'library' | 'permissions' | 'rosters'>('room')
  const [selected, setSelected] = useState<number[]>([])
  const [played, setPlayed] = useState<Card[]>([])
  const [pending, setPending] = useState<Card[]>([])
  const [liveCardPlayed, setLiveCardPlayed] = useState(false)
  const [log, setLog] = useState(initialLog)
  const [targeting, setTargeting] = useState(false)
  const [inspecting, setInspecting] = useState<Card | null>(null)
  const [toast, setToast] = useState('')
  const [revealed, setRevealed] = useState(false)
  const [activeNav, setActiveNav] = useState('League Room')
  const [teamData, setTeamData] = useState<Team[]>(mockTeams)
  const [leagueName, setLeagueName] = useState('THE CHAOS LEAGUE')
  const [sleeperStatus, setSleeperStatus] = useState<'loading'|'live'|'fallback'>('loading')
  const [uploadedCards, setUploadedCards] = useState<UploadedCard[]>(() => { try { return JSON.parse(localStorage.getItem('chaos-uploaded-cards') || '[]').map((card: UploadedCard) => ({...card,status:card.status || (card.active?'ACTIVE':'ARTWORK READY'),notes:card.notes || '',updatedBy:card.updatedBy || 'Commissioner',updatedAt:card.updatedAt || new Date().toISOString()})) } catch { return [] } })
  const [editingCard, setEditingCard] = useState<UploadedCard | null>(null)
  const [permissionGrants, setPermissionGrants] = useState<Record<number,string[]>>(() => { try { return JSON.parse(localStorage.getItem('chaos-permission-grants') || '{}') } catch { return {} } })
  const [rosterAssignments, setRosterAssignments] = useState<RosterAssignment[]>(() => { try { return JSON.parse(localStorage.getItem('chaos-roster-assignments') || '[]') } catch { return [] } })
  const home = teamData[0], away = teamData[1]

  useEffect(() => {
    const leagueId = '1301750882777456640'
    Promise.all([
      fetch(`https://api.sleeper.app/v1/league/${leagueId}`).then(r => { if (!r.ok) throw new Error('League unavailable'); return r.json() }),
      fetch(`https://api.sleeper.app/v1/league/${leagueId}/users`).then(r => r.json()),
      fetch(`https://api.sleeper.app/v1/league/${leagueId}/rosters`).then(r => r.json()),
    ]).then(([league, users, rosters]) => {
      const imported = users.map((user: { user_id: string; display_name?: string; username?: string; metadata?: { team_name?: string } }, index: number) => {
        const manager = user.display_name || user.username || `Manager ${index + 1}`
        const name = user.metadata?.team_name || `${manager}'s Team`
        const roster = rosters.find((item: { owner_id?: string }) => item.owner_id === user.user_id)
        const visual = mockTeams[index % mockTeams.length]
        return { ...visual, id: roster?.roster_id || index + 1, sleeperUserId: user.user_id, manager, name, initials: name.split(/\s+/).map((word: string) => word[0]).join('').slice(0, 2).toUpperCase() }
      })
      if (imported.length >= 2) setTeamData(imported)
      setLeagueName(league.name || 'SLEEPER LEAGUE')
      setSleeperStatus('live')
    }).catch(() => setSleeperStatus('fallback'))
  }, [])

  useEffect(() => {
    apiFetch<{cards: any[]}>(`/api/leagues/${CHAOS_LEAGUE_ID}/cards`)
      .then(workspace => {
        const shared = workspace.cards.map(fromServerCard)
        setUploadedCards(shared)
        localStorage.setItem('chaos-uploaded-cards',JSON.stringify(shared))
      })
      .catch(() => { /* The first saved draft creates the workspace. */ })
    apiFetch<{assignments:RosterAssignment[]}>(`/api/leagues/${CHAOS_LEAGUE_ID}/rosters`)
      .then(workspace => { setRosterAssignments(workspace.assignments); localStorage.setItem('chaos-roster-assignments',JSON.stringify(workspace.assignments)) })
      .catch(() => { /* The commissioner starts setup from the roster screen. */ })
  }, [])

  // The server is the record for every card mutation -- localStorage only mirrors the answer so a
  // reload paints something before the workspace request returns.
  const saveUploadedCard = async (card: UploadedCard) => {
    const path = card.serverId ? `/api/leagues/${CHAOS_LEAGUE_ID}/cards/${card.serverId}` : `/api/leagues/${CHAOS_LEAGUE_ID}/cards`
    const saved = fromServerCard(await apiFetch<any>(path,{method:card.serverId?'PUT':'POST',body:JSON.stringify(toServerCard(card,card.status==='NEEDS REVIEW'))}))
    const next = uploadedCards.some(existing=>existing.id===card.id) ? uploadedCards.map(existing=>existing.id===card.id?saved:existing) : [...uploadedCards, saved]
    localStorage.setItem('chaos-uploaded-cards', JSON.stringify(next)); setUploadedCards(next); setScreen('library'); setActiveNav('Card Library')
  }
  const updateCardStatus = async (id: number, status: CardStatus) => {
    const current = uploadedCards.find(card=>card.id===id)
    if (!current?.serverId) return
    const saved = fromServerCard(await apiFetch<any>(`/api/leagues/${CHAOS_LEAGUE_ID}/cards/${current.serverId}/status`,{method:'POST',body:JSON.stringify({status})}))
    const next = uploadedCards.map(card=>card.id===id?saved:card)
    localStorage.setItem('chaos-uploaded-cards',JSON.stringify(next)); setUploadedCards(next)
  }
  const deleteUploadedCard = (id: number) => {
    const next = uploadedCards.filter(card => card.id !== id)
    localStorage.setItem('chaos-uploaded-cards', JSON.stringify(next))
    setUploadedCards(next)
  }
  const updatePermissionGrants = (next: Record<number,string[]>) => {
    localStorage.setItem('chaos-permission-grants',JSON.stringify(next)); setPermissionGrants(next)
  }
  const saveRosterAssignment = async (team: Team, email: string) => {
    const requested: RosterAssignment = { rosterId:team.id, sleeperUserId:team.sleeperUserId || String(team.id), sleeperManagerName:team.manager, sleeperTeamName:team.name, fantasyToolsEmail:email.trim(), fantasyToolsName:email.trim() }
    const saved = await apiFetch<RosterAssignment>(`/api/leagues/${CHAOS_LEAGUE_ID}/rosters/${team.id}`,{method:'PUT',body:JSON.stringify(requested)})
    const next = [...rosterAssignments.filter(item=>item.rosterId!==team.id && item.fantasyToolsUserId!==saved.fantasyToolsUserId),saved]
    setRosterAssignments(next); localStorage.setItem('chaos-roster-assignments',JSON.stringify(next))
  }
  const removeRosterAssignment = async (team: Team) => {
    await apiFetch(`/api/leagues/${CHAOS_LEAGUE_ID}/rosters/${team.id}`,{method:'DELETE'})
    const next=rosterAssignments.filter(item=>item.rosterId!==team.id); setRosterAssignments(next); localStorage.setItem('chaos-roster-assignments',JSON.stringify(next))
  }

  const chaos = useMemo(() => {
    let score = home.score
    if (played.some(c => c.id === 5)) score += 8
    if (played.some(c => c.id === 2)) score += 6.2
    if (played.some(c => c.id === 1)) score -= 7.1
    return score
  }, [played])

  const selectCard = (id: number) => {
    if (played.some(c => c.id === id)) return
    setSelected(current => current.includes(id) ? current.filter(x => x !== id) : current.length < 2 ? [...current, id] : current)
  }

  const chooseInspectedCard = () => {
    if (!inspecting) return
    if (revealed && liveCardPlayed) { setToast('Your one live card has already been played this week.'); setInspecting(null); return }
    if (!revealed && pending.length >= 2) { setToast('You can select at most two pre-week cards.'); setInspecting(null); return }
    setSelected([inspecting.id])
    setInspecting(null)
    setTargeting(true)
  }

  const playLive = () => {
    if (selected.length !== 1) { setToast('Choose one card from your hand first.'); return }
    setTargeting(true)
  }

  const confirmPlay = () => {
    const card = cards.find(c => c.id === selected[0])!
    if (revealed) {
      if (liveCardPlayed) { setTargeting(false); setSelected([]); setToast('Your one live card has already been played this week.'); return }
      setPlayed(p => [...p, card]); setLiveCardPlayed(true)
      setLog(l => [[new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }), card.icon, `${home.manager} played ${card.name.toUpperCase()} live against ${away.manager}’s QB slot.`], ...l])
      setToast(`${card.name} slammed onto the table and is now locked!`)
    } else {
      if (pending.length >= 2) { setTargeting(false); setSelected([]); setToast('You can select at most two pre-week cards.'); return }
      setPending(p => [...p, card])
      setLog(l => [[new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }), '🔒', `${card.name.toUpperCase()} was selected privately. It can be changed before the deadline.`], ...l])
      setToast(`${card.name} selected privately. You can still return it to your hand.`)
    }
    setSelected([]); setTargeting(false)
    setTimeout(() => setToast(''), 2800)
  }

  const returnToHand = (card: Card) => {
    if (revealed) return
    setPending(current => current.filter(item => item.id !== card.id))
    setLog(l => [[new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }), '↩', `${card.name.toUpperCase()} was returned to the private hand before lock.`], ...l])
    setToast(`${card.name} returned to your hand.`); setTimeout(() => setToast(''), 2200)
  }

  const toggleReveal = () => {
    if (!revealed) {
      setPlayed(current => [...current, ...pending]); setPending([]); setRevealed(true)
      setLog(l => [['NOW', '👁', 'Pre-week selections locked and revealed across the league.'], ...l])
    } else {
      setRevealed(false); setPending([]); setLiveCardPlayed(false); setPlayed([])
    }
  }

  return <div className="app-shell">
    <header><button className="brand" onClick={() => {setScreen('room');setActiveNav('League Room')}}><b>CC</b><span>CHAOS CARDS<small>FANTASY FOOTBALL</small></span></button><nav>{['League Room','Standings','Card Library','History'].map(n => <button className={activeNav===n?'active':''} onClick={() => {setActiveNav(n); setScreen(n==='Card Library'?'library':'room')}} key={n}>{n}</button>)}</nav><div className="admin-actions"><button className="admin-link roster-link" onClick={()=>{setScreen('rosters');setActiveNav('')}}>⇄ ROSTERS</button><button className="admin-link commissioner-link" onClick={()=>{setScreen('permissions');setActiveNav('')}}>♛ COMMISSIONERS</button><button className="admin-link creator-link" onClick={()=>{setScreen('admin');setActiveNav('')}}>⚙ CARD CREATOR</button><button className="admin-link signout-link" title={account?.email} onClick={logout}>Sign out</button></div><div className="week"><span>WEEK 1</span><b className={revealed?'':'preweek'}>{revealed?'REVEALED':'PRE-WEEK'}</b></div><TeamBadge team={home} small /></header>
    {screen === 'rosters' ? <RosterAssignments teams={teamData} assignments={rosterAssignments} onSave={saveRosterAssignment} onRemove={removeRosterAssignment}/> : screen === 'permissions' ? <CommissionerAccess teams={teamData} grants={permissionGrants} onChange={updatePermissionGrants}/> : screen === 'admin' ? <CardCreator initialCard={editingCard} onSave={saveUploadedCard} onCancel={()=>{setEditingCard(null);setScreen('library');setActiveNav('Card Library')}}/> : screen === 'library' ? <SharedCardLibrary uploaded={uploadedCards} onCreate={()=>{setEditingCard(null);setScreen('admin');setActiveNav('')}} onEdit={card=>{setEditingCard(card);setScreen('admin');setActiveNav('')}} onStatus={updateCardStatus} onDelete={deleteUploadedCard}/> : screen === 'room' ? <main className="room">
      <section className="room-hero"><div><div className="eyebrow">{leagueName} · WEEK 1 DEMO</div><h1>League Room</h1><p>{sleeperStatus === 'live' ? 'REAL SLEEPER TEAMS · DEMO MATCHUPS AND SCORES' : sleeperStatus === 'loading' ? 'IMPORTING YOUR SLEEPER MANAGERS…' : 'SLEEPER IMPORT BLOCKED HERE · SHOWING DEMO NAMES'}</p></div><div className="deadline"><span>THURSDAY CARD LOCK</span><strong>{revealed ? 'CARDS REVEALED' : '02 : 14 : 36'}</strong><button onClick={toggleReveal}>{revealed ? 'RESET TO PRE-WEEK' : 'COMMISSIONER: LOCK & REVEAL'}</button></div></section>
      <div className="ticker"><b>LIVE CHAOS</b><span>⚡ Stephen played CRUSHING BLOW</span><span>•</span><span>Jordan’s score jumps +7.5</span><span>•</span><span>🛡 LOCKDOWN activated</span></div>
      <section className="matchup-grid">{[0,2,4,6,8].map((i, index) => { const a=teamData[i], b=teamData[i+1]; if (!a || !b) return null; return <article className={`matchup ${index===0?'featured':''}`} key={a.id} onClick={() => index===0 && setScreen('battle')}>
        <div className="matchup-top"><span>{index===0?'🔥 FEATURED BATTLE':`MATCHUP ${index+1}`}</span><b>{index < 2 ? 'LIVE' : 'SUN 4:25'}</b></div>
        <div className="versus-row"><div><TeamBadge team={a}/><strong>{a.name}</strong><small>{a.manager} · {a.record}</small></div><div className="scores"><span>{a.score.toFixed(1)} <i>SLEEPER</i></span><b>{a.chaos.toFixed(1)}</b><em>VS</em><b>{b.chaos.toFixed(1)}</b><span>{b.score.toFixed(1)} <i>SLEEPER</i></span></div><div><TeamBadge team={b}/><strong>{b.name}</strong><small>{b.manager} · {b.record}</small></div></div>
        <div className="public-cards"><span>HAND {a.hand} 🂠</span><div>{revealed ? <><i className="mini attack">⚡</i><i className="mini boost">🔥</i></> : '2 HIDDEN'}</div><span>🂠 {b.hand} HAND</span></div>
        {index===0 && <button className="spectate">ENTER BATTLE →</button>}
      </article>})}</section>
    </main> : <main className="battle">
      <button className="back" onClick={() => setScreen('room')}>← BACK TO LEAGUE ROOM</button>
      <section className="battle-board">
        <div className="combatant left"><TeamBadge team={home}/><div><small>{home.manager} · {home.record}</small><h2>{home.name}</h2></div><div className="score-block"><span>SLEEPER {home.score.toFixed(1)}</span><strong>{chaos.toFixed(1)}</strong><b>CHAOS SCORE</b></div></div>
        <div className="vs-burst">VS<small>WEEK 1 · {revealed?'REVEALED':'PRE-WEEK'}</small></div>
        <div className="combatant right"><div className="score-block"><span>SLEEPER {away.score.toFixed(1)}</span><strong>{away.chaos.toFixed(1)}</strong><b>CHAOS SCORE</b></div><div><small>{away.manager} · {away.record}</small><h2>{away.name}</h2></div><TeamBadge team={away}/></div>
        <div className="field">
          <div className="effect-zone"><span>YOUR ACTIVE EFFECTS · LOCKED</span>{played.length?played.map(c => <ChaosCard key={c.id} card={c} compact />):<div className="empty-effects">NO LOCKED EFFECTS</div>}</div>
          <div className="calculation"><span>CHAOS CALCULATION</span><div><p>Sleeper score <b>{home.score.toFixed(2)}</b></p>{played.some(c=>c.id===2)&&<p className="positive">End Zone Fever <b>+6.20</b></p>}{played.some(c=>c.id===5)&&<p className="positive">Hail Mary <b>+8.00</b></p>}{played.some(c=>c.id===1)&&<p className="negative">Crushing Blow <b>−7.10</b></p>}{!played.length&&<p className="waiting-math">Waiting for Thursday reveal…</p>}<strong>CHAOS SCORE <b>{chaos.toFixed(2)}</b></strong></div></div>
          <div className="effect-zone enemy"><span>OPPONENT EFFECTS</span>{revealed?<><ChaosCard card={cards[0]} compact/><ChaosCard card={cards[2]} compact/></>:<><div className="card-back">?</div><div className="card-back">?</div></>}</div>
        </div>
      </section>
      <section className="battle-lower"><div className="log"><div className="section-title"><h3>GAME LOG</h3><span>ALL ACTIONS ARE PERMANENT</span></div>{log.map((x,i)=><div className="log-row" key={i}><time>{x[0]}</time><i>{x[1]}</i><p>{x[2]}</p></div>)}</div><aside>{!revealed && <div className="pending-zone"><div className="section-title"><h3>SECRET PRE-WEEK SELECTIONS</h3><span>{pending.length} / 2 · UNLOCKED</span></div>{pending.length===0?<p>No cards selected. Choose up to two from your hand.</p>:<div className="pending-list">{pending.map(card=><div key={card.id}><ChaosCard card={card} compact/><button onClick={()=>returnToHand(card)}>↩ RETURN TO HAND</button></div>)}</div>}</div>}<div className="section-title"><h3>YOUR HAND</h3><span>{cards.filter(c=>!played.some(p=>p.id===c.id)&&!pending.some(p=>p.id===c.id)).length} CARDS · {revealed ? liveCardPlayed ? 'LIVE PLAY USED' : '1 LIVE PLAY AVAILABLE' : `${2-pending.length} PRE-WEEK PICKS LEFT`}</span></div><div className="hand">{cards.filter(c=>!played.some(p=>p.id===c.id)&&!pending.some(p=>p.id===c.id)).map(c=><ChaosCard key={c.id} card={c} selected={selected.includes(c.id)} onClick={()=>setInspecting(c)}/>)}</div>{revealed && <div className={`play-status ${liveCardPlayed?'used':''}`}>{liveCardPlayed?'✓ LIVE CARD USED · REMAINING CARDS STAY IN YOUR HAND':'⚡ SELECT ONE CARD FOR YOUR LIVE PLAY'}</div>}</aside></section>
      {inspecting && <div className="modal-backdrop inspect-backdrop" onClick={()=>setInspecting(null)}><div className={`inspect-modal ${inspecting.category.toLowerCase()}`} onClick={event=>event.stopPropagation()}>
        <button className="modal-close" onClick={()=>setInspecting(null)} aria-label="Close card view">×</button>
        <div className="inspect-art"><span>{inspecting.icon}</span><div className="art-caption">PLACEHOLDER ARTWORK</div></div>
        <div className="inspect-details"><div className="eyebrow">{inspecting.category} · COMMON</div><h2>{inspecting.name}</h2><p>{inspecting.copy}</p><div className="inspect-rule"><span>TARGET</span><strong>{inspecting.target}</strong></div><div className="inspect-rule"><span>TIMING</span><strong>Pre-week or live play</strong></div><button className="primary inspect-play" onClick={chooseInspectedCard}>PLAY THIS CARD <span>⚡</span></button><button className="keep-card" onClick={()=>setInspecting(null)}>KEEP IT IN MY HAND</button></div>
      </div></div>}
      {targeting && <div className="modal-backdrop"><div className="target-modal"><div className="eyebrow">SELECT A TARGET</div><h2>Where should the chaos land?</h2><p>This effect follows the lineup slot—even if Stephen changes players later.</p>{['Starting QB · Lamar Jackson','Starting RB1 · Bijan Robinson','Starting WR1 · CeeDee Lamb','Entire Team'].map((t,i)=><button onClick={confirmPlay} key={t}><span>{i===0?'QB':i===1?'RB':'◎'}</span>{t}<b>→</b></button>)}<button className="cancel" onClick={()=>setTargeting(false)}>CANCEL</button></div></div>}
      {toast && <div className="toast">⚡ {toast}</div>}
    </main>}
  </div>
}
