import { Fragment, useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  AlertTriangle, Check, Crown, FileText, FolderSearch, Pencil, Play, Plus, RefreshCw, RotateCw, Search, Server, ShieldCheck, Square, Trash2, Wrench, Zap, X,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import {
  applyInstallationImport, browseFolders, collectNow, createInstallation, deleteInstallation, discoverPaths, discoverServices, enterMaintenance, executeServiceAction, exitMaintenance, getInstallationConfiguration, getMaintenanceStatus, previewInstallationImport, proposeComponent, session, setAutoStart, setExclusiveInstallation, updateInstallation,
} from '../api'
import type {
  BrowseResult, ComponentProposal, ComponentProposalResult, ComponentSnapshot, ComponentType, DashboardSummary, EnvironmentKind, HttpCheckConfiguration, ImportPreview, MaintenanceStatus, PathCandidate, SaveInstallationInput, ServiceAction, ServiceCandidate, TcpCheckConfiguration,
} from '../types'
import { environmentLabel, worstStatus } from '../lib/format'
import { serviceActionLabels, serviceActionAllowed, serviceStateTone, autoStartNote, serviceStatusLabel } from '../lib/services'
import { BusyOverlay, StatusBadge } from '../components/Primitives'

const componentTypeOptions: Array<{ value: ComponentType; label: string }> = [
  { value: 'AppServer', label: 'AppServer' },
  { value: 'Rest', label: 'REST / WebApp' },
  { value: 'Broker', label: 'Broker' },
  { value: 'Worker', label: 'Worker' },
  { value: 'Job', label: 'Job / integração' },
  { value: 'Tss', label: 'TSS' },
  { value: 'DbAccess', label: 'DBAccess' },
  { value: 'LicenseServer', label: 'License Server' },
  { value: 'HttpEndpoint', label: 'Endpoint HTTP(S)' },
  { value: 'WindowsService', label: 'Serviço Windows' },
  { value: 'WebApp', label: 'Aplicação web' },
  { value: 'Generic', label: 'Genérico' },
]

interface InstallationGroup {
  id: string
  name: string
  isExclusive: boolean
  autoStartEnabled: boolean
  components: ComponentSnapshot[]
}

export function Installations({ summary, refresh, addInstallation, editInstallation }: { summary: DashboardSummary; refresh: () => Promise<void>; addInstallation: () => void; editInstallation: (id: string) => void }) {
  const [importing, setImporting] = useState(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [maintenance, setMaintenance] = useState<MaintenanceStatus | null>(null)
  const [serviceBusyId, setServiceBusyId] = useState<string | null>(null)
  const [automationBusyId, setAutomationBusyId] = useState<string | null>(null)
  const isAdministrator = session.role === 'Administrator'
  const groups = useMemo(() => Object.values(summary.components.reduce<Record<string, InstallationGroup>>((result, item) => {
    const group = (result[item.installationId] ??= {
      id: item.installationId,
      name: item.installationName,
      isExclusive: Boolean(item.installationIsExclusive),
      autoStartEnabled: Boolean(item.installationAutoStartEnabled),
      components: [],
    })
    group.components.push(item)
    return result
  }, {})), [summary.components])
  const exclusiveGroup = groups.find(group => group.isExclusive)

  const loadMaintenance = useCallback(async () => {
    try { setMaintenance(await getMaintenanceStatus()) } catch { setMaintenance(null) }
  }, [])
  useEffect(() => { void loadMaintenance() }, [loadMaintenance])

  const toggleMaintenance = async () => {
    const entering = !(maintenance?.active ?? false)
    const exclusiveName = exclusiveGroup?.name ?? maintenance?.exclusiveInstallation?.name
    const confirmed = window.confirm(entering
      ? exclusiveName
        ? `Entrar em modo manutenção? Todos os serviços Windows monitorados serão PARADOS e os de “${exclusiveName}” serão REINICIADOS, derrubando as sessões conectadas para que o ambiente fique exclusivo para compilar e salvar configurações. Os alertas ficarão suspensos.`
        : 'Entrar em modo manutenção? Todos os serviços Windows monitorados serão PARADOS e os alertas ficarão suspensos.'
      : 'Encerrar o modo manutenção? Os serviços monitorados serão iniciados novamente.')
    if (!confirmed) return
    setBusy(true); setError(null); setMessage(null)
    try {
      const result = entering ? await enterMaintenance() : await exitMaintenance()
      const failures = result.services.filter(item => !item.success)
      const stopped = result.services.filter(item => item.action === 'stop' && item.success).length
      const restarted = result.services.filter(item => item.action === 'restart' && item.success).length
      const summaryText = entering
        ? `Modo manutenção ativado. ${stopped} serviço(s) parado(s)${restarted > 0 ? ` e ${restarted} reiniciado(s) em “${result.exclusiveInstallation?.name ?? exclusiveName}”, sem sessões remanescentes` : ''}`
        : `Modo manutenção encerrado. ${result.services.length - failures.length} serviço(s) iniciado(s)`
      if (failures.length > 0) setError(`${summaryText}; falhas: ${failures.map(item => `${item.serviceName} (${item.message})`).join('; ')}`)
      else setMessage(`${summaryText}.`)
      await loadMaintenance()
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível alterar o modo manutenção.')
    } finally { setBusy(false) }
  }

  const toggleExclusive = async (group: InstallationGroup) => {
    const enabling = !group.isExclusive
    if (enabling && !window.confirm(`Tornar “${group.name}” a instalação exclusiva? No modo manutenção ela é reiniciada e permanece como o único ambiente no ar para compilar e salvar configurações.`)) return
    setAutomationBusyId(group.id); setError(null); setMessage(null)
    try {
      const result = await setExclusiveInstallation(group.id, enabling)
      setMessage(result.isExclusive
        ? `“${result.name}” agora é a instalação exclusiva do modo manutenção.`
        : `“${result.name}” não é mais a instalação exclusiva.`)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível definir a instalação exclusiva.')
    } finally { setAutomationBusyId(null) }
  }

  const toggleAutoStart = async (group: InstallationGroup) => {
    setAutomationBusyId(group.id); setError(null); setMessage(null)
    try {
      const result = await setAutoStart(group.id, !group.autoStartEnabled)
      setMessage(result.autoStartEnabled
        ? `Auto-start ativado em “${result.name}”: serviços que caírem sobem automaticamente.`
        : `Auto-start desativado em “${result.name}”.`)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível alterar o auto-start.')
    } finally { setAutomationBusyId(null) }
  }

  const runServiceAction = async (component: ComponentSnapshot, action: ServiceAction) => {
    const verb = action === 'start' ? 'Iniciar' : action === 'stop' ? 'Parar' : 'Reiniciar'
    if (!serviceActionAllowed(component.windowsServiceStatus, action)) return
    if (action !== 'start' && !window.confirm(`${verb} o serviço “${component.windowsServiceName}” de ${component.name}?`)) return
    setServiceBusyId(component.id); setError(null); setMessage(null)
    try {
      const outcome = await executeServiceAction(component.id, action)
      const failures = outcome.results.filter(item => !item.success)
      if (failures.length > 0) setError(`Falha em ${component.name}: ${failures.map(item => `${item.serviceName}: ${item.message}`).join('; ')}`)
      else setMessage(`${verb} concluído em ${component.name}: ${outcome.results.map(item => `${item.serviceName} → ${item.status}`).join(', ')}.`)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível executar a ação de serviço.')
    } finally { setServiceBusyId(null) }
  }

  const runCollection = async () => {
    setBusy(true); setError(null); setMessage(null)
    try {
      const result = await collectNow()
      setMessage(`Coleta concluída em ${result.processedComponents} componente(s).`)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível executar a coleta.')
    } finally { setBusy(false) }
  }

  const remove = async (id: string, name: string) => {
    if (!window.confirm(`Remover a instalação “${name}” e seu histórico?`)) return
    setBusy(true); setError(null); setMessage(null)
    try {
      await deleteInstallation(id)
      setMessage('Instalação removida.')
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível remover a instalação.')
    } finally { setBusy(false) }
  }

  const busyLabel = serviceBusyId !== null
    ? 'Aplicando ação no serviço…'
    : automationBusyId !== null
      ? 'Aplicando automação…'
      : busy ? 'Aplicando alteração…' : null

  return <>
    <div className="page-body">
    {busyLabel && <BusyOverlay label={busyLabel} />}
    <section className="intro-row"><div><h2>Ambientes cadastrados</h2><p>Configure serviços, arquivos, portas e URLs sem sair do painel.</p></div><div className="intro-actions">{isAdministrator && <button className={maintenance?.active ? 'primary-button' : 'danger-button'} disabled={busy || summary.demoMode} onClick={() => void toggleMaintenance()}><Wrench size={16} /> {maintenance?.active ? 'Encerrar manutenção' : 'Modo manutenção'}</button>}<button className="secondary-button" disabled={busy || summary.demoMode} onClick={() => void runCollection()}><Play size={16} /> {busy ? 'Executando…' : 'Coletar agora'}</button>{isAdministrator && <button className="secondary-button" onClick={() => setImporting(true)}><FileText size={16} /> Importar arquivo</button>}<button className="primary-button" onClick={addInstallation}><Plus size={16} /> Adicionar instalação</button></div></section>
    {maintenance?.active && <div className="maintenance-banner"><Wrench size={16} /> Modo manutenção ativo{maintenance.endsAt ? ` até ${new Date(maintenance.endsAt).toLocaleString('pt-BR')}` : ''}: serviços monitorados parados e alertas suspensos.{maintenance.exclusiveInstallation ? ` Somente “${maintenance.exclusiveInstallation.name}” segue no ar, reiniciado no início da janela para compilar e salvar configurações sem sessões antigas.` : ''}</div>}
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <article className="panel installation-table">{groups.map(group => {
      const { id: installationId, name, components } = group
      const isDemo = components.every(item => item.isDemo)
      const canAutomate = isAdministrator && !isDemo && !summary.demoMode
      const healthy = components.filter(item => item.status === 'Healthy').length
      return <Fragment key={installationId}>
        <div className="install-line">
          <div className="install-line-identity">
            <strong>{name}</strong>
            <span className="environment-tag">{environmentLabel(components[0]?.installationEnvironment)}</span>
            {group.isExclusive && <span className="exclusive-tag"><Crown size={12} /> Exclusivo</span>}
            {group.autoStartEnabled && <span className="auto-start-tag"><Zap size={12} /> Auto-start</span>}
          </div>
          <span className="install-line-stat"><strong>{components.length}</strong> componentes · <strong>{healthy}</strong> saudáveis</span>
          <StatusBadge status={worstStatus(components)} />
          <div className="install-line-actions">
            {canAutomate && <button className={group.isExclusive ? 'chip-button active' : 'chip-button'} disabled={busy || automationBusyId === installationId} aria-pressed={group.isExclusive} title="Instalação reiniciada no início da manutenção, que permanece como o único ambiente no ar" onClick={() => void toggleExclusive(group)}><Crown size={13} /> Exclusivo</button>}
            {canAutomate && <button className={group.autoStartEnabled ? 'chip-button active' : 'chip-button'} disabled={busy || automationBusyId === installationId} aria-pressed={group.autoStartEnabled} title="Sobe automaticamente os serviços deste ambiente quando eles caem. Um serviço parado pelo painel fica de fora até ser iniciado pelo painel." onClick={() => void toggleAutoStart(group)}><Zap size={13} /> Auto-start</button>}
            {!isDemo && installationId && <button className="row-action" title={`Configurar ${name}`} aria-label={`Configurar ${name}`} onClick={() => editInstallation(installationId)}><Pencil size={15} /></button>}
            {!isDemo && installationId && <button className="row-action danger" title={`Remover ${name}`} aria-label={`Remover ${name}`} disabled={busy} onClick={() => void remove(installationId, name)}><Trash2 size={15} /></button>}
          </div>
        </div>
        {components.map(component => <div className="component-line" key={component.id}>
          <i className={`status-dot ${component.status.toLowerCase()}`} />
          <span className="component-line-name">{component.name}</span>
          {component.windowsServiceName && <small className={`service-state ${serviceStateTone(component.windowsServiceStatus)}`}><i />{serviceStatusLabel(component.windowsServiceStatus)}{group.autoStartEnabled ? autoStartNote(component) : ''}</small>}
          <small className="component-line-summary">{component.summary}</small>
          {isAdministrator && !component.isDemo && component.windowsServiceName && <span className="mini-component-actions"><ServiceActionButton component={component} action="start" icon={Play} busy={serviceBusyId === component.id} disabled={busy} run={runServiceAction} /><ServiceActionButton component={component} action="restart" icon={RotateCw} busy={serviceBusyId === component.id} disabled={busy} run={runServiceAction} /><ServiceActionButton component={component} action="stop" icon={Square} busy={serviceBusyId === component.id} disabled={busy} run={runServiceAction} /></span>}
        </div>)}
      </Fragment>
    })}</article>
  </div>
    {importing && <ImportInstallationsDialog close={() => setImporting(false)} onImported={async () => { setImporting(false); await refresh() }} />}
  </>
}

function ServiceActionButton({ component, action, icon: Icon, busy, disabled, run }: {
  component: ComponentSnapshot
  action: ServiceAction
  icon: LucideIcon
  busy: boolean
  disabled: boolean
  run: (component: ComponentSnapshot, action: ServiceAction) => Promise<void>
}) {
  const verb = serviceActionLabels[action]
  const allowed = serviceActionAllowed(component.windowsServiceStatus, action)
  const title = allowed
    ? `${verb} ${component.windowsServiceName}`
    : `${component.windowsServiceName} já está ${serviceStatusLabel(component.windowsServiceStatus).toLowerCase()}`
  return <button
    className={`row-action ${allowed ? '' : 'is-current-state'}`}
    title={title}
    aria-label={`${verb} serviço de ${component.name}`}
    disabled={disabled || busy || !allowed}
    onClick={() => void run(component, action)}
  >{busy && action === 'start' ? <RefreshCw className="spin" size={14} /> : <Icon size={14} />}</button>
}

/// Quem lista as pastas é o serviço, não o navegador: o browser não entrega caminho
/// absoluto por segurança, e um seletor nativo abriria o disco de quem está olhando a
/// tela — que, com o acesso remoto ligado, é outra máquina.
function FolderPicker({ initial, onPick, onClose }: { initial: string; onPick: (path: string) => void; onClose: () => void }) {
  const [result, setResult] = useState<BrowseResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const go = useCallback(async (path?: string) => {
    setLoading(true)
    try {
      setResult(await browseFolders(path))
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível listar a pasta.')
    } finally { setLoading(false) }
  }, [])
  useEffect(() => { void go(initial.trim() === '' ? undefined : initial.trim()) }, [go, initial])

  return <div className="modal-backdrop" onClick={onClose}>
    <section className="modal-card folder-picker" role="dialog" aria-modal="true" aria-labelledby="folder-picker-title" onClick={event => event.stopPropagation()}>
      <header className="modal-header">
        <div><span>Pastas do servidor</span><h2 id="folder-picker-title">Escolher pasta</h2><p>{result?.current ?? 'Unidades disponíveis no servidor onde o Pulse está instalado.'}</p></div>
        <button className="icon-button" onClick={onClose} aria-label="Fechar seleção de pasta"><X size={18} /></button>
      </header>
      {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
      {loading
        ? <div className="modal-loading"><RefreshCw className="spin" size={20} /> Lendo…</div>
        : <>
          {result?.looksLikeProtheus && <div className="success-banner"><Check size={16} /> Esta pasta tem arquivos de AppServer. Pode selecionar aqui.</div>}
          <ul className="folder-list">
            {result?.parent && <li><button type="button" onClick={() => void go(result.parent!)}><FolderSearch size={15} /> .. voltar</button></li>}
            {result?.current === null && result.entries.length === 0 && <li className="folder-empty">Nenhuma unidade disponível.</li>}
            {result?.current !== null && result?.entries.length === 0 && <li className="folder-empty">Nenhuma subpasta acessível aqui.</li>}
            {result?.entries.map(entry => <li key={entry.path}><button type="button" onClick={() => void go(entry.path)}><FolderSearch size={15} /> {entry.name}</button></li>)}
          </ul>
        </>}
      <footer className="modal-footer">
        <button type="button" className="secondary-button" onClick={onClose}>Cancelar</button>
        <button type="button" className="primary-button" disabled={!result?.current} onClick={() => { if (result?.current) { onPick(result.current); onClose() } }}>Usar esta pasta</button>
      </footer>
    </section>
  </div>
}

/// Aponte a pasta do Protheus e o Pulse propõe um componente por appserver.ini que achar,
/// com ambiente, executável, console.log, serviço do Windows e os alvos de rede que o
/// próprio INI declara. Preencher campo por campo era o passo mais lento do cadastro.
function FolderAutoDetect({ onDetected }: { onDetected: (proposal: ComponentProposal) => void }) {
  const [folder, setFolder] = useState('')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<ComponentProposalResult | null>(null)
  const [picking, setPicking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function detect() {
    if (folder.trim() === '') return
    setBusy(true)
    setResult(null)
    try {
      setResult(await proposeComponent(folder.trim()))
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível varrer a pasta.')
    } finally { setBusy(false) }
  }

  return <div className="auto-detect">
    <div className="auto-detect-row">
      <label className="wide-field">Pasta do Protheus
        <input
          aria-label="Pasta para detecção automática"
          value={folder}
          onChange={event => setFolder(event.target.value)}
          onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); void detect() } }}
          placeholder={'D:\\Protheus\\Protheus'} />
      </label>
      <button type="button" className="secondary-button" onClick={() => setPicking(true)}><FolderSearch size={15} /> Procurar…</button>
      <button type="button" className="primary-button" disabled={busy || folder.trim() === ''} onClick={() => void detect()}>{busy ? 'Procurando…' : 'Detectar'}</button>
    </div>
    {picking && <FolderPicker initial={folder} onPick={setFolder} onClose={() => setPicking(false)} />}
    {result && result.proposals.length === 0 && <p className="field-hint">Nenhum appserver.ini reconhecido em {result.filesInspected} arquivos. Confira a pasta ou preencha manualmente.</p>}
    {result && result.proposals.length > 0 && <>
      <p className="field-hint">{result.proposals.length} encontrado(s) em {result.filesInspected} arquivos. Clique para preencher o formulário — nada é gravado antes de você revisar e salvar.</p>
      <ul className="proposal-list">{result.proposals.map(proposal => <li key={proposal.iniPath ?? proposal.suggestedName}>
        <button type="button" onClick={() => onDetected(proposal)}>
          <div className="proposal-identity">
            <strong>{proposal.suggestedName}</strong>
            <span>{[proposal.environmentName, proposal.databaseKind, proposal.windowsServiceName].filter(Boolean).join(' · ') || proposal.iniPath}</span>
          </div>
          <ul className="proposal-checks">{proposal.checks.map(check => <li key={`${check.host}:${check.port}`} className={check.isRequired ? 'required' : ''}>{check.label} <strong>{check.port}</strong></li>)}</ul>
        </button>
      </li>)}</ul>
    </>}
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
  </div>
}

interface TcpCheckDraft extends TcpCheckConfiguration { key: number }
interface HttpCheckDraft extends HttpCheckConfiguration { key: number }
interface ComponentDraft {
  key: number
  id?: string
  name: string
  type: ComponentType
  isRequired: boolean
  windowsServiceName: string
  executablePath: string
  iniPath: string
  logPaths: string[]
  tcpChecks: TcpCheckDraft[]
  httpChecks: HttpCheckDraft[]
}

let draftKey = 0
const nextDraftKey = () => ++draftKey
const emptyComponent = (): ComponentDraft => ({
  key: nextDraftKey(), name: '', type: 'AppServer', isRequired: true, windowsServiceName: '',
  executablePath: '', iniPath: '', logPaths: [], tcpChecks: [], httpChecks: [],
})

/// Conferir antes de aplicar: o arquivo cadastra vários ambientes de uma vez, e o erro
/// só aparecendo depois de gravar metade seria pior que não ter a importação.
function ImportInstallationsDialog({ close, onImported }: { close: () => void; onImported: () => Promise<void> }) {
  const [format, setFormat] = useState('yaml')
  const [content, setContent] = useState('')
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const check = async () => {
    setBusy(true)
    setError(null)
    try {
      setPreview(await previewInstallationImport(format, content))
    } catch (reason) {
      setPreview(null)
      setError(reason instanceof Error ? reason.message : 'Não foi possível conferir o arquivo.')
    } finally { setBusy(false) }
  }

  const apply = async () => {
    setBusy(true)
    setError(null)
    try {
      await applyInstallationImport(format, content)
      await onImported()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível importar.')
    } finally { setBusy(false) }
  }

  const readFile = (file: File | undefined) => {
    if (!file) return
    setFormat(file.name.toLowerCase().endsWith('.json') ? 'json' : 'yaml')
    void file.text().then(text => { setContent(text); setPreview(null) })
  }

  return <div className="modal-backdrop">
    <section className="modal-card" role="dialog" aria-modal="true" aria-labelledby="import-dialog-title">
      <header className="modal-header">
        <div>
          <span>Importação em massa</span>
          <h2 id="import-dialog-title">Importar instalações</h2>
          <p>Um arquivo com vários ambientes e componentes, conferido antes de gravar.</p>
        </div>
        <button className="icon-button" onClick={close} disabled={busy} aria-label="Fechar importação"><X size={18} /></button>
      </header>
      <form onSubmit={event => { event.preventDefault(); void check() }}>
        {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
        <div className="form-grid">
          <label>Formato
            <select aria-label="Formato do arquivo" value={format} onChange={event => { setFormat(event.target.value); setPreview(null) }}>
              <option value="yaml">YAML</option>
              <option value="json">JSON</option>
            </select>
          </label>
          <label>Arquivo
            <input type="file" aria-label="Arquivo de importação" accept=".yaml,.yml,.json" onChange={event => readFile(event.target.files?.[0])} />
          </label>
        </div>
        <label className="import-content">Conteúdo
          <textarea aria-label="Conteúdo do arquivo" value={content} rows={14} spellCheck={false}
            onChange={event => { setContent(event.target.value); setPreview(null) }}
            placeholder={'schemaVersion: 1\ninstallations:\n  - name: ERP Produção\n    environment: Production\n    components:\n      - name: AppServer\n        type: AppServer'} />
        </label>

        {preview && <div className={preview.valid ? 'success-banner' : 'form-error'}>
          {preview.valid ? <Check size={16} /> : <AlertTriangle size={16} />}
          {preview.valid
            ? `${preview.installationCount} instalação(ões) e ${preview.componentCount} componente(s) prontos para importar.`
            : 'O arquivo tem problemas e não pode ser aplicado.'}
        </div>}
        {preview && preview.errors.length > 0 && <ul className="import-messages errors">{preview.errors.map(item => <li key={item}>{item}</li>)}</ul>}
        {preview && preview.warnings.length > 0 && <ul className="import-messages">{preview.warnings.map(item => <li key={item}>{item}</li>)}</ul>}

        <footer className="modal-actions">
          <button type="button" className="secondary-button" onClick={close} disabled={busy}>Cancelar</button>
          <button type="submit" className="secondary-button" disabled={busy || !content.trim()}>{busy ? 'Conferindo…' : 'Conferir arquivo'}</button>
          <button type="button" className="primary-button" disabled={busy || !preview?.valid} onClick={() => void apply()}>
            {busy ? 'Importando…' : 'Importar'}
          </button>
        </footer>
      </form>
    </section>
  </div>
}

export function InstallationDialog({ installationId, close, onSaved }: { installationId: string | null; close: () => void; onSaved: () => Promise<void> }) {
  const [name, setName] = useState('')
  const [environment, setEnvironment] = useState<EnvironmentKind>('Production')
  const [customEnvironmentName, setCustomEnvironmentName] = useState('')
  const [tags, setTags] = useState('')
  const [components, setComponents] = useState<ComponentDraft[]>([emptyComponent()])
  const [loading, setLoading] = useState(Boolean(installationId))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!installationId) return
    setLoading(true)
    getInstallationConfiguration(installationId).then(configuration => {
      setName(configuration.name)
      setEnvironment(configuration.environment)
      setCustomEnvironmentName(configuration.customEnvironmentName ?? '')
      setTags(configuration.tags.join(', '))
      setComponents(configuration.components.map(component => ({
        ...component,
        key: nextDraftKey(),
        windowsServiceName: component.windowsServiceName ?? '',
        executablePath: component.executablePath ?? '',
        iniPath: component.iniPath ?? '',
        tcpChecks: component.tcpChecks.map(check => ({ ...check, key: nextDraftKey() })),
        httpChecks: component.httpChecks.map(check => ({ ...check, key: nextDraftKey() })),
      })))
    }).catch(reason => setError(reason instanceof Error ? reason.message : 'Não foi possível carregar a configuração.'))
      .finally(() => setLoading(false))
  }, [installationId])

  const updateComponent = (key: number, update: Partial<Omit<ComponentDraft, 'key'>>) => setComponents(current => current.map(item => item.key === key ? { ...item, ...update } : item))
  const addComponent = () => setComponents(current => [...current, emptyComponent()])
  const removeComponent = (key: number) => setComponents(current => current.filter(item => item.key !== key))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    const missingTarget = components.find(component => !component.windowsServiceName.trim()
      && !component.executablePath.trim() && !component.iniPath.trim()
      && component.logPaths.every(path => !path.trim())
      && component.tcpChecks.length === 0 && component.httpChecks.length === 0)
    if (missingTarget) {
      setError(`Configure ao menos um alvo no componente “${missingTarget.name || 'sem nome'}”.`)
      return
    }

    const input: SaveInstallationInput = {
      name,
      environment,
      customEnvironmentName: environment === 'Custom' ? customEnvironmentName : undefined,
      tags: tags.split(',').map(item => item.trim()).filter(Boolean),
      components: components.map(component => ({
        id: component.id,
        name: component.name,
        type: component.type,
        isRequired: component.isRequired,
        windowsServiceName: component.windowsServiceName.trim() || undefined,
        executablePath: component.executablePath.trim() || undefined,
        iniPath: component.iniPath.trim() || undefined,
        logPaths: component.logPaths.map(path => path.trim()).filter(Boolean),
        tcpChecks: component.tcpChecks.map(({ key: _, ...check }) => check),
        httpChecks: component.httpChecks.map(({ key: _, ...check }) => ({ ...check, bodyPattern: check.bodyPattern?.trim() || undefined })),
      })),
    }
    setBusy(true)
    try {
      if (installationId) await updateInstallation(installationId, input)
      else await createInstallation(input)
      await onSaved()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar a instalação.')
    } finally { setBusy(false) }
  }

  return <div className="modal-backdrop">
    <section className="modal-card configuration-modal" role="dialog" aria-modal="true" aria-labelledby="installation-dialog-title">
      <header className="modal-header"><div><span>Configuração local completa</span><h2 id="installation-dialog-title">{installationId ? 'Configurar instalação' : 'Adicionar instalação'}</h2><p>Defina os alvos reais que serão consultados em modo somente leitura.</p></div><button className="icon-button" onClick={close} disabled={busy} aria-label="Fechar cadastro"><X size={18} /></button></header>
      {loading ? <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando configuração…</div> : <form onSubmit={submit}>
        {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
        <div className="form-grid">
          <label>Nome da instalação<input aria-label="Nome da instalação" value={name} onChange={event => setName(event.target.value)} maxLength={160} placeholder="Ex.: ERP Produção" required /></label>
          <label>Ambiente<select aria-label="Ambiente" value={environment} onChange={event => setEnvironment(event.target.value as EnvironmentKind)}><option value="Production">Produção</option><option value="Homologation">Homologação</option><option value="Development">Desenvolvimento</option><option value="Custom">Personalizado</option></select></label>
          {environment === 'Custom' && <label>Nome do ambiente<input aria-label="Nome do ambiente personalizado" value={customEnvironmentName} onChange={event => setCustomEnvironmentName(event.target.value)} maxLength={80} required /></label>}
          <label className={environment === 'Custom' ? '' : 'wide-field'}>Tags opcionais<input aria-label="Tags opcionais" value={tags} onChange={event => setTags(event.target.value)} placeholder="matriz, servidor-a" /></label>
        </div>
        <div className="component-editor">
          <div className="component-editor-heading"><div><h3>Componentes e alvos</h3><p>Serviço, executável, INI, logs, TCP e HTTP podem ser combinados.</p></div><button type="button" className="secondary-button" onClick={addComponent}><Plus size={15} /> Adicionar componente</button></div>
          {components.map((component, index) => <ComponentConfigurationEditor key={component.key} component={component} index={index} update={update => updateComponent(component.key, update)} remove={() => removeComponent(component.key)} canRemove={components.length > 1} />)}
        </div>
        <div className="modal-safety"><ShieldCheck size={18} /><span>A descoberta apenas lista candidatos e a coleta é somente leitura. Ações de iniciar ou parar serviços são explícitas, auditadas e restritas a administradores.</span></div>
        <footer className="modal-actions"><button type="button" className="secondary-button" onClick={close} disabled={busy}>Cancelar</button><button className="primary-button" disabled={busy}>{busy ? <RefreshCw className="spin" size={16} /> : <Check size={16} />}{busy ? 'Salvando…' : 'Salvar e monitorar'}</button></footer>
      </form>}
    </section>
  </div>
}

function ComponentConfigurationEditor({ component, index, update, remove, canRemove }: { component: ComponentDraft; index: number; update: (update: Partial<Omit<ComponentDraft, 'key'>>) => void; remove: () => void; canRemove: boolean }) {
  const [serviceQuery, setServiceQuery] = useState(component.windowsServiceName || component.name)
  const [serviceCandidates, setServiceCandidates] = useState<ServiceCandidate[]>([])
  const [pathRoot, setPathRoot] = useState('')
  const [fileNames, setFileNames] = useState(defaultFileNames(component.type))
  const [pathCandidates, setPathCandidates] = useState<PathCandidate[]>([])
  const [discoveryBusy, setDiscoveryBusy] = useState(false)
  const [discoveryError, setDiscoveryError] = useState<string | null>(null)

  const findServices = async () => {
    setDiscoveryError(null)
    if (serviceQuery.trim().length < 2) { setDiscoveryError('Informe ao menos dois caracteres para buscar serviços.'); return }
    setDiscoveryBusy(true)
    try { setServiceCandidates((await discoverServices(serviceQuery.trim())).candidates) }
    catch (reason) { setDiscoveryError(reason instanceof Error ? reason.message : 'Falha na descoberta de serviços.') }
    finally { setDiscoveryBusy(false) }
  }

  const findPaths = async () => {
    setDiscoveryError(null)
    const names = fileNames.split(',').map(item => item.trim()).filter(Boolean)
    if (!pathRoot.trim() || names.length === 0) { setDiscoveryError('Informe uma pasta inicial e ao menos um nome de arquivo.'); return }
    setDiscoveryBusy(true)
    try { setPathCandidates((await discoverPaths(pathRoot.trim(), names)).candidates) }
    catch (reason) { setDiscoveryError(reason instanceof Error ? reason.message : 'Falha na descoberta de caminhos.') }
    finally { setDiscoveryBusy(false) }
  }

  const addLog = (path: string) => update({ logPaths: [...new Set([...component.logPaths, path])] })
  const addTcp = () => update({ tcpChecks: [...component.tcpChecks, { key: nextDraftKey(), host: '127.0.0.1', port: 0, timeoutMs: 3000, isRequired: true }] })
  const addHttp = () => update({ httpChecks: [...component.httpChecks, { key: nextDraftKey(), url: '', method: 'GET', expectedStatusMin: 200, expectedStatusMax: 399, timeoutMs: 5000, validateTls: true, certificateWarningDays: 30, isRequired: true }] })

  return <article className="component-config-card">
    <header><span>{index + 1}</span><div><strong>{component.name || 'Novo componente'}</strong><small>Configure um ou mais alvos de leitura</small></div><button type="button" className="row-action remove-component" onClick={remove} disabled={!canRemove} aria-label={`Remover componente ${index + 1}`}><X size={17} /></button></header>
    <div className="target-form-grid basic-target-grid">
      <label>Nome<input aria-label={`Nome do componente ${index + 1}`} value={component.name} onChange={event => update({ name: event.target.value })} maxLength={160} placeholder="Ex.: AppServer REST" required /></label>
      <label>Tipo<select aria-label={`Tipo do componente ${index + 1}`} value={component.type} onChange={event => { const type = event.target.value as ComponentType; update({ type }); setFileNames(defaultFileNames(type)) }}>{componentTypeOptions.map(option => <option value={option.value} key={option.value}>{option.label}</option>)}</select></label>
      <label className="checkbox-label"><input type="checkbox" checked={component.isRequired} onChange={event => update({ isRequired: event.target.checked })} /> Obrigatório</label>
    </div>

    <section className="target-section"><div className="target-section-heading"><div><h4>Serviço Windows</h4><p>Use o nome interno do serviço, não apenas o nome exibido.</p></div></div>
      <div className="discovery-row"><input aria-label={`Buscar serviço do componente ${index + 1}`} value={serviceQuery} onChange={event => setServiceQuery(event.target.value)} placeholder="Ex.: AppServer" /><button type="button" className="secondary-button" disabled={discoveryBusy} onClick={() => void findServices()}><Search size={14} /> Buscar no servidor</button></div>
      {serviceCandidates.length > 0 && <div className="candidate-list">{serviceCandidates.slice(0, 20).map(candidate => <button type="button" key={candidate.serviceName} onClick={() => update({ windowsServiceName: candidate.serviceName })}><span><strong>{candidate.displayName}</strong><small>{candidate.serviceName} · {candidate.status}</small></span><Check size={14} /></button>)}</div>}
      <label className="target-field">Serviço selecionado<input aria-label={`Nome do serviço Windows ${index + 1}`} value={component.windowsServiceName} onChange={event => update({ windowsServiceName: event.target.value })} placeholder="Opcional" /></label>
    </section>

    <section className="target-section"><div className="target-section-heading"><div><h4>Arquivos e logs</h4><p>Informe caminhos absolutos locais ou UNC. Nenhuma unidade mapeada é usada.</p></div><FolderSearch size={17} /></div>
      <FolderAutoDetect onDetected={proposal => update({
        name: proposal.suggestedName,
        windowsServiceName: proposal.windowsServiceName ?? component.windowsServiceName,
        executablePath: proposal.executablePath ?? component.executablePath,
        iniPath: proposal.iniPath ?? component.iniPath,
        logPaths: proposal.logPaths.length > 0 ? proposal.logPaths : component.logPaths,
        tcpChecks: proposal.checks.length > 0
          ? proposal.checks.map(check => ({ key: nextDraftKey(), host: check.host, port: check.port, timeoutMs: 3_000, isRequired: check.isRequired }))
          : component.tcpChecks
      })} />
      <div className="path-discovery-grid"><label>Pasta inicial<input aria-label={`Pasta para descoberta ${index + 1}`} value={pathRoot} onChange={event => setPathRoot(event.target.value)} placeholder="D:\TOTVS\Protheus" /></label><label>Nomes exatos<input aria-label={`Arquivos para descoberta ${index + 1}`} value={fileNames} onChange={event => setFileNames(event.target.value)} /></label><button type="button" className="secondary-button" disabled={discoveryBusy} onClick={() => void findPaths()}><FolderSearch size={14} /> Localizar</button></div>
      {pathCandidates.length > 0 && <div className="path-candidates">{pathCandidates.slice(0, 20).map(candidate => <div key={candidate.path}><span title={candidate.path}>{candidate.path}</span><div>{candidate.fileName.toLowerCase().endsWith('.exe') && <button type="button" onClick={() => update({ executablePath: candidate.path })}>Executável</button>}{candidate.fileName.toLowerCase().endsWith('.ini') && <button type="button" onClick={() => update({ iniPath: candidate.path })}>INI</button>}<button type="button" onClick={() => addLog(candidate.path)}>Log</button></div></div>)}</div>}
      <div className="target-form-grid"><label>Executável<input aria-label={`Caminho do executável ${index + 1}`} value={component.executablePath} onChange={event => update({ executablePath: event.target.value })} placeholder="Opcional" /></label><label>Arquivo INI<input aria-label={`Caminho do INI ${index + 1}`} value={component.iniPath} onChange={event => update({ iniPath: event.target.value })} placeholder="Opcional" /></label><label className="wide-field">Logs, um caminho por linha<textarea aria-label={`Caminhos de log ${index + 1}`} value={component.logPaths.join('\n')} onChange={event => update({ logPaths: event.target.value.split(/\r?\n/) })} placeholder={'D:\\TOTVS\\Protheus\\logs\\console.log'} /></label></div>
    </section>

    <section className="target-section"><div className="target-section-heading"><div><h4>Portas TCP</h4><p>Verifica conectividade sem enviar comandos ao Protheus.</p></div><button type="button" className="secondary-button" onClick={addTcp}><Plus size={14} /> Porta</button></div>
      {component.tcpChecks.map((check, checkIndex) => <div className="check-row tcp-row" key={check.key}><label>Host<input aria-label={`Host TCP ${index + 1}.${checkIndex + 1}`} value={check.host} onChange={event => update({ tcpChecks: component.tcpChecks.map(item => item.key === check.key ? { ...item, host: event.target.value } : item) })} required /></label><label>Porta<input aria-label={`Porta TCP ${index + 1}.${checkIndex + 1}`} type="number" min="1" max="65535" value={check.port || ''} onChange={event => update({ tcpChecks: component.tcpChecks.map(item => item.key === check.key ? { ...item, port: Number(event.target.value) } : item) })} required /></label><label>Timeout (ms)<input type="number" min="250" max="30000" value={check.timeoutMs} onChange={event => update({ tcpChecks: component.tcpChecks.map(item => item.key === check.key ? { ...item, timeoutMs: Number(event.target.value) } : item) })} /></label><button type="button" className="row-action remove-component" aria-label={`Remover porta TCP ${checkIndex + 1}`} onClick={() => update({ tcpChecks: component.tcpChecks.filter(item => item.key !== check.key) })}><X size={16} /></button></div>)}
    </section>

    <section className="target-section"><div className="target-section-heading"><div><h4>Endpoints HTTP/HTTPS</h4><p>Somente GET ou HEAD, sem redirecionamentos.</p></div><button type="button" className="secondary-button" onClick={addHttp}><Plus size={14} /> Endpoint</button></div>
      {component.httpChecks.map((check, checkIndex) => <div className="http-check" key={check.key}><div className="check-row http-row"><label>URL<input aria-label={`URL HTTP ${index + 1}.${checkIndex + 1}`} type="url" value={check.url} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, url: event.target.value } : item) })} placeholder="http://127.0.0.1:porta/rota" required /></label><label>Método<select value={check.method} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, method: event.target.value as 'GET' | 'HEAD' } : item) })}><option>GET</option><option>HEAD</option></select></label><label>Status mínimo<input type="number" min="100" max="599" value={check.expectedStatusMin} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, expectedStatusMin: Number(event.target.value) } : item) })} /></label><label>Status máximo<input type="number" min="100" max="599" value={check.expectedStatusMax} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, expectedStatusMax: Number(event.target.value) } : item) })} /></label><button type="button" className="row-action remove-component" aria-label={`Remover endpoint HTTP ${checkIndex + 1}`} onClick={() => update({ httpChecks: component.httpChecks.filter(item => item.key !== check.key) })}><X size={16} /></button></div><div className="http-options"><label>Texto esperado<input value={check.bodyPattern ?? ''} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, bodyPattern: event.target.value } : item) })} placeholder="Opcional" /></label><label>Timeout (ms)<input type="number" min="250" max="30000" value={check.timeoutMs} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, timeoutMs: Number(event.target.value) } : item) })} /></label><label className="checkbox-label"><input type="checkbox" checked={check.validateTls} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, validateTls: event.target.checked } : item) })} /> Validar TLS</label><label className="checkbox-label"><input type="checkbox" checked={check.isRequired} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, isRequired: event.target.checked } : item) })} /> Obrigatório</label></div></div>)}
    </section>
    {discoveryError && <div className="inline-error"><AlertTriangle size={14} /> {discoveryError}</div>}
  </article>
}

function defaultFileNames(type: ComponentType) {
  if (type === 'DbAccess') return 'dbaccess.exe, dbaccess.ini, dbaccess.log'
  if (type === 'LicenseServer') return 'licenseserver.exe, appserver.ini, console.log'
  if (type === 'Tss') return 'appserver.exe, appserver.ini, console.log'
  return 'appserver.exe, appserver.ini, console.log'
}
