import { useCallback, useEffect, useState } from 'react'
import {
  Activity, AlertTriangle, Check, HeartPulse, LockKeyhole, RefreshCw,
} from 'lucide-react'
import {
  getDiagnostics, session,
} from '../api'
import type {
  DiagnosticsInfo, HealthStatus,
} from '../types'
import { PanelHeader, Diagnostic } from '../components/Primitives'

export function DiagnosticsPage({ demo }: { demo: boolean }) {
  const [info, setInfo] = useState<DiagnosticsInfo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const isAdministrator = session.role === 'Administrator'

  const load = useCallback(async () => {
    if (!isAdministrator) { setLoading(false); return }
    setLoading(true)
    try {
      setInfo(await getDiagnostics())
      setError(null)
    } catch (reason) {
      setInfo(null)
      setError(reason instanceof Error ? reason.message : 'Não foi possível consultar o diagnóstico.')
    } finally { setLoading(false) }
  }, [isAdministrator])

  useEffect(() => { void load() }, [load])

  if (!isAdministrator) {
    return <div className="page-body"><div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente administradores</strong><p>O diagnóstico expõe caminho de dados, plataforma e estado do banco. Só o perfil Administrator pode consultá-lo.</p></div></div></div>
  }

  // Sem resposta do servidor não há como afirmar que algo está saudável.
  const reachable = info !== null
  const databaseStatus: HealthStatus = !reachable ? 'Unknown' : info.status
  const collectorStatus: HealthStatus = !reachable ? 'Unknown' : demo ? 'Unknown' : 'Healthy'

  return <div className="page-body">
    <section className="toolbar">
      <span className="refresh-hint"><Activity size={15} /> Consultado a cada abertura da aba</span>
      <button className="secondary-button" disabled={loading} onClick={() => void load()}><RefreshCw size={16} /> {loading ? 'Consultando…' : 'Consultar de novo'}</button>
    </section>

    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}

    <div className="diagnostic-grid">
      <Diagnostic
        title="Serviço web"
        status={reachable ? 'Healthy' : 'Critical'}
        detail={reachable ? `${info.service} respondeu a esta consulta` : 'A API não respondeu; o painel está sem contato com o serviço.'} />
      <Diagnostic
        title="Banco local"
        status={databaseStatus}
        detail={reachable ? `${info.database} ${info.status === 'Healthy' ? 'disponível com o esquema aplicado' : 'não respondeu à verificação de conexão'}` : 'Sem resposta do serviço.'} />
      <Diagnostic
        title="Plataforma"
        status={reachable ? 'Healthy' : 'Unknown'}
        detail={reachable ? `${info.platform} · versão ${info.version}` : 'Sem resposta do serviço.'} />
      <Diagnostic
        title="Coletores reais"
        status={collectorStatus}
        detail={!reachable ? 'Sem resposta do serviço.' : demo ? 'Desativados no modo demonstração.' : 'Agendador somente leitura ativo.'} />
    </div>

    {reachable && info.notes.length > 0 && <article className="panel settings-panel">
      <PanelHeader title="O que este serviço faz e não faz" subtitle="Limites declarados pelo próprio serviço" />
      <ul className="diagnostic-notes">{info.notes.map(note => <li key={note}><Check size={14} /> {note}</li>)}</ul>
    </article>}

    {demo && <div className="demo-notice"><HeartPulse size={22} /><div><strong>Modo demonstração ativo</strong><p>Todos os alvos e eventos exibidos são sintéticos e claramente marcados.</p></div></div>}
  </div>
}
