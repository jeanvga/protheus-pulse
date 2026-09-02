import { useCallback, useEffect, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import {
  AlertTriangle, Archive, Bell, Boxes, Check, ChevronDown, Clock3, Cpu, HardDrive, LockKeyhole, Mail, RefreshCw, Send, ShieldCheck, Trash2, UserRound,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import {
  createBackup, createSelfSignedCertificate, createUser, deleteUser, downloadBackup, getBackups, getEmailSettings, getNetworkSettings, getRetentionSettings, getServerThresholds, getUsers, resetUserPassword, saveEmailSettings, saveNetworkSettings, saveRetentionSettings, saveServerThresholds, sendTestEmail, session, updateUser,
} from '../api'
import type {
  BackupFile, EmailSettings, NetworkSettings, PulseUser, RetentionSettings, ServerThresholdSettings, SmtpSecurity,
} from '../types'
import { formatBytes } from '../lib/format'
import { PanelHeader } from '../components/Primitives'

/// Seções fechadas por padrão: a aba inteira aberta de uma vez virava uma tela sem fim.
function SettingsSection({ icon: Icon, title, summary, children }: { icon: LucideIcon; title: string; summary: string; children: ReactNode }) {
  const [open, setOpen] = useState(false)
  return <article className={`panel settings-section ${open ? 'is-open' : ''}`}>
    <button type="button" className="settings-section-head" aria-expanded={open} onClick={() => setOpen(current => !current)}>
      <span className="settings-section-icon"><Icon size={18} /></span>
      <div><h3>{title}</h3><p>{summary}</p></div>
      <ChevronDown className="settings-section-caret" size={17} />
    </button>
    {open && <div className="settings-section-body">{children}</div>}
  </article>
}

const backupRetention = 10

function formatBackupAge(createdAt: string) {
  const days = Math.floor((Date.now() - new Date(createdAt).getTime()) / 86_400_000)
  if (days <= 0) return 'hoje'
  if (days === 1) return 'ontem'
  return `há ${days} dias`
}

function BackupSettingsCard() {
  const [backups, setBackups] = useState<BackupFile[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [downloading, setDownloading] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setBackups(await getBackups())
      setError(null)
    } catch (reason) {
      setBackups([])
      setError(reason instanceof Error ? reason.message : 'Não foi possível listar os backups.')
    }
  }, [])
  useEffect(() => { void load() }, [load])

  async function create() {
    setBusy(true)
    setMessage(null)
    try {
      const created = await createBackup()
      setMessage(`Backup ${created.name} gerado com ${formatBytes(created.sizeBytes)}.`)
      setError(null)
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível gerar o backup.')
    } finally { setBusy(false) }
  }

  async function download(name: string) {
    setDownloading(name)
    try {
      await downloadBackup(name)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível baixar o backup.')
    } finally { setDownloading(null) }
  }

  const latest = backups?.[0]
  const stale = latest ? (Date.now() - new Date(latest.createdAt).getTime()) / 86_400_000 > 7 : false

  return <div className="settings-form">
    <p className="field-hint">O pacote leva o banco, a <strong>chave de Data Protection</strong> e a configuração de rede. A chave é o item que costuma faltar num backup feito à mão: sem ela, o banco restaurado perde as URLs dos pontos de contato e a senha do SMTP, que ficam cifradas por ela. O banco é copiado com <code>VACUUM INTO</code>, então sai consistente mesmo com a coleta rodando.</p>

    {backups?.length === 0 && <div className="inline-warning"><AlertTriangle size={15} /> Nenhum backup gerado até agora. Se o disco falhar, o cadastro dos ambientes, o histórico e as contas se perdem.</div>}
    {stale && latest && <div className="inline-warning"><AlertTriangle size={15} /> O backup mais recente é de {formatBackupAge(latest.createdAt)}.</div>}

    {backups && backups.length > 0 && <ul className="entity-list backup-list">
      {backups.map(item => <li className="entity-row" key={item.name}>
        <div className="entity-main">
          <strong>{item.name}</strong>
          <small>{new Date(item.createdAt).toLocaleString('pt-BR')} · {formatBytes(item.sizeBytes)} · {formatBackupAge(item.createdAt)}</small>
        </div>
        <div className="entity-actions">
          <button type="button" className="secondary-button" disabled={downloading === item.name} onClick={() => void download(item.name)}>
            {downloading === item.name ? 'Baixando…' : 'Baixar'}
          </button>
        </div>
      </li>)}
    </ul>}

    <p className="field-hint">Os {backupRetention} pacotes mais recentes ficam guardados no servidor; os antigos são apagados quando um novo é gerado. Baixe uma cópia para fora da máquina — um backup que mora no disco que falhou não serve.</p>
    <p className="field-hint">A restauração não é feita pelo painel de propósito: trocar o banco embaixo de um serviço em execução corrompe o que estiver sendo escrito. O passo a passo vai dentro do pacote, no arquivo <code>LEIAME.txt</code>.</p>

    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <div className="form-actions"><button type="button" className="primary-button" disabled={busy} onClick={() => void create()}>{busy ? 'Gerando…' : 'Gerar backup agora'}</button></div>
  </div>
}

function ServerThresholdsCard() {
  const [draft, setDraft] = useState({ cpuWarning: '80', cpuCritical: '92', memoryWarning: '85', memoryCritical: '94', diskWarning: '15', diskCritical: '5' })
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const apply = (settings: ServerThresholdSettings) => {
    setDraft({
      cpuWarning: String(settings.cpuWarningPercent),
      cpuCritical: String(settings.cpuCriticalPercent),
      memoryWarning: String(settings.memoryWarningPercent),
      memoryCritical: String(settings.memoryCriticalPercent),
      diskWarning: String(settings.diskFreeWarningPercent),
      diskCritical: String(settings.diskFreeCriticalPercent),
    })
    setUpdatedAt(settings.updatedAt)
  }

  useEffect(() => {
    void (async () => {
      try {
        apply(await getServerThresholds())
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os limites.')
      } finally { setLoading(false) }
    })()
  }, [])

  async function save(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      apply(await saveServerThresholds({
        cpuWarningPercent: Number(draft.cpuWarning),
        cpuCriticalPercent: Number(draft.cpuCritical),
        memoryWarningPercent: Number(draft.memoryWarning),
        memoryCriticalPercent: Number(draft.memoryCritical),
        diskFreeWarningPercent: Number(draft.diskWarning),
        diskFreeCriticalPercent: Number(draft.diskCritical),
      }))
      setMessage('Limites salvos. O próximo ciclo de coleta já usa os novos valores.')
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar os limites.')
    } finally { setBusy(false) }
  }

  if (loading) return <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando limites…</div>

  return <form className="settings-form" onSubmit={event => void save(event)}>
    <p className="field-hint">São os mesmos limites que pintam a aba Servidor e que classificam o estado das verificações de processador, memória e disco. Até aqui só mudavam editando o <code>appsettings.json</code> no servidor e reiniciando o serviço.</p>
    <div className="form-grid">
      <label>Processador · atenção (%)<input type="number" aria-label="Atenção do processador" min={1} max={100} value={draft.cpuWarning} onChange={event => setDraft({ ...draft, cpuWarning: event.target.value })} required /></label>
      <label>Processador · crítico (%)<input type="number" aria-label="Crítico do processador" min={1} max={100} value={draft.cpuCritical} onChange={event => setDraft({ ...draft, cpuCritical: event.target.value })} required /></label>
      <label>Memória · atenção (%)<input type="number" aria-label="Atenção da memória" min={1} max={100} value={draft.memoryWarning} onChange={event => setDraft({ ...draft, memoryWarning: event.target.value })} required /></label>
      <label>Memória · crítico (%)<input type="number" aria-label="Crítico da memória" min={1} max={100} value={draft.memoryCritical} onChange={event => setDraft({ ...draft, memoryCritical: event.target.value })} required /></label>
      <label>Disco livre · atenção (%)<input type="number" aria-label="Atenção do disco livre" min={0} max={100} value={draft.diskWarning} onChange={event => setDraft({ ...draft, diskWarning: event.target.value })} required /></label>
      <label>Disco livre · crítico (%)<input type="number" aria-label="Crítico do disco livre" min={0} max={100} value={draft.diskCritical} onChange={event => setDraft({ ...draft, diskCritical: event.target.value })} required /></label>
    </div>
    <p className="field-hint">Processador e memória medem <strong>uso</strong>: o crítico fica acima da atenção. Disco mede o espaço <strong>livre</strong>: o crítico fica abaixo. Uma regra de alerta com limite próprio ignora estes valores e usa o dela.</p>
    {updatedAt && <p className="field-hint">Alterado pela última vez em {new Date(updatedAt).toLocaleString('pt-BR')}.</p>}
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Salvar limites'}</button></div>
  </form>
}

function RetentionSettingsCard() {
  const [settings, setSettings] = useState<RetentionSettings | null>(null)
  const [historyDays, setHistoryDays] = useState('30')
  const [aggregationDays, setAggregationDays] = useState('7')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const loaded = await getRetentionSettings()
        setSettings(loaded)
        setHistoryDays(String(loaded.historyRetentionDays))
        setAggregationDays(String(loaded.metricAggregationAfterDays))
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar a retenção.')
      }
    })()
  }, [])

  async function save(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      await saveRetentionSettings({ historyRetentionDays: Number(historyDays), metricAggregationAfterDays: Number(aggregationDays) })
      setMessage('Retenção salva. A limpeza roda diariamente e na próxima execução já usa o novo prazo.')
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar a retenção.')
    } finally { setBusy(false) }
  }

  const stored = settings?.counts
  return <form className="settings-form" onSubmit={event => void save(event)}>
    <p className="field-hint">O Pulse guarda o histórico em SQLite no próprio servidor. Sem prazo, o arquivo cresce sem teto: a limpeza diária apaga probes, eventos de log e métricas mais antigos que o prazo, e preserva usuários, configuração e auditoria.</p>
    {stored && <div className="retention-counts">
      <div><span>Verificações</span><strong>{stored.probeResults.toLocaleString('pt-BR')}</strong></div>
      <div><span>Eventos de log</span><strong>{stored.logEvents.toLocaleString('pt-BR')}</strong></div>
      <div><span>Amostras de métrica</span><strong>{stored.metricSamples.toLocaleString('pt-BR')}</strong></div>
    </div>}
    <div className="form-grid">
      <label>Guardar histórico por (dias)<input type="number" min={1} max={365} value={historyDays} onChange={event => setHistoryDays(event.target.value)} /></label>
      <label>Agregar métricas por hora após (dias)<input type="number" min={1} max={365} value={aggregationDays} onChange={event => setAggregationDays(event.target.value)} /></label>
    </div>
    <p className="field-hint">Entre 1 e 365 dias. A agregação não pode passar do tamanho do histórico: amostras detalhadas viram médias por hora depois desse prazo, o que reduz o banco sem perder a tendência.</p>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Salvar retenção'}</button></div>
  </form>
}

function UsersSettingsCard() {
  const [users, setUsers] = useState<PulseUser[]>([])
  const [draft, setDraft] = useState({ username: '', displayName: '', email: '', password: '', role: 'Viewer' })
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setUsers(await getUsers())
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as contas.')
    }
  }, [])
  useEffect(() => { void load() }, [load])

  async function run(action: () => Promise<void>, success: string) {
    setBusy(true)
    setMessage(null)
    try {
      await action()
      setMessage(success)
      setError(null)
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'A operação não foi concluída.')
    } finally { setBusy(false) }
  }

  return <div className="settings-form">
    <p className="field-hint">Administrator controla serviços e configuração; Operator reconhece alertas e opera a manutenção; Viewer só enxerga. O painel nunca fica sem um administrador ativo: a última conta com esse perfil não pode ser rebaixada, desativada nem removida.</p>
    <ul className="user-list">{users.map(user => <li key={user.id}>
      <div className="user-line-identity"><strong>{user.displayName}</strong><span>{user.username}{user.email ? ` · ${user.email}` : ''}</span></div>
      {!user.isActive && <span className="user-inactive">Inativa</span>}
      <select aria-label={`Perfil de ${user.username}`} value={user.role} disabled={busy} onChange={event => void run(() => updateUser(user.id, { role: event.target.value }), 'Perfil atualizado.')}>
        <option value="Administrator">Administrator</option>
        <option value="Operator">Operator</option>
        <option value="Viewer">Viewer</option>
      </select>
      <button type="button" className="row-action" title={user.isActive ? `Desativar ${user.username}` : `Reativar ${user.username}`} aria-label={user.isActive ? `Desativar ${user.username}` : `Reativar ${user.username}`} disabled={busy} onClick={() => void run(() => updateUser(user.id, { role: user.role, isActive: !user.isActive }), user.isActive ? 'Conta desativada.' : 'Conta reativada.')}><LockKeyhole size={15} /></button>
      <button type="button" className="row-action" title={`Trocar a senha de ${user.username}`} aria-label={`Trocar a senha de ${user.username}`} disabled={busy} onClick={() => {
        const password = window.prompt(`Nova senha para ${user.username}`)
        if (password) void run(() => resetUserPassword(user.id, password), 'Senha trocada.')
      }}><RefreshCw size={15} /></button>
      <button type="button" className="row-action danger" title={`Remover ${user.username}`} aria-label={`Remover ${user.username}`} disabled={busy} onClick={() => {
        if (window.confirm(`Remover a conta ${user.username}?`)) void run(() => deleteUser(user.id), 'Conta removida.')
      }}><Trash2 size={15} /></button>
    </li>)}</ul>
    <form className="form-grid" onSubmit={event => {
      event.preventDefault()
      void run(async () => {
        await createUser(draft)
        setDraft({ username: '', displayName: '', email: '', password: '', role: 'Viewer' })
      }, 'Conta criada.')
    }}>
      <label>Usuário<input required value={draft.username} onChange={event => setDraft({ ...draft, username: event.target.value })} placeholder="joao.silva" /></label>
      <label>Nome<input value={draft.displayName} onChange={event => setDraft({ ...draft, displayName: event.target.value })} placeholder="João Silva" /></label>
      <label>E-mail<input type="email" value={draft.email} onChange={event => setDraft({ ...draft, email: event.target.value })} placeholder="Opcional" /></label>
      <label>Senha<input required type="password" value={draft.password} onChange={event => setDraft({ ...draft, password: event.target.value })} /></label>
      <label>Perfil<select value={draft.role} onChange={event => setDraft({ ...draft, role: event.target.value })}><option value="Administrator">Administrator</option><option value="Operator">Operator</option><option value="Viewer">Viewer</option></select></label>
      <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Criar conta'}</button></div>
    </form>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
  </div>
}

function NetworkSettingsCard() {
  const [settings, setSettings] = useState<NetworkSettings | null>(null)
  const [allowRemote, setAllowRemote] = useState(false)
  const [port, setPort] = useState('5058')
  const [useHttps, setUseHttps] = useState(false)
  const [certificatePath, setCertificatePath] = useState('')
  const [certificatePassword, setCertificatePassword] = useState('')
  const [passwordTouched, setPasswordTouched] = useState(false)
  const [busy, setBusy] = useState(false)
  const [generating, setGenerating] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const apply = (loaded: NetworkSettings) => {
    setSettings(loaded)
    setAllowRemote(loaded.allowRemoteAccess)
    setPort(String(loaded.port))
    setUseHttps(loaded.useHttps)
    setCertificatePath(loaded.certificatePath ?? '')
  }

  useEffect(() => {
    void (async () => {
      try {
        apply(await getNetworkSettings())
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar o acesso pela rede.')
      }
    })()
  }, [])

  async function save(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      apply(await saveNetworkSettings({
        allowRemoteAccess: allowRemote,
        port: Number(port),
        useHttps,
        certificatePath: certificatePath.trim() || undefined,
        // Sem mexer no campo, a senha guardada continua valendo.
        certificatePassword: passwordTouched ? certificatePassword : undefined,
      }))
      setPasswordTouched(false)
      setCertificatePassword('')
      setMessage('Salvo. Reinicie o serviço ProtheusPulse para o novo endereço valer.')
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar.')
    } finally { setBusy(false) }
  }

  async function generate() {
    setGenerating(true)
    setMessage(null)
    try {
      const created = await createSelfSignedCertificate()
      setCertificatePath(created.path)
      setUseHttps(true)
      setPasswordTouched(true)
      setCertificatePassword('')
      setMessage(`Certificado gerado para ${created.subject}, válido até ${new Date(created.notAfter).toLocaleDateString('pt-BR')}. Salve para aplicar.`)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível gerar o certificado.')
    } finally { setGenerating(false) }
  }

  const exposedWithoutTls = allowRemote && !useHttps

  return <form className="settings-form" onSubmit={event => void save(event)}>
    <p className="field-hint">Por padrão o painel escuta apenas em <code>127.0.0.1</code> e só abre no próprio servidor. Liberar o acesso faz o serviço escutar em todas as interfaces, para abrir de outra máquina por <code>{useHttps ? 'https' : 'http'}://ip:porta</code>.</p>

    {exposedWithoutTls && <div className="network-warning"><ShieldCheck size={18} /><div><strong>Sem TLS, o tráfego vai legível.</strong> Senha e token de sessão passam em texto claro na rede, e por esta tela se controla serviço do Windows. Ligue o HTTPS abaixo antes de abrir para outros computadores.</div></div>}

    <label className="switch-field"><input type="checkbox" checked={allowRemote} onChange={event => setAllowRemote(event.target.checked)} /> Permitir acesso de outros computadores</label>
    <label className="switch-field"><input type="checkbox" checked={useHttps} onChange={event => setUseHttps(event.target.checked)} /> Servir por HTTPS</label>

    <div className="form-grid">
      <label>Porta<input type="number" aria-label="Porta" min={1024} max={65535} value={port} onChange={event => setPort(event.target.value)} /></label>
      {useHttps && <label>Senha do certificado
        <input type="password" aria-label="Senha do certificado" value={certificatePassword} placeholder={settings?.hasCertificatePassword ? 'Guardada; deixe em branco para manter' : 'Deixe em branco se não houver'}
          onChange={event => { setCertificatePassword(event.target.value); setPasswordTouched(true) }} />
      </label>}
      {useHttps && <label className="wide-field">Arquivo do certificado (.pfx)
        <input aria-label="Caminho do certificado" value={certificatePath} onChange={event => setCertificatePath(event.target.value)} placeholder="C:\\ProgramData\\ProtheusPulse\\certs\\pulse.pfx" />
      </label>}
    </div>

    {useHttps && <>
      <p className="field-hint">O arquivo precisa trazer a chave privada. O certificado é conferido ao salvar: se não abrir, a gravação é recusada em vez de deixar o serviço subir sem conseguir atender ninguém. A senha fica cifrada com Data Protection, fora do <code>network.json</code>.</p>
      {settings?.certificateValid === false && <div className="inline-warning"><AlertTriangle size={15} /> {settings.certificateMessage}</div>}
      {settings?.certificateValid && settings.certificateSubject && <div className="success-banner"><ShieldCheck size={16} /> {settings.certificateSubject} · válido até {new Date(settings.certificateNotAfter ?? '').toLocaleDateString('pt-BR')}</div>}
      <div className="form-actions">
        <button type="button" className="secondary-button" disabled={generating} onClick={() => void generate()}>
          {generating ? 'Gerando…' : 'Gerar certificado para esta máquina'}
        </button>
      </div>
      <p className="field-hint">O certificado gerado aqui é assinado pelo próprio servidor: o navegador avisa que não conhece quem assinou, e o tráfego deixa de ir em texto claro. Para não ter o aviso, use um certificado emitido pela autoridade da sua rede.</p>
    </>}

    {settings && <div className="network-addresses">
      <span>Escutando agora em <code>{settings.boundUrl}</code></span>
      {allowRemote && settings.localAddresses.length > 0 && <ul>{settings.localAddresses.map(address => <li key={address}><code>{address}</code></li>)}</ul>}
    </div>}
    <p className="field-hint">O instalador não cria regra de firewall. Para liberar a porta no Windows, execute como administrador: <code>netsh advfirewall firewall add rule name="Protheus Pulse" dir=in action=allow protocol=TCP localport={port}</code></p>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Salvar acesso'}</button></div>
  </form>
}

export function SettingsPage() {
  const isAdministrator = session.role === 'Administrator'
  const items = [{ icon: Clock3, title: 'Intervalos e retenção', text: '30 dias de histórico · agregação após 7 dias' }, { icon: UserRound, title: 'Usuários e perfis', text: 'Administrator, Operator e Viewer' }, { icon: Bell, title: 'Canais de notificação', text: 'Dashboard · E-mail · Webhook · Teams · Slack · Discord' }, { icon: ShieldCheck, title: 'Segurança', text: 'Bind local · HTTPS recomendado para acesso em rede' }]
  return <div className="page-body">
    {isAdministrator
      ? <><SettingsSection icon={Mail} title="Envio de e-mail" summary="Servidor SMTP, remetente, destinatários e teste de envio"><EmailSettingsCard /></SettingsSection><SettingsSection icon={Cpu} title="Limites do servidor" summary="A partir de quanto uso o processador, a memória e o disco entram em atenção e crítico"><ServerThresholdsCard /></SettingsSection><SettingsSection icon={Archive} title="Retenção de dados" summary="Por quanto tempo o histórico fica no banco antes de ser apagado"><RetentionSettingsCard /></SettingsSection><SettingsSection icon={HardDrive} title="Backup" summary="Banco, chave de cifra e configuração num pacote para levar para fora da máquina"><BackupSettingsCard /></SettingsSection><SettingsSection icon={UserRound} title="Usuários e perfis" summary="Contas de acesso ao painel e o que cada perfil pode fazer"><UsersSettingsCard /></SettingsSection><SettingsSection icon={Boxes} title="Acesso pela rede" summary="Abrir o painel de outro computador por http://ip:porta"><NetworkSettingsCard /></SettingsSection></>
      : <div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente administradores</strong><p>Os dados de envio de e-mail e os tokens dos agentes de log só aparecem para o perfil Administrator.</p></div></div>}
    <div className="settings-grid">{items.map(({ icon: Icon, title, text }) => <article className="panel setting-card" key={title}><span><Icon size={20} /></span><div><h3>{title}</h3><p>{text}</p></div></article>)}</div>
    <div className="read-only-notice"><ShieldCheck size={22} /><div><strong>Coleta segura e ações auditadas</strong><p>A coleta é somente leitura e não escreve nas pastas monitoradas. Iniciar, reiniciar ou parar serviços exige perfil Administrator e fica registrado na auditoria.</p></div></div>
  </div>
}

const smtpSecurityOptions: Array<{ value: SmtpSecurity; label: string; port: number; hint: string }> = [
  { value: 'Auto', label: 'Automático', port: 587, hint: 'Escolhe TLS implícito ou STARTTLS conforme a porta e o servidor.' },
  { value: 'StartTls', label: 'STARTTLS', port: 587, hint: 'Conexão aberta em texto puro e promovida a TLS. Porta usual: 587.' },
  { value: 'SslOnConnect', label: 'SSL/TLS implícito', port: 465, hint: 'TLS desde o primeiro byte, sem texto puro. Porta usual: 465.' },
  { value: 'None', label: 'Sem criptografia', port: 25, hint: 'Só em relay interno na mesma rede. A senha trafega legível.' },
]

const knownSmtpPorts = smtpSecurityOptions.map(option => option.port)

interface EmailDraft {
  enabled: boolean
  host: string
  port: string
  security: SmtpSecurity
  username: string
  fromAddress: string
  fromName: string
  recipients: string
  timeoutSeconds: string
  allowInvalidCertificate: boolean
  notifyAlerts: boolean
  notifyLogErrors: boolean
}

const emptyEmailDraft: EmailDraft = {
  enabled: false, host: '', port: '587', security: 'Auto', username: '', fromAddress: '', fromName: 'Protheus Pulse',
  recipients: '', timeoutSeconds: '20', allowInvalidCertificate: false, notifyAlerts: true, notifyLogErrors: true,
}

function toDraft(settings: EmailSettings): EmailDraft {
  return {
    enabled: settings.enabled,
    host: settings.host,
    port: String(settings.port),
    security: settings.security,
    username: settings.username ?? '',
    fromAddress: settings.fromAddress,
    fromName: settings.fromName ?? '',
    recipients: settings.recipients.join('\n'),
    timeoutSeconds: String(settings.timeoutSeconds),
    allowInvalidCertificate: settings.allowInvalidCertificate,
    notifyAlerts: settings.notifyAlerts,
    notifyLogErrors: settings.notifyLogErrors,
  }
}

function EmailSettingsCard() {
  const [draft, setDraft] = useState<EmailDraft>(emptyEmailDraft)
  const [hasPassword, setHasPassword] = useState(false)
  const [password, setPassword] = useState('')
  const [clearPassword, setClearPassword] = useState(false)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [testing, setTesting] = useState(false)
  const [configured, setConfigured] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const settings = await getEmailSettings()
      setDraft(toDraft(settings))
      setHasPassword(settings.hasPassword)
      setConfigured(settings.configured)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os dados de e-mail.')
    } finally {
      setLoading(false)
    }
  }, [])
  useEffect(() => { void load() }, [load])

  const update = (change: Partial<EmailDraft>) => setDraft(current => ({ ...current, ...change }))

  // Trocar o modo de segurança ajusta a porta, mas só quando ela ainda é uma das
  // portas padrão: uma porta digitada à mão não pode ser sobrescrita.
  const changeSecurity = (security: SmtpSecurity) => {
    const suggested = smtpSecurityOptions.find(option => option.value === security)?.port
    const keepPort = !knownSmtpPorts.includes(Number(draft.port))
    update({ security, port: keepPort || suggested === undefined ? draft.port : String(suggested) })
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true); setError(null); setMessage(null)
    try {
      await saveEmailSettings({
        enabled: draft.enabled,
        host: draft.host.trim(),
        port: Number(draft.port),
        security: draft.security,
        username: draft.username.trim() || undefined,
        password: clearPassword ? '' : password || undefined,
        fromAddress: draft.fromAddress.trim(),
        fromName: draft.fromName.trim() || undefined,
        recipients: draft.recipients.split(/[\n,;]/).map(item => item.trim()).filter(Boolean),
        timeoutSeconds: Number(draft.timeoutSeconds),
        allowInvalidCertificate: draft.allowInvalidCertificate,
        notifyAlerts: draft.notifyAlerts,
        notifyLogErrors: draft.notifyLogErrors,
      })
      setPassword('')
      setClearPassword(false)
      setMessage('Dados de envio salvos.')
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar os dados de e-mail.')
    } finally { setBusy(false) }
  }

  const test = async () => {
    setTesting(true); setError(null); setMessage(null)
    try {
      const result = await sendTestEmail()
      if (result.success) setMessage(`Teste enviado: ${result.message}`)
      else setError(`O teste falhou: ${result.message}`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível enviar o teste.')
    } finally { setTesting(false) }
  }

  const securityHint = smtpSecurityOptions.find(option => option.value === draft.security)?.hint

  return <article className="panel settings-panel">
    <PanelHeader title="Dados para envio de e-mail" subtitle="Servidor SMTP usado nos alertas e nos erros de log enviados pelos agentes" />
    {loading ? <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando…</div> : <form className="settings-form" onSubmit={submit}>
      {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
      {message && <div className="success-banner"><Check size={16} /> {message}</div>}

      <label className="checkbox-label toggle-row">
        <input type="checkbox" aria-label="Ativar envio de e-mail" checked={draft.enabled} onChange={event => update({ enabled: event.target.checked })} />
        <span><strong>Enviar e-mail</strong><small>Desligado, o Pulse continua registrando tudo, só não avisa por e-mail.</small></span>
      </label>

      <div className="target-form-grid">
        <label>Servidor SMTP<input aria-label="Servidor SMTP" value={draft.host} onChange={event => update({ host: event.target.value })} placeholder="smtp.suaempresa.com.br" maxLength={253} required /></label>
        <label>Porta<input aria-label="Porta SMTP" type="number" min="1" max="65535" value={draft.port} onChange={event => update({ port: event.target.value })} required /></label>
        <label>Segurança
          <select aria-label="Segurança SMTP" value={draft.security} onChange={event => changeSecurity(event.target.value as SmtpSecurity)}>
            {smtpSecurityOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </label>
        <label>Tempo limite (s)<input aria-label="Tempo limite SMTP" type="number" min="5" max="120" value={draft.timeoutSeconds} onChange={event => update({ timeoutSeconds: event.target.value })} /></label>
        {securityHint && <p className="field-hint wide-field">{securityHint}</p>}

        <label>Usuário<input aria-label="Usuário SMTP" autoComplete="off" value={draft.username} onChange={event => update({ username: event.target.value })} placeholder="Deixe vazio para relay sem autenticação" maxLength={200} /></label>
        <label>Senha
          <input
            aria-label="Senha SMTP"
            type="password"
            autoComplete="new-password"
            value={password}
            disabled={clearPassword}
            onChange={event => setPassword(event.target.value)}
            placeholder={hasPassword ? 'Guardada — preencha só para trocar' : 'Senha do usuário SMTP'}
            maxLength={200}
          />
        </label>
        {hasPassword && <label className="checkbox-label wide-field">
          <input type="checkbox" checked={clearPassword} onChange={event => { setClearPassword(event.target.checked); setPassword('') }} /> Remover a senha guardada
        </label>}

        <label>Remetente<input aria-label="Endereço do remetente" type="email" value={draft.fromAddress} onChange={event => update({ fromAddress: event.target.value })} placeholder="pulse@suaempresa.com.br" maxLength={254} required /></label>
        <label>Nome do remetente<input aria-label="Nome do remetente" value={draft.fromName} onChange={event => update({ fromName: event.target.value })} placeholder="Protheus Pulse" maxLength={120} /></label>
        <label className="wide-field">Destinatários, um por linha
          <textarea aria-label="Destinatários" value={draft.recipients} onChange={event => update({ recipients: event.target.value })} placeholder={'ti@suaempresa.com.br\nplantao@suaempresa.com.br'} required />
        </label>
      </div>

      <div className="settings-options">
        <label className="checkbox-label"><input type="checkbox" checked={draft.notifyAlerts} onChange={event => update({ notifyAlerts: event.target.checked })} /> Avisar sobre alertas</label>
        <label className="checkbox-label"><input type="checkbox" checked={draft.notifyLogErrors} onChange={event => update({ notifyLogErrors: event.target.checked })} /> Avisar sobre erros de log dos agentes</label>
        <label className="checkbox-label"><input type="checkbox" checked={draft.allowInvalidCertificate} onChange={event => update({ allowInvalidCertificate: event.target.checked })} /> Aceitar certificado que não valida</label>
      </div>
      {draft.allowInvalidCertificate && <div className="inline-warning"><AlertTriangle size={14} /> Aceitar certificado inválido só faz sentido em relay interno com certificado próprio.</div>}

      <footer className="modal-actions">
        <button type="button" className="secondary-button" disabled={busy || testing || !configured} onClick={() => void test()}>
          {testing ? <RefreshCw className="spin" size={16} /> : <Send size={16} />}{testing ? 'Enviando…' : 'Enviar teste'}
        </button>
        <button className="primary-button" disabled={busy}>{busy ? <RefreshCw className="spin" size={16} /> : <Mail size={16} />}{busy ? 'Salvando…' : 'Salvar dados de envio'}</button>
      </footer>
      {!configured && <p className="field-hint">Salve os dados antes de enviar o teste.</p>}
    </form>}
  </article>
}
