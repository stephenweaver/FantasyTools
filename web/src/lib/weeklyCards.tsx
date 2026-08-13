import { useState } from 'react'
import { apiFetch } from './api'

export type WeeklyCard = { id?:string; week:number; name:string; artworkUrl:string; description:string; ruleType:string; amount:number; target:string; active:boolean }

export function WeeklyCardSeason({leagueId,cards,currentWeek,canEdit,onSaved}:{leagueId:string;cards:WeeklyCard[];currentWeek:number;canEdit:boolean;onSaved:(card:WeeklyCard)=>void}) {
  const [selectedWeek,setSelectedWeek]=useState(currentWeek)
  const [editing,setEditing]=useState(false)
  const selected=cards.find(card=>card.week===selectedWeek)
  return <main className="weekly-page">
    <section className="weekly-hero"><div><div className="eyebrow">LEAGUE-WIDE CHAOS</div><h1>Weekly Card</h1><p>Every scheduled card is public from the start of the season so managers can plan ahead.</p></div>
      <label>VIEW WEEK<select value={selectedWeek} onChange={e=>{setSelectedWeek(Number(e.target.value));setEditing(false)}}>{Array.from({length:18},(_,i)=><option key={i+1} value={i+1}>Week {i+1}{i+1===currentWeek?' · CURRENT':''}</option>)}</select></label></section>
    {selected?<section className="weekly-feature"><div className="weekly-art">{selected.artworkUrl?<img src={selected.artworkUrl}/>:<div>WEEK {selected.week}<small>ARTWORK COMING SOON</small></div>}</div><div><span>{selected.week===currentWeek?'CURRENT WEEKLY CARD':`WEEK ${selected.week}`}</span><h2>{selected.name}</h2><p>{selected.description}</p><dl><dt>APPLIES TO</dt><dd>{selected.target||'Entire league'}</dd><dt>RULE</dt><dd>{selected.ruleType||'Custom weekly rule'}{selected.amount?` · ${selected.amount}`:''}</dd></dl>{canEdit&&<button className="primary" onClick={()=>setEditing(true)}>EDIT WEEK {selected.week}</button>}</div></section>:<section className="weekly-empty"><h2>Week {selectedWeek} has not been scheduled yet.</h2>{canEdit&&<button className="primary" onClick={()=>setEditing(true)}>+ ADD WEEKLY CARD</button>}</section>}
    <section className="season-strip"><h3>FULL SEASON</h3><div>{Array.from({length:18},(_,i)=>{const card=cards.find(item=>item.week===i+1);return <button className={`${selectedWeek===i+1?'selected':''} ${i+1===currentWeek?'current':''}`} onClick={()=>{setSelectedWeek(i+1);setEditing(false)}} key={i+1}><b>W{i+1}</b><span>{card?.name||'Unscheduled'}</span></button>})}</div></section>
    {editing&&canEdit&&<WeeklyCardEditor leagueId={leagueId} week={selectedWeek} initial={selected} onClose={()=>setEditing(false)} onSaved={card=>{onSaved(card);setEditing(false)}}/>}
  </main>
}

function WeeklyCardEditor({leagueId,week,initial,onSaved,onClose}:{leagueId:string;week:number;initial?:WeeklyCard;onSaved:(card:WeeklyCard)=>void;onClose:()=>void}) {
  const [card,setCard]=useState<WeeklyCard>(initial||{week,name:'',artworkUrl:'',description:'',ruleType:'Custom weekly rule',amount:0,target:'Entire league',active:true})
  const [busy,setBusy]=useState(false),[error,setError]=useState('')
  const upload=async(file?:File)=>{if(!file)return;const body=new FormData();body.append('file',file);setBusy(true);try{const saved=await apiFetch<{url:string}>('/api/images/cards',{method:'POST',body});setCard({...card,artworkUrl:saved.url})}catch(ex){setError((ex as Error).message)}finally{setBusy(false)}}
  const save=async()=>{setBusy(true);setError('');try{onSaved(await apiFetch<WeeklyCard>(`/api/leagues/${leagueId}/cards/weekly/${week}`,{method:'PUT',body:JSON.stringify(card)}))}catch(ex){setError((ex as Error).message)}finally{setBusy(false)}}
  return <div className="modal-backdrop"><section className="weekly-editor"><button className="close" onClick={onClose}>×</button><div className="eyebrow">COMMISSIONER · WEEK {week}</div><h2>Schedule Weekly Card</h2><label>NAME<input value={card.name} onChange={e=>setCard({...card,name:e.target.value})}/></label><label>FINISHED ARTWORK<input type="file" accept="image/png,image/jpeg,image/webp" onChange={e=>upload(e.target.files?.[0])}/></label>{card.artworkUrl&&<img src={card.artworkUrl}/>}<label>FULL PUBLIC DESCRIPTION<textarea value={card.description} onChange={e=>setCard({...card,description:e.target.value})}/></label><div className="form-grid"><label>RULE TYPE<input value={card.ruleType} onChange={e=>setCard({...card,ruleType:e.target.value})}/></label><label>TARGET<input value={card.target} onChange={e=>setCard({...card,target:e.target.value})}/></label><label>AMOUNT<input type="number" value={card.amount} onChange={e=>setCard({...card,amount:Number(e.target.value)})}/></label></div>{error&&<p className="form-error">{error}</p>}<button className="primary" disabled={busy} onClick={save}>{busy?'SAVING…':'SAVE TO SEASON'}</button></section></div>
}
