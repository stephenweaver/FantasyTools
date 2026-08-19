import { useEffect, useMemo, useState } from 'react'
import { apiFetch } from './lib/api'
import { useAuth } from './lib/auth'
import { cards, ChaosCard, type Card, type Category } from './lib/cards'
import { cardDataIdeas, toDraftRequest } from './lib/cardDataIdeas'
import { WeeklyCardSeason, type WeeklyCard } from './lib/weeklyCards'

type Team = { id: number; manager: string; name: string; initials: string; record: string; score: number; chaos: number; hand: number; accent: string; sleeperUserId?: string }
type LineupPlayer = { name: string; position: string; nflTeam: string; opponent: string; game: string; projection: number; points: number; stats: string; status?: string }
type RosterAssignment = { rosterId: number; sleeperUserId: string; sleeperManagerName: string; sleeperTeamName: string; fantasyToolsUserId?: string; fantasyToolsEmail: string; fantasyToolsName?: string }
type RosterClaim = { id: string; rosterId: number; sleeperManagerName: string; sleeperTeamName: string; fantasyToolsName: string; fantasyToolsEmail: string; status: string }
type RosterWorkspace = { primaryCommissionerUserId: string; assignments: RosterAssignment[]; claims: RosterClaim[] }
type CardWorkspaceAccess = { cards: any[]; weeklyCards?: WeeklyCard[]; collaborators?: { userId:string; permissions:string[] }[] }
type ChaosLeague = { leagueId: string; sleeperLeagueId: string; name: string; primaryCommissionerUserId: string; createdAt: string }
type CardStatus = 'IDEA' | 'ARTWORK READY' | 'NEEDS REVIEW' | 'ACTIVE' | 'ARCHIVED'
type UploadedCard = Card & { serverId?: string; artwork: string; rarity: string; copies: number; active: boolean; status: CardStatus; notes: string; updatedBy: string; updatedAt: string; effectType: string; special: boolean; sourcePlayer?: string; sourcePlayerId?: string; destinationSlot?: string; multiplier?: number }
type SyncedPlayer = { playerId:string; name:string; position:string; nflTeam:string; starter:boolean; points:number }
type SyncedTeam = { rosterId:number; ownerId:string; managerName:string; teamName:string; wins:number; losses:number; players:SyncedPlayer[] }
type SyncedMatchup = { week:number; matchupId:number; rosterId:number; points:number; starters:string[]; playerPoints:Record<string,number> }
type DealtCard = { copyId:string; cardId:string; name:string; category:Category; artworkUrl:string; description:string; target:string }
type SavedSelection = { copyId:string; cardId:string; category:Category; targetRosterId?:string; targetPlayerId?:string; targetSlot?:string; cancelledCopyId?:string }
type ChaosScore = { rosterId:number; sleeperScore:number; chaosScore:number; lines:{description:string;change:number}[] }
type GameWeek = { week:number; status:string; deadlineUtc?:string; sleeperStatus:string; team?:SyncedTeam; hand:DealtCard[]; selections:SavedSelection[]; publicSelections?:{rosterId:number;selections:SavedSelection[]}[]; canDraw?:boolean; cardsNeeded?:number; teams?:SyncedTeam[]; matchups:SyncedMatchup[]; chaosScores?:ChaosScore[] }

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

const playerPool: LineupPlayer[] = [
  { name:'Joe Burrow',position:'QB',nflTeam:'CIN',opponent:'CLE',game:'SUN 1:00 PM',projection:21.55,points:24.82,stats:'286 pass yds · 2 pass TD · 18 rush yds' },
  { name:'Christian McCaffrey',position:'RB',nflTeam:'SF',opponent:'SEA',game:'THU 8:15 PM',projection:19.59,points:17.40,stats:'82 rush yds · 5 rec · 38 rec yds' },
  { name:'Chase Brown',position:'RB',nflTeam:'CIN',opponent:'CLE',game:'SUN 1:00 PM',projection:16.72,points:14.60,stats:'68 rush yds · 4 rec · 32 rec yds' },
  { name:'Chris Olave',position:'WR',nflTeam:'NO',opponent:'ATL',game:'SUN 4:25 PM',projection:14.75,points:18.10,stats:'7 rec · 101 rec yds · 1 TD' },
  { name:'Garrett Wilson',position:'WR',nflTeam:'NYJ',opponent:'BUF',game:'SUN 1:00 PM',projection:13.54,points:12.30,stats:'6 rec · 63 rec yds' },
  { name:'Trey McBride',position:'TE',nflTeam:'ARI',opponent:'LAR',game:'SUN 4:05 PM',projection:12.10,points:15.70,stats:'8 rec · 77 rec yds' },
  { name:'DeVonta Smith',position:'FLEX',nflTeam:'PHI',opponent:'DAL',game:'SUN 8:20 PM',projection:13.20,points:11.90,stats:'5 rec · 69 rec yds' },
  { name:'Brandon Aubrey',position:'K',nflTeam:'DAL',opponent:'PHI',game:'SUN 8:20 PM',projection:8.80,points:10.00,stats:'2 FG · 4 XP' },
  { name:'Jalen Hurts',position:'QB',nflTeam:'PHI',opponent:'DAL',game:'SUN 8:20 PM',projection:21.17,points:22.44,stats:'241 pass yds · 2 pass TD · 41 rush yds' },
  { name:'Bucky Irving',position:'RB',nflTeam:'TB',opponent:'ATL',game:'SUN 1:00 PM',projection:12.18,points:16.20,stats:'91 rush yds · 3 rec · 29 rec yds' },
  { name:'Bijan Robinson',position:'RB',nflTeam:'ATL',opponent:'TB',game:'SUN 1:00 PM',projection:18.90,points:20.70,stats:'106 rush yds · 4 rec · 34 rec yds' },
  { name:'CeeDee Lamb',position:'WR',nflTeam:'DAL',opponent:'PHI',game:'SUN 8:20 PM',projection:17.15,points:19.80,stats:'8 rec · 108 rec yds' },
  { name:'DJ Moore',position:'WR',nflTeam:'CHI',opponent:'MIN',game:'MON 8:15 PM',projection:12.11,points:9.60,stats:'5 rec · 46 rec yds' },
  { name:'Sam LaPorta',position:'TE',nflTeam:'DET',opponent:'GB',game:'SUN 4:25 PM',projection:10.80,points:13.40,stats:'6 rec · 74 rec yds' },
  { name:'James Cook',position:'FLEX',nflTeam:'BUF',opponent:'NYJ',game:'SUN 1:00 PM',projection:14.30,points:18.60,stats:'88 rush yds · 1 TD · 3 rec' },
  { name:'Jake Elliott',position:'K',nflTeam:'PHI',opponent:'DAL',game:'SUN 8:20 PM',projection:8.10,points:9.00,stats:'2 FG · 3 XP' },
]

const lineupFor = (teamId: number, side: number) => Array.from({length:8},(_,index) => ({...playerPool[(teamId * 3 + side * 8 + index) % playerPool.length], status:index < 5 ? 'FINAL' : 'YET TO PLAY'}))

const initialLog = [
  ['2:41 PM', '🔥', 'Matthew revealed END ZONE FEVER on his WR1 slot.'],
  ['1:18 PM', '🛡', 'Stephen’s LOCKDOWN reduced an incoming QB attack by 50%.'],
  ['12:55 PM', '⚡', 'Stephen played CRUSHING BLOW against Matthew’s QB slot.'],
  ['THU', '👁', 'Pre-week cards revealed across the Chaos League.'],
]

function TeamBadge({ team, small }: { team: Team; small?: boolean }) {
  return <div className={`team-badge ${small ? 'small' : ''}`} style={{ '--team': team.accent } as React.CSSProperties}>{team.initials}</div>
}

function LineupComparison({ home, away, week, left = [], right = [] }: { home: Team; away: Team; week:number; left?:LineupPlayer[]; right?:LineupPlayer[] }) {
  const [expanded, setExpanded] = useState<number | null>(null)
  return <section className="lineup-comparison"><div className="lineup-title"><div><span>WEEK {week} MATCHUP</span><h2>Starting Lineups</h2></div><p>Tap any player to view the stats behind their score.</p></div>
    <div className="lineup-columns lineup-team-head"><div><TeamBadge team={home} small/><span><b>{home.name}</b><small>{home.manager}</small></span><strong>{home.score.toFixed(2)}</strong></div><i>VS</i><div><strong>{away.score.toFixed(2)}</strong><span><b>{away.name}</b><small>{away.manager}</small></span><TeamBadge team={away} small/></div></div>
    <div className="lineup-list">{left.map((player,index) => { const opponent=right[index]||{name:'Open slot',position:player.position,nflTeam:'—',opponent:'—',game:'',projection:0,points:0,stats:'No starter assigned.',status:'OPEN'}; const open=expanded===index; return <button className={`lineup-row ${open?'expanded':''}`} onClick={()=>setExpanded(open?null:index)} key={`${player.name}-${index}`}>
      <div className="lineup-player left-player"><span className="player-avatar">{player.name.split(' ').map(word=>word[0]).join('').slice(0,2)}</span><span><b>{player.name}</b><small>{player.position} · {player.nflTeam} · vs {player.opponent}</small><em>{player.game} · {player.status}</em>{open&&<p>{player.stats}</p>}</span><strong>{player.points.toFixed(2)}<small>PROJ {player.projection.toFixed(2)}</small></strong></div>
      <i>{player.position}</i>
      <div className="lineup-player right-player"><strong>{opponent.points.toFixed(2)}<small>PROJ {opponent.projection.toFixed(2)}</small></strong><span><b>{opponent.name}</b><small>{opponent.position} · {opponent.nflTeam} · vs {opponent.opponent}</small><em>{opponent.game} · {opponent.status}</em>{open&&<p>{opponent.stats}</p>}</span><span className="player-avatar">{opponent.name.split(' ').map(word=>word[0]).join('').slice(0,2)}</span></div>
    </button>})}</div>
  </section>
}

function RosterAssignments({ teams, assignments, claims, leagueId, onSave, onRemove, onReview }: { teams: Team[]; assignments: RosterAssignment[]; claims: RosterClaim[]; leagueId:string; onSave: (team: Team, email: string) => Promise<void>; onRemove: (team: Team) => Promise<void>; onReview:(id:string,approve:boolean)=>Promise<void> }) {
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
  const inviteUrl=`${window.location.origin}/?invite=${leagueId}`
  return <main className="access-page roster-page"><div className="admin-heading"><div><div className="eyebrow">PLAYER INVITATIONS</div><h1>Connect Player Accounts</h1><p>Share the invitation link. Players select their Sleeper roster and you approve the request.</p></div><div className="roster-progress"><b>{assignments.length} / {teams.length}</b><span>ROSTERS CONNECTED</span></div></div>
    <section className="invite-panel"><div><span>LEAGUE INVITATION LINK</span><b>{inviteUrl}</b><small>Send this privately to your league members.</small></div><button className="primary" onClick={()=>navigator.clipboard.writeText(inviteUrl)}>COPY INVITE LINK</button></section>
    {claims.filter(x=>x.status==='PENDING').length>0&&<section className="claim-review"><h2>Pending roster claims</h2>{claims.filter(x=>x.status==='PENDING').map(claim=><article key={claim.id}><div><b>{claim.fantasyToolsName}</b><span>{claim.fantasyToolsEmail}</span></div><div><b>{claim.sleeperTeamName}</b><span>{claim.sleeperManagerName}</span></div><button className="primary" onClick={()=>onReview(claim.id,true)}>APPROVE</button><button className="secondary" onClick={()=>onReview(claim.id,false)}>REJECT</button></article>)}</section>}
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
    try { setArtwork((await apiFetch<{url:string}>('/api/images/cards',{method:'POST',body})).url) }
    catch (ex) { setError((ex as Error).message) }
    finally { setUploading(false) }
  }

  const pasteArtwork = async () => {
    try {
      if (!navigator.clipboard?.read) throw new Error('This browser does not allow the Paste Image button. Press Ctrl+V here or use Choose Image instead.')
      const items = await navigator.clipboard.read()
      for (const item of items) {
        const imageType = item.types.find(type => type.startsWith('image/'))
        if (!imageType) continue
        const blob = await item.getType(imageType)
        await upload(new File([blob], `pasted-card.${imageType.split('/')[1] || 'png'}`, { type:imageType }))
        return
      }
      setError('There is no image in your clipboard. Copy the card image first, then try again.')
    } catch (ex) { setError((ex as Error).message || 'The browser could not read an image from your clipboard.') }
  }

  const handleArtworkPaste = (event: React.ClipboardEvent) => {
    const image = Array.from(event.clipboardData.items).find(item => item.type.startsWith('image/'))?.getAsFile()
    if (!image) return
    event.preventDefault()
    upload(image)
  }

  const save = async (submitForReview: boolean) => {
    if (!name.trim() && !artwork) { setError('Add a working name or artwork before saving this draft.'); return }
    if (submitForReview && (!name.trim() || !description.trim() || !artwork)) { setError('A name, artwork, and complete game-rules summary are required before review.'); return }
    const status: CardStatus = submitForReview ? 'NEEDS REVIEW' : artwork ? 'ARTWORK READY' : 'IDEA'
    try {
      await onSave({ id: initialCard?.id || Date.now(), serverId: initialCard?.serverId, name: name.trim() || 'Untitled card idea', category, amount, target, copy: description.trim(), icon: category === 'ATTACK' ? '⚡' : category === 'BOOST' ? '🔥' : '🛡', artwork, rarity, copies, active: false, status, notes: notes.trim(), updatedBy: 'Matthew', updatedAt: new Date().toISOString(), effectType, special, ...(effectType === 'Referenced player replaces slot' ? { sourcePlayer, sourcePlayerId, destinationSlot, multiplier } : {}) })
    } catch (ex) { setError((ex as Error).message) }
  }

  return <main className="admin-page" onPaste={handleArtworkPaste}><div className="admin-heading"><div><div className="eyebrow">SHARED COMMISSIONER WORKSPACE</div><h1>{initialCard ? 'Edit Card Draft' : 'Card Creator'}</h1><p>Save artwork and ideas now. Finish the engine rules together before approving the card for the deck.</p></div><button className="secondary" onClick={onCancel}>VIEW CARD LIBRARY →</button></div>
    <div className="creator-layout"><section className="creator-form">
      <div className="form-section"><h3>01 · CARD IDENTITY & SEARCH DATA</h3><div className="form-grid"><label className="wide">CARD NAME<input value={name} onChange={e=>setName(e.target.value)} placeholder="e.g. Crushing Blow" /></label><label>CATEGORY<select value={category} onChange={e=>setCategory(e.target.value as Category)}><option>ATTACK</option><option>BOOST</option><option>UNIQUE</option></select></label><label>RARITY<select value={rarity} onChange={e=>setRarity(e.target.value)}><option>Common</option><option>Uncommon</option><option>Rare</option><option>Legendary</option></select></label><label className="wide">RULES SUMMARY FOR THE GAME LOG<textarea value={description} onChange={e=>setDescription(e.target.value)} placeholder="Store the card effect as searchable text. This will not be printed over the artwork." /></label></div></div>
      <div className="form-section"><h3>02 · COMPLETE FINISHED CARD</h3><label className={`upload-zone ${artwork?'has-art':''}`}><input type="file" accept="image/png,image/jpeg,image/webp" disabled={uploading} onChange={e=>upload(e.target.files?.[0])}/>{uploading?<><b>⋯</b><strong>UPLOADING…</strong><span>SENDING THE IMAGE TO THE CARD IMAGE STORE</span></>:artwork?<><img src={artwork}/><span>CHOOSE A DIFFERENT FINISHED CARD</span></>:<><b>↑</b><strong>CHOOSE THE COMPLETE CARD IMAGE</strong><span>UPLOAD FROM PHOTOS/FILES, OR PASTE AN IMAGE BELOW</span></>}</label><div className="paste-artwork-row"><button type="button" className="secondary paste-artwork-button" disabled={uploading} onClick={pasteArtwork}>PASTE IMAGE FROM CLIPBOARD</button><small>DESKTOP: YOU CAN ALSO PRESS CTRL+V ANYWHERE ON THIS PAGE</small></div></div>
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

function CommissionerAccess({ teams, grants, onChange, leagueId }: { teams: Team[]; grants: Record<number,string[]>; onChange: (next: Record<number,string[]>) => void; leagueId: string }) {
  const { user } = useAuth()
  const [workspace, setWorkspace] = useState<RosterWorkspace | null | undefined>(undefined)
  useEffect(() => { apiFetch<RosterWorkspace>(`/api/leagues/${leagueId}/rosters`).then(setWorkspace).catch(()=>setWorkspace(null)) }, [leagueId])
  const primaryRoster = workspace?.assignments.find(item=>item.fantasyToolsUserId===workspace.primaryCommissionerUserId)
  const primaryTeam = primaryRoster ? teams.find(team=>team.id===primaryRoster.rosterId) : undefined
  const viewerIsPrimary = Boolean(user?.userId && user.userId===workspace?.primaryCommissionerUserId)
  const [selectedTeam, setSelectedTeam] = useState(teams[0].id)
  const selected = teams.find(team=>team.id===selectedTeam) || teams[0]
  const selectedIsPrimary = selected.id === primaryTeam?.id
  const selectedGrants = grants[selected.id] || []
  const toggle = (permission: string) => {
    if (permission === 'manage_co_commissioners') return
    const current = grants[selected.id] || []
    onChange({...grants,[selected.id]:current.includes(permission)?current.filter(item=>item!==permission):[...current,permission]})
  }
  if (workspace === undefined) return <main className="access-page"><div className="setup-message">Checking commissioner access…</div></main>
  if (!viewerIsPrimary) return <main className="access-page"><div className="setup-message">Only the person who created this Chaos Cards league can manage commissioner access.</div></main>
  return <main className="access-page"><div className="admin-heading"><div><div className="eyebrow">PRIMARY COMMISSIONER ONLY</div><h1>Commissioner Access</h1><p>Give trusted managers only the tools they need. Permission changes are permanently logged.</p></div>{primaryTeam?<div className="primary-owner"><TeamBadge team={primaryTeam} small/><span>PRIMARY COMMISSIONER<b>{primaryTeam.manager}</b></span></div>:<div className="primary-owner unlinked-owner"><span>PRIMARY COMMISSIONER<b>ROSTER NOT CONNECTED</b></span></div>}</div>
    {!primaryTeam&&<div className="setup-message">You are the Chaos Cards league creator, but your account is not connected to a Sleeper roster yet. No Sleeper manager is labeled as owner until you connect yours.</div>}
    <div className="access-layout"><aside className="manager-list"><h3>LEAGUE MANAGERS</h3>{teams.map(team=><button className={selected.id===team.id?'selected':''} onClick={()=>setSelectedTeam(team.id)} key={team.id}><TeamBadge team={team} small/><span><b>{team.manager}</b><small>{team.name}</small></span>{primaryTeam?.id===team.id?<em>OWNER</em>:(grants[team.id]?.length||0)>0?<em className="co">CO-COMMISSIONER</em>:null}</button>)}</aside>
      <section className="permission-panel"><div className="permission-person"><TeamBadge team={selected}/><div><span>EDITING ACCESS FOR</span><h2>{selected.manager}</h2><p>{selected.name}</p></div>{selectedIsPrimary&&<b>PRIMARY COMMISSIONER · FULL ACCESS</b>}</div>
        <div className="permission-list">{commissionerPermissions.map(([key,title,description])=>{const primary=selectedIsPrimary;const primaryOnly=key==='manage_co_commissioners';const checked=primary||selectedGrants.includes(key);return <label className={`${checked?'granted':''} ${primaryOnly&&!primary?'locked-permission':''}`} key={key}><input type="checkbox" checked={checked} disabled={primary||primaryOnly} onChange={()=>toggle(key)}/><span><b>{title}</b><small>{description}</small></span>{primaryOnly&&!primary&&<em>PRIMARY ONLY</em>}</label>})}</div>
        {!selectedIsPrimary&&<div className="access-summary"><b>{selectedGrants.length?`${selected.manager} IS A CO-COMMISSIONER`:`${selected.manager} IS A REGULAR PLAYER`}</b><span>{selectedGrants.length?`${selectedGrants.length} permission${selectedGrants.length===1?'':'s'} granted`:'No administrative access'}</span></div>}
      </section></div>
  </main>
}

function LeagueGame({ league }: { league: ChaosLeague }) {
  // Only ever rendered behind the auth gate in main.tsx, so there is no signed-out screen here.
  const { user: account, logout } = useAuth()
  const workspaceId = league.leagueId
  const sleeperLeagueId = league.sleeperLeagueId
  const [screen, setScreen] = useState<'home' | 'room' | 'battle' | 'admin' | 'library' | 'permissions' | 'rosters' | 'weekly'>('home')
  const [selected, setSelected] = useState<number[]>([])
  const [played, setPlayed] = useState<Card[]>([])
  const [pending, setPending] = useState<Card[]>([])
  const [liveCardPlayed, setLiveCardPlayed] = useState(false)
  const [log, setLog] = useState<string[][]>([])
  const [targeting, setTargeting] = useState(false)
  const [inspecting, setInspecting] = useState<Card | null>(null)
  const [toast, setToast] = useState('')
  const [revealed, setRevealed] = useState(false)
  const [activeNav, setActiveNav] = useState('League Room')
  const [teamData, setTeamData] = useState<Team[]>([])
  const [currentWeek,setCurrentWeek]=useState(1)
  const [leagueName, setLeagueName] = useState('THE CHAOS LEAGUE')
  const [sleeperStatus, setSleeperStatus] = useState<'loading'|'live'|'fallback'>('loading')
  const [uploadedCards, setUploadedCards] = useState<UploadedCard[]>(() => { try { return JSON.parse(localStorage.getItem('chaos-uploaded-cards') || '[]').map((card: UploadedCard) => ({...card,status:card.status || (card.active?'ACTIVE':'ARTWORK READY'),notes:card.notes || '',updatedBy:card.updatedBy || 'Commissioner',updatedAt:card.updatedAt || new Date().toISOString()})) } catch { return [] } })
  const [editingCard, setEditingCard] = useState<UploadedCard | null>(null)
  const [weeklyCards,setWeeklyCards]=useState<WeeklyCard[]>([])
  const [permissionGrants, setPermissionGrants] = useState<Record<number,string[]>>(() => { try { return JSON.parse(localStorage.getItem('chaos-permission-grants') || '{}') } catch { return {} } })
  const [rosterAssignments, setRosterAssignments] = useState<RosterAssignment[]>(() => { try { return JSON.parse(localStorage.getItem('chaos-roster-assignments') || '[]') } catch { return [] } })
  const [rosterClaims, setRosterClaims] = useState<RosterClaim[]>([])
  const [primaryCommissionerUserId, setPrimaryCommissionerUserId] = useState('')
  const [cardPermissions, setCardPermissions] = useState<string[]>([])
  const [activeMatchup, setActiveMatchup] = useState(0)
  const [gameWeek,setGameWeek]=useState<GameWeek|null>(null)
  const [gameMessage,setGameMessage]=useState('')
  const emptyTeam:Team={id:0,manager:'Roster pending',name:'Roster pending',initials:'—',record:'0–0',score:0,chaos:0,hand:0,accent:'#64748b'}
  const home = teamData[activeMatchup * 2] || teamData[0] || emptyTeam, away = teamData[activeMatchup * 2 + 1] || teamData[1] || emptyTeam
  const primaryRoster = rosterAssignments.find(item => item.fantasyToolsUserId === primaryCommissionerUserId)
  const primaryTeam = primaryRoster ? teamData.find(team => team.id === primaryRoster.rosterId) : undefined
  const viewerIsPrimary = Boolean(account?.userId && account.userId === primaryCommissionerUserId)
  const viewerRoster = rosterAssignments.find(item => item.fantasyToolsUserId === account?.userId)
  const viewerTeam = viewerRoster ? teamData.find(team => team.id === viewerRoster.rosterId) : undefined
  const canCreateCards = viewerIsPrimary || cardPermissions.includes('create_card_drafts')
  const dealtCards:Card[]=(gameWeek?.hand||[]).map((card,index)=>({id:Array.from(card.copyId).reduce((sum,char)=>((sum*31+char.charCodeAt(0))|0),0)+index,copyId:card.copyId,serverId:card.cardId,artworkUrl:card.artworkUrl,name:card.name,category:card.category,amount:0,target:card.target,copy:card.description,icon:card.category==='ATTACK'?'⚡':card.category==='BOOST'?'🔥':'★'}))
  const handCards=dealtCards
  const syncedLineup=(teamId:number,side:number,startersOnly=true):LineupPlayer[]=>{
    const team=gameWeek?.teams?.find(item=>item.rosterId===teamId)
    if(!team?.players?.length)return []
    const sorted=[...team.players].sort((a,b)=>Number(b.starter)-Number(a.starter))
    return sorted.filter(player=>!startersOnly||player.starter).map(player=>({name:player.name,position:player.position||'—',nflTeam:player.nflTeam||'FA',opponent:'TBD',game:player.starter?'STARTER':'BENCH',projection:0,points:Number(player.points||0),stats:'Live fantasy points imported from Sleeper.',status:player.starter?'ACTIVE':'BENCH'}))
  }

  useEffect(()=>{
    if(!gameWeek||!dealtCards.length)return
    const ids=new Set((gameWeek.selections||[]).map(selection=>selection.copyId))
    setPending(dealtCards.filter(card=>card.copyId&&ids.has(card.copyId)))
    setRevealed(['revealed','live','finalized'].includes(gameWeek.status))
  },[gameWeek?.status,gameWeek?.hand?.length,gameWeek?.selections?.length])

  const applySyncedWeek=(week:GameWeek)=>{
    setGameWeek(week)
    if(week.teams?.length){
      const ordered=[...week.teams].sort((a,b)=>{const ma=week.matchups.find(item=>item.rosterId===a.rosterId)?.matchupId??999;const mb=week.matchups.find(item=>item.rosterId===b.rosterId)?.matchupId??999;return ma-mb||a.rosterId-b.rosterId})
      const palette=['#9cff57','#ff4e79','#ffd452','#54d6ff','#b783ff','#ff9f43','#56e3c2','#fb7185','#a78bfa','#38bdf8']
      const imported=ordered.map((team,index)=>{const matchup=week.matchups.find(item=>item.rosterId===team.rosterId);const calculated=week.chaosScores?.find(item=>item.rosterId===team.rosterId);return {id:team.rosterId,sleeperUserId:team.ownerId,manager:team.managerName||`Roster ${team.rosterId}`,name:team.teamName||`${team.managerName}'s Team`,initials:(team.teamName||team.managerName||'TM').split(/\s+/).map(word=>word[0]).join('').slice(0,2).toUpperCase(),record:`${team.wins}–${team.losses}`,score:matchup?.points||0,chaos:calculated?.chaosScore??matchup?.points??0,hand:team.rosterId===week.team?.rosterId?week.hand.length:0,accent:palette[index%palette.length]}}
      )
      setTeamData(imported)
    }
    setSleeperStatus('live')
  }
  const refreshGame=async(weekNumber=currentWeek)=>{const week=await apiFetch<GameWeek>(`/api/leagues/${workspaceId}/game/weeks/${weekNumber}`);applySyncedWeek(week);return week}

  useEffect(() => {
    fetch('https://api.sleeper.app/v1/state/nfl').then(response=>response.ok?response.json():Promise.reject()).then(state=>{
      const week=Math.max(1,Math.min(18,Number(state.week||state.display_week||1)))
      setCurrentWeek(week)
      return apiFetch(`/api/leagues/${workspaceId}/game/sync/${week}`,{method:'POST'}).then(()=>refreshGame(week)).catch(()=>refreshGame(week))
    }).catch(()=>setSleeperStatus('fallback'))
    fetch(`https://api.sleeper.app/v1/league/${sleeperLeagueId}`).then(r=>r.ok?r.json():Promise.reject()).then(data=>setLeagueName(data.name||league.name||'SLEEPER LEAGUE')).catch(()=>setLeagueName(league.name||'SLEEPER LEAGUE'))
  }, [sleeperLeagueId,workspaceId])

  useEffect(()=>{
    const timer=window.setInterval(()=>apiFetch(`/api/leagues/${workspaceId}/game/sync/${currentWeek}`,{method:'POST'}).then(()=>refreshGame(currentWeek)).catch(()=>{}),60000)
    return()=>window.clearInterval(timer)
  },[workspaceId,currentWeek])

  useEffect(() => {
    apiFetch<CardWorkspaceAccess>(`/api/leagues/${workspaceId}/cards`)
      .then(workspace => {
        const shared = workspace.cards.map(fromServerCard)
        setUploadedCards(shared)
        setWeeklyCards(workspace.weeklyCards||[])
        setCardPermissions(workspace.collaborators?.find(item=>item.userId===account?.userId)?.permissions || [])
        localStorage.setItem('chaos-uploaded-cards',JSON.stringify(shared))
        if(account?.userId===league.primaryCommissionerUserId){const existing=new Set(workspace.cards.map(card=>String(card.name).toLowerCase()));const missing=cardDataIdeas.filter(card=>!existing.has(card.name.toLowerCase()));if(missing.length)Promise.all(missing.map(card=>apiFetch(`/api/leagues/${workspaceId}/cards`,{method:'POST',body:JSON.stringify(toDraftRequest(card))}))).then(()=>apiFetch<CardWorkspaceAccess>(`/api/leagues/${workspaceId}/cards`)).then(updated=>{const imported=updated.cards.map(fromServerCard);setUploadedCards(imported);localStorage.setItem('chaos-uploaded-cards',JSON.stringify(imported))}).catch(()=>{})}
      })
      .catch(() => { /* The first saved draft creates the workspace. */ })
    apiFetch<RosterWorkspace>(`/api/leagues/${workspaceId}/rosters`)
      .then(workspace => { setPrimaryCommissionerUserId(workspace.primaryCommissionerUserId); setRosterAssignments(workspace.assignments); setRosterClaims(workspace.claims||[]); localStorage.setItem('chaos-roster-assignments',JSON.stringify(workspace.assignments)) })
      .catch(() => { /* The commissioner starts setup from the roster screen. */ })
  }, [workspaceId,account?.userId])

  // The server is the record for every card mutation -- localStorage only mirrors the answer so a
  // reload paints something before the workspace request returns.
  const saveUploadedCard = async (card: UploadedCard) => {
    const path = card.serverId ? `/api/leagues/${workspaceId}/cards/${card.serverId}` : `/api/leagues/${workspaceId}/cards`
    const saved = fromServerCard(await apiFetch<any>(path,{method:card.serverId?'PUT':'POST',body:JSON.stringify(toServerCard(card,card.status==='NEEDS REVIEW'))}))
    const next = uploadedCards.some(existing=>existing.id===card.id) ? uploadedCards.map(existing=>existing.id===card.id?saved:existing) : [...uploadedCards, saved]
    localStorage.setItem('chaos-uploaded-cards', JSON.stringify(next)); setUploadedCards(next); setScreen('library'); setActiveNav('Card Library')
  }
  const updateCardStatus = async (id: number, status: CardStatus) => {
    const current = uploadedCards.find(card=>card.id===id)
    if (!current?.serverId) return
    const saved = fromServerCard(await apiFetch<any>(`/api/leagues/${workspaceId}/cards/${current.serverId}/status`,{method:'POST',body:JSON.stringify({status})}))
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
    const cardKeys=['create_card_drafts','edit_card_rules','approve_cards']
    rosterAssignments.forEach(assignment=>{if(!assignment.fantasyToolsUserId)return;apiFetch(`/api/leagues/${workspaceId}/cards/collaborators`,{method:'PUT',body:JSON.stringify({userId:assignment.fantasyToolsUserId,permissions:(next[assignment.rosterId]||[]).filter(permission=>cardKeys.includes(permission))})}).catch(()=>{})})
  }
  const saveRosterAssignment = async (team: Team, email: string) => {
    const requested: RosterAssignment = { rosterId:team.id, sleeperUserId:team.sleeperUserId || String(team.id), sleeperManagerName:team.manager, sleeperTeamName:team.name, fantasyToolsEmail:email.trim(), fantasyToolsName:email.trim() }
    const saved = await apiFetch<RosterAssignment>(`/api/leagues/${workspaceId}/rosters/${team.id}`,{method:'PUT',body:JSON.stringify(requested)})
    const next = [...rosterAssignments.filter(item=>item.rosterId!==team.id && item.fantasyToolsUserId!==saved.fantasyToolsUserId),saved]
    setRosterAssignments(next); localStorage.setItem('chaos-roster-assignments',JSON.stringify(next))
  }
  const removeRosterAssignment = async (team: Team) => {
    await apiFetch(`/api/leagues/${workspaceId}/rosters/${team.id}`,{method:'DELETE'})
    const next=rosterAssignments.filter(item=>item.rosterId!==team.id); setRosterAssignments(next); localStorage.setItem('chaos-roster-assignments',JSON.stringify(next))
  }
  const reviewRosterClaim = async (id:string, approve:boolean) => {
    const workspace=await apiFetch<RosterWorkspace>(`/api/leagues/${workspaceId}/rosters/claims/${id}?approve=${approve}`,{method:'POST'})
    setRosterAssignments(workspace.assignments)
    setRosterClaims(workspace.claims||[])
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
    if (revealed) { setToast('All four weekly cards are locked and visible.'); setInspecting(null); return }
    if (!isChaosWeek && pending.length >= 4) { setToast('Your four weekly card slots are full.'); setInspecting(null); return }
    setSelected([inspecting.id])
    setInspecting(null)
    setTargeting(true)
  }

  const targetCard = handCards.find(card => card.id === selected[0])
  const isChaosWeek = weeklyCards.some(card=>card.week===currentWeek&&card.active&&card.name.toLowerCase()==='chaos')
  const eligibleTargets = useMemo(() => {
    if (!targetCard) return []
    const target = targetCard.target.toLowerCase()
    const roster = target.includes('opponent') ? syncedLineup(away.id,1) : syncedLineup(home.id,0)
    const side = target.includes('opponent') ? 'Opponent' : 'Your'
    if (target.includes('team')) return [{ label: `${side} entire team`, badge: 'TEAM' }]
    if (target.includes('card')) return played.map(card=>({label:card.name,badge:'CARD'}))
    const position = ['QB','RB','WR','TE','FLEX','DEF'].find(item=>target.includes(item.toLowerCase()))
    let eligible = position ? roster.filter(player=>player.position===position || (position==='FLEX' && ['RB','WR','TE'].includes(player.position))) : roster
    if (/\b(qb|rb|wr|te)1\b/.test(target)) eligible = eligible.slice(0,1)
    return eligible.map(player=>({label:`${side} ${player.position} · ${player.name}`,badge:player.position}))
  }, [targetCard, away.id, home.id, activeMatchup, played])

  const playLive = () => {
    if (selected.length !== 1) { setToast('Choose one card from your hand first.'); return }
    setTargeting(true)
  }

  const confirmPlay = async (chosen?:{rosterId?:string;playerId?:string;slot?:string}) => {
    const card = handCards.find(c => c.id === selected[0])!
    if (revealed) {
      if (liveCardPlayed) { setTargeting(false); setSelected([]); setToast('Your one live card has already been played this week.'); return }
      setPlayed(p => [...p, card]); setLiveCardPlayed(true)
      setLog(l => [[new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }), card.icon, `${home.manager} played ${card.name.toUpperCase()} live against ${away.manager}’s QB slot.`], ...l])
      setToast(`${card.name} slammed onto the table and is now locked!`)
    } else {
      if (!isChaosWeek && pending.length >= 4) { setTargeting(false); setSelected([]); setToast('Your four weekly card slots are full.'); return }
      const normalized = card.category
      const count = pending.filter(item=>item.category===normalized).length
      const limit = normalized === 'UNIQUE' ? 2 : 1
      if (!isChaosWeek && count >= limit) { setTargeting(false); setSelected([]); setToast(`Your ${normalized} slots are already full.`); return }
      if(card.copyId){
        try { await apiFetch(`/api/leagues/${workspaceId}/game/weeks/${currentWeek}/selections`,{method:'POST',body:JSON.stringify({copyId:card.copyId,targetRosterId:chosen?.rosterId||'',targetPlayerId:chosen?.playerId||'',targetSlot:chosen?.slot||''})}); await refreshGame() }
        catch(error){setTargeting(false);setSelected([]);setToast((error as Error).message);return}
      } else setPending(p => [...p, card])
      setLog(l => [[new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }), '🔒', `${card.name.toUpperCase()} was selected privately. It can be changed before the deadline.`], ...l])
      setToast(`${card.name} selected. You can still return it to your hand.`)
    }
    setSelected([]); setTargeting(false)
    setTimeout(() => setToast(''), 2800)
  }

  const returnToHand = async (card: Card) => {
    if (revealed) return
    if(card.copyId){try{await apiFetch(`/api/leagues/${workspaceId}/game/weeks/${currentWeek}/selections/${card.copyId}`,{method:'DELETE'});await refreshGame()}catch(error){setToast((error as Error).message);return}}
    else setPending(current => current.filter(item => item.id !== card.id))
    setLog(l => [[new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }), '↩', `${card.name.toUpperCase()} was returned to the private hand before lock.`], ...l])
    setToast(`${card.name} returned to your hand.`); setTimeout(() => setToast(''), 2200)
  }

  const toggleReveal = async () => {
    if (!revealed) {
      const boost=pending.filter(card=>card.category==='BOOST').length,attack=pending.filter(card=>card.category==='ATTACK').length,unique=pending.filter(card=>card.category==='UNIQUE').length
      if(!isChaosWeek&&(pending.length!==4||boost!==1||attack!==1||unique!==2)){setToast('Select exactly 1 Boost, 1 Attack, and 2 Unique cards.');return}
      if(gameWeek){try{await apiFetch(`/api/leagues/${workspaceId}/game/weeks/${currentWeek}/reveal`,{method:'POST'});await refreshGame()}catch(error){setToast((error as Error).message);return}}
      setPlayed(current => [...current, ...pending]); setPending([]); setRevealed(true)
      setLog(l => [['NOW', '👁', 'Pre-week selections locked and revealed across the league.'], ...l])
    } else {
      setRevealed(false); setPending([]); setLiveCardPlayed(false); setPlayed([])
    }
  }

  const discardCard = async (card: Card) => {
    if(!card.copyId||revealed)return
    try{await apiFetch(`/api/leagues/${workspaceId}/game/weeks/${currentWeek}/hand/${card.copyId}`,{method:'DELETE'});setInspecting(null);await refreshGame();setToast(`${card.name} discarded. Its replacement will arrive with next week's draw.`)}
    catch(error){setToast((error as Error).message)}
    setTimeout(()=>setToast(''),2800)
  }
  const setChallengeTarget=async(copyId:string,cancelledCopyId:string)=>{
    try{await apiFetch(`/api/leagues/${workspaceId}/game/weeks/${currentWeek}/challenge/${copyId}`,{method:'PUT',body:JSON.stringify({cancelledCopyId})});await refreshGame();setToast('Challenge Flag target saved. It will cancel that card before scoring.')}
    catch(error){setToast((error as Error).message)}setTimeout(()=>setToast(''),2800)
  }

  const dealWeek=async()=>{
    setGameMessage(`Opening Week ${currentWeek} for card drawing…`)
    try{await apiFetch(`/api/leagues/${workspaceId}/game/weeks/${currentWeek}/deal`,{method:'POST'});await refreshGame();setGameMessage(`Week ${currentWeek} is open. Each player can now draw back up to eight cards.`)}
    catch(error){setGameMessage((error as Error).message)}
  }
  const drawCards=async()=>{
    setGameMessage('Drawing enough cards to fill your hand…')
    try{const result=await apiFetch<{drawnCards:number;cardsInHand:number}>(`/api/leagues/${workspaceId}/game/weeks/${currentWeek}/draw`,{method:'POST'});await refreshGame();setGameMessage(`You drew ${result.drawnCards} card${result.drawnCards===1?'':'s'}. Your hand now has ${result.cardsInHand}.`)}
    catch(error){setGameMessage((error as Error).message)}
  }
  const challengeSelection=gameWeek?.selections?.find(selection=>dealtCards.find(card=>card.copyId===selection.copyId)?.name==='Challenge Flag')
  const ownRosterId=gameWeek?.team?.rosterId||viewerTeam?.id||0
  const ownMatch=gameWeek?.matchups?.find(match=>match.rosterId===ownRosterId)
  const opponentRosterId=gameWeek?.matchups?.find(match=>match.matchupId===ownMatch?.matchupId&&match.rosterId!==ownRosterId)?.rosterId
  const opponentSelections=gameWeek?.publicSelections?.find(entry=>entry.rosterId===opponentRosterId)?.selections||[]

  if (screen === 'weekly') return <div className="app-shell"><button className="weekly-back" onClick={()=>setScreen('home')}>← BACK TO MY TEAM</button><WeeklyCardSeason leagueId={workspaceId} cards={weeklyCards} currentWeek={currentWeek} canEdit={viewerIsPrimary||cardPermissions.includes('edit_card_rules')} onSaved={saved=>setWeeklyCards(current=>[...current.filter(card=>card.week!==saved.week),saved].sort((a,b)=>a.week-b.week))}/><nav className="mobile-bottom-nav" aria-label="Main navigation"><button onClick={()=>setScreen('home')}><b>⌂</b><span>My Team</span></button><button onClick={()=>setScreen('room')}><b>VS</b><span>Matchups</span></button><button className="active"><b>★</b><span>Weekly</span></button><button onClick={()=>setScreen('library')}><b>▣</b><span>Cards</span></button>{canCreateCards&&<button onClick={()=>setScreen('admin')}><b>＋</b><span>Create</span></button>}</nav></div>

  return <div className="app-shell">
    <button className="weekly-nav-button" onClick={()=>setScreen('weekly')}>WEEKLY CARDS · VIEW THE SEASON</button>
    <nav className="mobile-bottom-nav" aria-label="Main navigation"><button className={screen==='home'?'active':''} onClick={()=>{setScreen('home');setActiveNav('')}}><b>⌂</b><span>My Team</span></button><button className={screen==='room'||screen==='battle'?'active':''} onClick={()=>{setScreen('room');setActiveNav('League Room')}}><b>VS</b><span>Matchups</span></button><button onClick={()=>setScreen('weekly')}><b>★</b><span>Weekly</span></button><button className={screen==='library'?'active':''} onClick={()=>{setScreen('library');setActiveNav('Card Library')}}><b>▣</b><span>Cards</span></button>{canCreateCards?<button className={screen==='admin'?'active':''} onClick={()=>setScreen('admin')}><b>＋</b><span>Create</span></button>:<button onClick={()=>setScreen('home')}><b>●</b><span>Account</span></button>}</nav>
    <header><button className="brand" onClick={() => {setScreen('home');setActiveNav('')}}><b>CC</b><span>CHAOS CARDS<small>FANTASY FOOTBALL</small></span></button><nav><button className={screen==='home'?'active':''} onClick={()=>{setScreen('home');setActiveNav('')}}>My Team</button>{['League Room','Standings','Card Library','History'].map(n => <button className={activeNav===n?'active':''} onClick={() => {setActiveNav(n); setScreen(n==='Card Library'?'library':'room')}} key={n}>{n}</button>)}</nav><div className="admin-actions">{viewerIsPrimary&&<button className="admin-link roster-link" onClick={()=>{setScreen('rosters');setActiveNav('')}}>⇄ ROSTERS</button>}{viewerIsPrimary&&<button className="admin-link commissioner-link" onClick={()=>{setScreen('permissions');setActiveNav('')}}>♛ COMMISSIONERS</button>}{canCreateCards&&<button className="admin-link creator-link" onClick={()=>{setScreen('admin');setActiveNav('')}}>⚙ CARD CREATOR</button>}<button className="admin-link signout-link" title={account?.email} onClick={logout}>Sign out</button></div><div className="week"><span>{viewerTeam?.name || account?.name || 'MY TEAM'}</span><b className={revealed?'':'preweek'}>{revealed?'REVEALED':'PRE-WEEK'}</b></div><TeamBadge team={viewerTeam || home} small /></header>
    {gameWeek?.canDraw&&<div className="setup-message draw-message"><b>YOUR WEEKLY DRAW IS READY</b><p>Your unplayed cards carry over. Draw {gameWeek.cardsNeeded||0} new card{gameWeek.cardsNeeded===1?'':'s'} to refill your hand to eight.</p><button className="primary" onClick={drawCards}>DRAW UP TO 8 CARDS</button>{gameMessage&&<small>{gameMessage}</small>}</div>}
    {screen === 'home' ? <main className="team-home"><section className="team-home-hero"><div className="eyebrow">SIGNED IN AS {account?.email}</div><div className="team-home-title"><TeamBadge team={viewerTeam || home}/><div><span>YOUR FANTASY TEAM</span><h1>{viewerTeam?.name || 'Roster connection pending'}</h1><p>{viewerTeam ? `${viewerTeam.manager} · ${viewerTeam.record}` : 'Your commissioner must approve and connect your Sleeper roster.'}</p></div></div><button className="primary enter-league" onClick={()=>{setScreen('room');setActiveNav('League Room')}}>ENTER LEAGUE ROOM →</button></section><section className="home-summary"><article><span>WEEK 1 MATCHUP</span><strong>{viewerTeam?.score.toFixed(1) || '—'}</strong><p>Sleeper points</p></article><article><span>YOUR CHAOS SCORE</span><strong>{viewerTeam?.chaos.toFixed(1) || '—'}</strong><p>After card effects</p></article><article><span>CARDS IN HAND</span><strong>{viewerTeam?.hand ?? 0}</strong><p>Ready to inspect and play</p></article><article><span>ROSTER STATUS</span><strong className="status-word">{viewerTeam?'CONNECTED':'PENDING'}</strong><p>{viewerTeam?'FantasyTools account linked':'Waiting for commissioner'}</p></article></section><section className="home-panels"><article><div className="section-title"><h3>YOUR CARDS</h3><span>{cards.length} AVAILABLE</span></div><div className="home-card-fan">{cards.slice(0,3).map(card=><ChaosCard card={card} compact key={card.id}/>)}</div><button onClick={()=>{setScreen('battle');setActiveNav('')}}>VIEW HAND & PLAY CARDS →</button></article><article><div className="section-title"><h3>YOUR STARTING ROSTER</h3><span>WEEK 1</span></div>{lineupFor(viewerTeam?.id || home.id,0).slice(0,5).map(player=><div className="home-player" key={`${player.position}-${player.name}`}><b>{player.position}</b><span>{player.name}<small>{player.nflTeam} · vs {player.opponent}</small></span><strong>{player.points.toFixed(1)}</strong></div>)}<button onClick={()=>{setScreen('battle');setActiveNav('')}}>VIEW FULL MATCHUP →</button></article></section></main> : screen === 'rosters' ? <RosterAssignments teams={teamData} assignments={rosterAssignments} claims={rosterClaims} leagueId={workspaceId} onSave={saveRosterAssignment} onRemove={removeRosterAssignment} onReview={reviewRosterClaim}/> : screen === 'permissions' ? <CommissionerAccess teams={teamData} grants={permissionGrants} onChange={updatePermissionGrants} leagueId={workspaceId}/> : screen === 'admin' && canCreateCards ? <CardCreator initialCard={editingCard} onSave={saveUploadedCard} onCancel={()=>{setEditingCard(null);setScreen('library');setActiveNav('Card Library')}}/> : screen === 'library' ? <SharedCardLibrary uploaded={uploadedCards} onCreate={()=>{if(canCreateCards){setEditingCard(null);setScreen('admin');setActiveNav('')}}} onEdit={card=>{if(canCreateCards){setEditingCard(card);setScreen('admin');setActiveNav('')}}} onStatus={updateCardStatus} onDelete={deleteUploadedCard}/> : screen === 'room' ? <main className="room">
      <section className="room-hero"><div><div className="eyebrow">{leagueName} · WEEK {currentWeek}</div><h1>League Room</h1><p>{sleeperStatus === 'live' ? gameWeek?.sleeperStatus==='pre_draft'?'SLEEPER CONNECTED · WAITING FOR THE DRAFT':'LIVE SLEEPER ROSTERS, MATCHUPS, LINEUPS, AND SCORES' : sleeperStatus === 'loading' ? 'IMPORTING YOUR SLEEPER LEAGUE…' : 'SLEEPER IS TEMPORARILY UNAVAILABLE'}</p></div><div className="deadline"><span>WEDNESDAY CARD REVEAL</span><strong>{revealed ? 'CARDS REVEALED' : gameWeek?.deadlineUtc?new Date(gameWeek.deadlineUtc).toLocaleString():'NOT SCHEDULED'}</strong>{viewerIsPrimary&&<button onClick={toggleReveal}>{revealed ? 'CARDS ARE VISIBLE' : 'COMMISSIONER: LOCK & REVEAL'}</button>}</div></section>
      {gameWeek?.sleeperStatus==='pre_draft'&&<div className="setup-message">Sleeper is connected. Team managers are ready, and drafted players will appear automatically after the draft.</div>}
      {viewerIsPrimary&&gameWeek?.status==='setup'&&<div className="setup-message"><b>WEEK {currentWeek} IS NOT OPEN YET</b><p>Finish and activate at least eight cards with artwork, then let every player draw their own weekly cards.</p><button className="primary" onClick={dealWeek}>OPEN WEEK {currentWeek} FOR DRAWING</button>{gameMessage&&<small>{gameMessage}</small>}</div>}
      {log.length>0&&<div className="ticker"><b>LIVE CHAOS</b>{log.slice(0,3).map((entry,index)=><span key={`${entry[0]}-${index}`}>{entry[1]} {entry[2]}</span>)}</div>}
      <section className="matchup-grid">{[0,2,4,6,8].map((i, index) => { const a=teamData[i], b=teamData[i+1]; if (!a || !b) return null; return <article className={`matchup ${index===0?'featured':''}`} key={a.id} onClick={() => {setActiveMatchup(index);setScreen('battle')}}>
        <div className="matchup-top"><span>{index===0?'🔥 FEATURED BATTLE':`MATCHUP ${index+1}`}</span><b>{index < 2 ? 'LIVE' : 'SUN 4:25'}</b></div>
        <div className="versus-row"><div><TeamBadge team={a}/><strong>{a.name}</strong><small>{a.manager} · {a.record}</small></div><div className="scores"><span>{a.score.toFixed(1)} <i>SLEEPER</i></span><b>{a.chaos.toFixed(1)}</b><em>VS</em><b>{b.chaos.toFixed(1)}</b><span>{b.score.toFixed(1)} <i>SLEEPER</i></span></div><div><TeamBadge team={b}/><strong>{b.name}</strong><small>{b.manager} · {b.record}</small></div></div>
        <div className="public-cards"><span>HAND {a.hand} 🂠</span><div>{revealed ? <><i className="mini attack">⚡</i><i className="mini boost">🔥</i></> : '2 HIDDEN'}</div><span>🂠 {b.hand} HAND</span></div>
        <button className="spectate">VIEW LINEUPS & BATTLE →</button>
      </article>})}</section>
    </main> : <main className="battle">
      <button className="back" onClick={() => setScreen('room')}>← BACK TO LEAGUE ROOM</button>
      <LineupComparison home={home} away={away} week={currentWeek} left={syncedLineup(home.id,0)} right={syncedLineup(away.id,1)}/>
      <section className="battle-board">
        <div className="combatant left"><TeamBadge team={home}/><div><small>{home.manager} · {home.record}</small><h2>{home.name}</h2></div><div className="score-block"><span>SLEEPER {home.score.toFixed(1)}</span><strong>{chaos.toFixed(1)}</strong><b>CHAOS SCORE</b></div></div>
        <div className="vs-burst">VS<small>WEEK {currentWeek} · {revealed?'REVEALED':'PRE-WEEK'}</small></div>
        <div className="combatant right"><div className="score-block"><span>SLEEPER {away.score.toFixed(1)}</span><strong>{away.chaos.toFixed(1)}</strong><b>CHAOS SCORE</b></div><div><small>{away.manager} · {away.record}</small><h2>{away.name}</h2></div><TeamBadge team={away}/></div>
        <div className="field">
          <div className="effect-zone"><span>YOUR ACTIVE EFFECTS · LOCKED</span>{played.length?played.map(c => <ChaosCard key={c.id} card={c} compact />):<div className="empty-effects">NO LOCKED EFFECTS</div>}</div>
          <div className="calculation"><span>CHAOS CALCULATION</span><div><p>Sleeper score <b>{home.score.toFixed(2)}</b></p>{played.some(c=>c.id===2)&&<p className="positive">End Zone Fever <b>+6.20</b></p>}{played.some(c=>c.id===5)&&<p className="positive">Hail Mary <b>+8.00</b></p>}{played.some(c=>c.id===1)&&<p className="negative">Crushing Blow <b>−7.10</b></p>}{!played.length&&<p className="waiting-math">Waiting for Thursday reveal…</p>}<strong>CHAOS SCORE <b>{chaos.toFixed(2)}</b></strong></div></div>
          <div className="effect-zone enemy"><span>OPPONENT EFFECTS</span>{revealed?<div className="empty-effect">Revealed opponent selections load from the server.</div>:<><div className="card-back">?</div><div className="card-back">?</div><div className="card-back">?</div><div className="card-back">?</div></>}</div>
        </div>
      </section>
      {revealed&&challengeSelection&&!challengeSelection.cancelledCopyId&&<section className="pending-zone challenge-picker"><div className="section-title"><h3>CHALLENGE FLAG</h3><span>CHOOSE BEFORE THURSDAY</span></div><p>Choose one card revealed by your weekly opponent to cancel. Weekly Cards cannot be challenged.</p><div className="pending-list">{opponentSelections.map(selection=>{const card=uploadedCards.find(item=>item.serverId===selection.cardId);return <button key={selection.copyId} onClick={()=>setChallengeTarget(challengeSelection.copyId,selection.copyId)}>{card?.name||'Opponent card'} · {selection.category}</button>})}</div></section>}
      <section className="battle-lower"><div className="log"><div className="section-title"><h3>GAME LOG</h3><span>LOCKED ACTIONS</span></div>{log.map((x,i)=><div className="log-row" key={i}><time>{x[0]}</time><i>{x[1]}</i><p>{x[2]}</p></div>)}</div><aside>{!revealed && <div className="pending-zone"><div className="section-title"><h3>YOUR WEEKLY PICKS</h3><span>{isChaosWeek?`${pending.length} / ${handCards.length}`:`${pending.length} / 4`} · UNLOCKED</span></div><div className="pick-slots">{isChaosWeek?<span className="filled">NO COUNT OR CATEGORY LIMITS</span>:<><span className={pending.some(c=>c.category==='BOOST')?'filled':''}>BOOST {pending.some(c=>c.category==='BOOST')?'✓':'0/1'}</span><span className={pending.some(c=>c.category==='ATTACK')?'filled':''}>ATTACK {pending.some(c=>c.category==='ATTACK')?'✓':'0/1'}</span><span className={pending.filter(c=>c.category==='UNIQUE').length===2?'filled':''}>UNIQUE {pending.filter(c=>c.category==='UNIQUE').length}/2</span></>}</div>{pending.length===0?<p>Tap a card to inspect it, then choose Select This Card.</p>:<div className="pending-list">{pending.map(card=><div key={card.id}><ChaosCard card={card} compact/><button onClick={()=>returnToHand(card)}>↩ RETURN TO HAND</button></div>)}</div>}</div>}<div className="section-title"><h3>YOUR HAND</h3><span>{handCards.filter(c=>!played.some(p=>p.id===c.id)&&!pending.some(p=>p.id===c.id)).length} CARDS · TAP TO INSPECT</span></div><div className="hand">{handCards.filter(c=>!played.some(p=>p.id===c.id)&&!pending.some(p=>p.id===c.id)).map(c=><ChaosCard key={c.id} card={c} selected={selected.includes(c.id)} onClick={()=>setInspecting(c)}/>)}</div></aside></section>
      {inspecting && <div className="modal-backdrop inspect-backdrop" onClick={()=>setInspecting(null)}><div className={`inspect-modal ${inspecting.category.toLowerCase()}`} onClick={event=>event.stopPropagation()}>
        <button className="modal-close" onClick={()=>setInspecting(null)} aria-label="Close card view">×</button>
        <div className="inspect-art"><span>{inspecting.icon}</span><div className="art-caption">PLACEHOLDER ARTWORK</div></div>
        <div className="inspect-details"><div className="eyebrow">{inspecting.category} · COMMON</div><h2>{inspecting.name}</h2><p>{inspecting.copy}</p><div className="inspect-rule"><span>TARGET</span><strong>{inspecting.target}</strong></div><div className="inspect-rule"><span>WEEKLY SLOT</span><strong>{isChaosWeek?'No limits this week':inspecting.category==='UNIQUE'?`${pending.filter(c=>c.category==='UNIQUE').length} of 2 used`:`${pending.some(c=>c.category===inspecting.category)?'Filled':'Available'} · 1 allowed`}</strong></div><button className="primary inspect-play" onClick={chooseInspectedCard}>SELECT THIS CARD <span>→</span></button>{inspecting.copyId&&!pending.some(card=>card.id===inspecting.id)&&<button className="keep-card" onClick={()=>discardCard(inspecting)}>DISCARD UNTIL NEXT WEEK</button>}<button className="keep-card" onClick={()=>setInspecting(null)}>KEEP IT IN MY HAND</button></div>
      </div></div>}
      {targeting && <div className="modal-backdrop"><div className="target-modal"><div className="eyebrow">ELIGIBLE TARGETS ONLY</div><h2>Choose the target</h2><p>{targetCard?.name} can only be applied to the valid lineup choices shown below.</p>{eligibleTargets.length?eligibleTargets.map(target=><button onClick={()=>confirmPlay({rosterId:String(targetCard?.target.toLowerCase().includes('opponent')?away.id:home.id),playerId:gameWeek?.teams?.flatMap(team=>team.players).find(player=>target.label.includes(player.name))?.playerId||'',slot:target.badge})} key={target.label}><span>{target.badge}</span>{target.label}<b>→</b></button>):<div className="no-targets">No eligible target is available for this card.</div>}<button className="cancel" onClick={()=>{setTargeting(false);setSelected([])}}>CANCEL</button></div></div>}
      {toast && <div className="toast">⚡ {toast}</div>}
    </main>}
  </div>
}

function LeagueOnboarding({ onCreated }: { onCreated: (league: ChaosLeague) => void }) {
  const { user, logout } = useAuth()
  const [sleeperLeagueId, setSleeperLeagueId] = useState('')
  const [preview, setPreview] = useState<{ name: string; totalRosters?: number } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const review = async () => {
    const id = sleeperLeagueId.trim()
    if (!/^\d+$/.test(id)) { setError('Enter the numeric league ID from your Sleeper league URL.'); return }
    setBusy(true); setError(''); setPreview(null)
    try {
      const response = await fetch(`https://api.sleeper.app/v1/league/${id}`)
      if (!response.ok) throw new Error('Sleeper could not find that league.')
      const league = await response.json()
      setPreview({ name: league.name || 'Sleeper League', totalRosters: league.total_rosters })
    } catch (ex) { setError((ex as Error).message) }
    finally { setBusy(false) }
  }
  const create = async () => {
    if (!preview) return
    setBusy(true); setError('')
    try {
      const league = await apiFetch<ChaosLeague>('/api/chaos-leagues', { method:'POST', body:JSON.stringify({ sleeperLeagueId:sleeperLeagueId.trim(), name:preview.name }) })
      onCreated(league)
    } catch (ex) { setError((ex as Error).message) }
    finally { setBusy(false) }
  }

  return <main className="league-onboarding">
    <div className="onboarding-brand"><b>CC</b><span>CHAOS CARDS<small>FANTASY FOOTBALL</small></span></div>
    <section className="onboarding-card">
      <div className="eyebrow">WELCOME, {user?.name || 'COMMISSIONER'}</div>
      <h1>Create Your Chaos League</h1>
      <p>Connect an existing Sleeper league. The FantasyTools account creating it becomes the primary commissioner automatically.</p>
      <div className="owner-confirmation"><span>PRIMARY COMMISSIONER</span><b>{user?.email}</b><small>You can appoint co-commissioners after setup.</small></div>
      <label>SLEEPER LEAGUE ID<input value={sleeperLeagueId} onChange={event=>{setSleeperLeagueId(event.target.value);setPreview(null)}} placeholder="Example: 1301750882777456640" inputMode="numeric" /></label>
      <small className="id-help">Find this number at the end of your Sleeper league’s web address.</small>
      {!preview&&<button className="primary onboarding-action" disabled={busy} onClick={review}>{busy?'CHECKING SLEEPER…':'FIND MY SLEEPER LEAGUE →'}</button>}
      {preview&&<div className="league-preview"><span>LEAGUE FOUND</span><h2>{preview.name}</h2><p>{preview.totalRosters || '—'} Sleeper rosters</p><button className="primary onboarding-action" disabled={busy} onClick={create}>{busy?'CREATING LEAGUE…':'CREATE CHAOS CARDS LEAGUE →'}</button><button className="secondary" onClick={()=>setPreview(null)}>USE A DIFFERENT ID</button></div>}
      {error&&<div className="form-error">⚠ {error}</div>}
      <button className="onboarding-signout" onClick={logout}>Sign out and use a different account</button>
    </section>
    <aside className="join-note"><b>JOINING SOMEONE ELSE’S LEAGUE?</b><span>Your commissioner will send an invitation after creating the league.</span></aside>
  </main>
}

function InviteClaim({ leagueId, onJoined }: { leagueId:string; onJoined:(league:ChaosLeague)=>void }) {
  const [league,setLeague]=useState<ChaosLeague|null>(null), [teams,setTeams]=useState<Team[]>([]), [message,setMessage]=useState('Loading invitation…'), [busy,setBusy]=useState(false)
  useEffect(()=>{ apiFetch<ChaosLeague>(`/api/chaos-leagues/invite/${leagueId}`).then(async found=>{setLeague(found); const [users,rosters]=await Promise.all([fetch(`https://api.sleeper.app/v1/league/${found.sleeperLeagueId}/users`).then(r=>r.json()),fetch(`https://api.sleeper.app/v1/league/${found.sleeperLeagueId}/rosters`).then(r=>r.json())]); setTeams(users.map((u:any,i:number)=>{const r=rosters.find((x:any)=>x.owner_id===u.user_id);const name=u.metadata?.team_name||`${u.display_name||u.username}'s Team`;return {...mockTeams[i%mockTeams.length],id:r?.roster_id||i+1,sleeperUserId:u.user_id,manager:u.display_name||u.username,name,initials:name.split(/\s+/).map((x:string)=>x[0]).join('').slice(0,2).toUpperCase()}}));setMessage('') }).catch(ex=>setMessage((ex as Error).message))},[leagueId])
  useEffect(()=>{if(!league)return;const check=()=>apiFetch<RosterClaim>(`/api/leagues/${leagueId}/rosters/claims/mine`).then(result=>{if(result.status==='APPROVED')onJoined(league);else if(result.status==='PENDING')setMessage('Waiting for commissioner approval… This page will open automatically when approved.');else if(result.status==='REJECTED')setMessage('Your previous request was declined. Select the correct roster to try again.')}).catch(()=>{});check();const timer=window.setInterval(check,3000);return()=>window.clearInterval(timer)},[league,leagueId,onJoined])
  const claim=async(team:Team)=>{setBusy(true);setMessage('');try{await apiFetch(`/api/chaos-leagues/invite/${leagueId}/join`,{method:'POST'});const result=await apiFetch<RosterClaim>(`/api/leagues/${leagueId}/rosters/claims`,{method:'POST',body:JSON.stringify({rosterId:team.id,sleeperUserId:team.sleeperUserId,sleeperManagerName:team.manager,sleeperTeamName:team.name})});if(result.status==='APPROVED'&&league)onJoined(league);else setMessage('Claim sent! Your commissioner must approve it before you enter the league.')}catch(ex){setMessage((ex as Error).message)}finally{setBusy(false)}}
  return <main className="league-onboarding"><section className="onboarding-card claim-picker"><div className="eyebrow">LEAGUE INVITATION</div><h1>{league?.name||'Chaos Cards'}</h1><p>Select your own Sleeper roster. The commissioner will approve your request.</p>{message&&<div className="setup-message">{message}</div>}<div className="claim-team-list">{teams.map(team=><button disabled={busy} onClick={()=>claim(team)} key={team.id}><TeamBadge team={team} small/><span><b>{team.name}</b><small>{team.manager}</small></span><em>CLAIM →</em></button>)}</div></section></main>
}

export default function App() {
  const [league, setLeague] = useState<ChaosLeague | null | undefined>(undefined)
  const inviteId=new URLSearchParams(window.location.search).get('invite')
  useEffect(() => { apiFetch<ChaosLeague>('/api/chaos-leagues/current').then(setLeague).catch(()=>setLeague(null)) }, [])
  if(inviteId) return <InviteClaim leagueId={inviteId} onJoined={created=>{window.history.replaceState({},'', '/');setLeague(created)}}/>
  if (league === undefined) return <main className="league-onboarding"><div className="onboarding-loading">LOADING YOUR CHAOS LEAGUE…</div></main>
  if (league === null) return <LeagueOnboarding onCreated={setLeague}/>
  return <LeagueGame league={league}/>
}
