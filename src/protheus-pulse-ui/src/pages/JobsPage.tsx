import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  AlertTriangle, BriefcaseBusiness, Check, CircleHelp, LockKeyhole, RefreshCw, RotateCw, Trash2,
} from 'lucide-react'
import {
  createHeartbeatDefinition, deleteHeartbeatDefinition, getHeartbeatDefinitions, rotateHeartbeatToken, session,
} from '../api'
import type {
  ComponentSnapshot, HealthStatus, HeartbeatDefinition, HeartbeatToken,
} from '../types'
import { formatRelative } from '../lib/format'
import { groupComponentsByInstallation } from '../lib/installations'
import { PanelHeader, StatusBadge } from '../components/Primitives'

function heartbeatIntervalLabel(seconds: number) {
  if (seconds < 60) return `${seconds} s`
  if (seconds < 3_600) return `${Math.round(seconds / 60)} min`
  if (seconds % 3_600 === 0) return `${seconds / 3_600} h`
  return `${(seconds / 3_600).toFixed(1)} h`
}

/// Atrasado é passar do intervalo esperado mais a tolerância cadastrada — a mesma
/// conta que o coletor faz. A tela mostrava sempre "5 min" independentemente disso.
function heartbeatStatus(definition: HeartbeatDefinition): { status: HealthStatus; label: string } {
  if (!definition.lastHeartbeatAt) return { status: 'Unknown', label: 'Sem sinal ainda' }
  const elapsed = (Date.now() - new Date(definition.lastHeartbeatAt).getTime()) / 1_000
  const limit = definition.expectedIntervalSeconds + definition.toleranceSeconds
  if (elapsed > limit * 2) return { status: 'Critical', label: 'Muito atrasado' }
  if (elapsed > limit) return { status: 'Warning', label: 'Atrasado' }
  return { status: 'Healthy', label: 'Em dia' }
}

export function JobsPage({ components }: { components: ComponentSnapshot[] }) {
  const [definitions, setDefinitions] = useState<HeartbeatDefinition[] | null>(null)
  const [issued, setIssued] = useState<HeartbeatToken | null>(null)
  const [draft, setDraft] = useState({ componentId: '', name: '', jobKey: '', interval: '3600', tolerance: '600' })
  const [busy, setBusy] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const isAdministrator = session.role === 'Administrator'

  const installations = useMemo(() => groupComponentsByInstallation(components), [components])

  const load = useCallback(async () => {
    try {
      setDefinitions(await getHeartbeatDefinitions())
      setError(null)
    } catch (reason) {
      setDefinitions([])
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os heartbeats.')
    }
  }, [])
  useEffect(() => { void load() }, [load])

  useEffect(() => {
    if (!draft.componentId && components.length > 0) setDraft(current => ({ ...current, componentId: components[0].id }))
  }, [components, draft.componentId])

  async function run(id: string | null, action: () => Promise<void>, success: string) {
    if (id) setBusyId(id); else setBusy(true)
    setMessage(null)
    try {
      await action()
      setMessage(success)
      setError(null)
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'A operação não foi concluída.')
    } finally { setBusyId(null); setBusy(false) }
  }

  const create = (event: FormEvent) => {
    event.preventDefault()
    void run(null, async () => {
      const token = await createHeartbeatDefinition({
        componentId: draft.componentId,
        name: draft.name.trim(),
        jobKey: draft.jobKey.trim(),
        expectedIntervalSeconds: Number(draft.interval),
        toleranceSeconds: Number(draft.tolerance),
      })
      setIssued(token)
      setDraft(current => ({ ...current, name: '', jobKey: '' }))
    }, 'Heartbeat cadastrado.')
  }

  return <div className="page-body">
    <section className="intro-row">
      <div><h2>Heartbeats de jobs</h2><p>O job avisa que rodou; o Pulse cobra quando o aviso não chega dentro do intervalo mais a tolerância.</p></div>
      <a className="secondary-button" href="https://github.com/jeanvga/protheus-pulse/blob/main/docs/HEARTBEATS.md" target="_blank" rel="noreferrer"><CircleHelp size={16} /> Como integrar</a>
    </section>

    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}

    {issued && <article className="panel settings-panel">
      <PanelHeader title="Token do heartbeat" subtitle="Aparece uma única vez; guarde antes de fechar" />
      <div className="settings-form">
        <div className="token-reveal">
          <span>Chave pública do job</span><code>{issued.jobKey}</code>
          <span>Token</span><code>{issued.token}</code>
        </div>
        <div className="inline-warning"><AlertTriangle size={15} /> {issued.warning}</div>
        <div className="form-actions"><button type="button" className="secondary-button" onClick={() => setIssued(null)}>Já guardei</button></div>
      </div>
    </article>}

    {definitions === null && <article className="panel"><div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando heartbeats…</div></article>}

    {definitions?.length === 0 && <article className="panel"><div className="tab-empty">
      <BriefcaseBusiness size={22} />
      <div><strong>Nenhum job monitorado</strong><p>Cadastre o job aqui, guarde o token e faça a rotina do Protheus chamar <code>POST /api/v1/heartbeats/&#123;chave&#125;</code> ao terminar. Sem heartbeat, um job que parou de rodar não gera alerta — nada falha, simplesmente nada acontece.</p></div>
    </div></article>}

    {definitions?.map(definition => {
      const state = heartbeatStatus(definition)
      return <article className="panel job-card" key={definition.id}>
        <div className="job-icon"><BriefcaseBusiness size={21} /></div>
        <div>
          <span>{definition.componentName} · {definition.installationName}</span>
          <h3>{definition.name}</h3>
          <p><code>{definition.jobKey}</code>{definition.windowStart && definition.windowEnd ? ` · janela ${definition.windowStart.slice(0, 5)}–${definition.windowEnd.slice(0, 5)}` : ''}</p>
        </div>
        <div className="job-metrics">
          <div><span>Último sinal</span><strong>{definition.lastHeartbeatAt ? formatRelative(definition.lastHeartbeatAt) : '—'}</strong></div>
          <div><span>Esperado a cada</span><strong>{heartbeatIntervalLabel(definition.expectedIntervalSeconds)}</strong></div>
          <div><span>Tolerância</span><strong>{heartbeatIntervalLabel(definition.toleranceSeconds)}</strong></div>
          <StatusBadge status={state.status} label={state.label} />
          {isAdministrator && <div className="mini-component-actions">
            <button type="button" className="row-action" disabled={busyId === definition.id} aria-label={`Rotacionar token de ${definition.name}`}
              onClick={() => { if (window.confirm(`Gerar um token novo para “${definition.name}”? O token atual para de funcionar assim que o novo for gerado.`)) void run(definition.id, async () => { setIssued(await rotateHeartbeatToken(definition.id)) }, 'Token rotacionado.') }}><RotateCw size={14} /></button>
            <button type="button" className="row-action danger" disabled={busyId === definition.id} aria-label={`Remover ${definition.name}`}
              onClick={() => { if (window.confirm(`Remover o heartbeat “${definition.name}”? O job deixa de ser cobrado.`)) void run(definition.id, () => deleteHeartbeatDefinition(definition.id), 'Heartbeat removido.') }}><Trash2 size={14} /></button>
          </div>}
        </div>
      </article>
    })}

    {isAdministrator && <article className="panel settings-panel">
      <PanelHeader title="Novo heartbeat" subtitle="A chave pública vai na URL que o job chama; o token vai no cabeçalho" />
      <form className="settings-form" onSubmit={create}>
        <div className="form-grid">
          <label>Componente
            <select aria-label="Componente do heartbeat" value={draft.componentId} onChange={event => setDraft({ ...draft, componentId: event.target.value })} required>
              {installations.map(installation => <optgroup key={installation.id} label={installation.name}>
                {installation.components.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
              </optgroup>)}
            </select>
          </label>
          <label>Nome<input aria-label="Nome do heartbeat" value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} maxLength={160} placeholder="Ex.: Fechamento contábil" required /></label>
          <label>Chave pública<input aria-label="Chave pública do job" value={draft.jobKey} onChange={event => setDraft({ ...draft, jobKey: event.target.value })} maxLength={80} placeholder="fechamento-contabil" required /></label>
          <label>Esperado a cada (segundos)<input type="number" aria-label="Intervalo esperado" min={30} max={86400} value={draft.interval} onChange={event => setDraft({ ...draft, interval: event.target.value })} required /></label>
          <label>Tolerância (segundos)<input type="number" aria-label="Tolerância" min={0} max={86400} value={draft.tolerance} onChange={event => setDraft({ ...draft, tolerance: event.target.value })} required /></label>
        </div>
        <p className="field-hint">O alerta abre quando passa do intervalo somado à tolerância sem sinal. A chave pública aparece na URL e não é segredo; o token, que autentica a chamada, é mostrado uma única vez ao salvar.</p>
        <div className="form-actions"><button className="primary-button" type="submit" disabled={busy || components.length === 0}>{busy ? 'Salvando…' : 'Cadastrar heartbeat'}</button></div>
      </form>
    </article>}

    {!isAdministrator && definitions !== null && definitions.length > 0 && <div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente leitura</strong><p>Cadastrar heartbeat, rotacionar token e remover exige o perfil Administrator.</p></div></div>}
  </div>
}
