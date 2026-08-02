#!/usr/bin/env python3
"""Agente de log do AppServer para o Protheus Pulse.

Acompanha o console.log de uma ou mais instalações TOTVS Protheus, reconhece as
linhas de erro, agrupa as repetições e envia o resultado para o Pulse, que grava
na página de Logs e dispara o e-mail configurado na aba Configurações.

Usa apenas a biblioteca padrão do Python: servidor Protheus costuma não ter pip
nem saída para a internet.

Uso:
    python pulse_log_agent.py --config pulse-agent.ini
    python pulse_log_agent.py --config pulse-agent.ini --once --dry-run
    python pulse_log_agent.py --config pulse-agent.ini --test-connection
"""

from __future__ import annotations

import argparse
import configparser
import json
import logging
import logging.handlers
import os
import re
import signal
import ssl
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from datetime import datetime, timezone
from hashlib import sha256
from pathlib import Path
from typing import Iterable, Sequence

VERSION = "1.0.0"
DEFAULT_INTERVAL_SECONDS = 60
DEFAULT_TIMEOUT_SECONDS = 15
DEFAULT_MAX_BYTES_PER_CYCLE = 262_144
DEFAULT_MAX_EVENTS_PER_CYCLE = 200
DEFAULT_ENCODING = "cp1252"
MAXIMUM_MESSAGE_LENGTH = 1_000
MAXIMUM_CONTINUATION_LINES = 5
SEND_ATTEMPTS = 3

# Mesmas regras do Pulse: o segredo não pode sair do servidor nem dentro de um log.
SENSITIVE_ASSIGNMENT = re.compile(
    r"(password|passwd|pwd|secret|token|credential|authorization|privatekey"
    r"|cryptkey|accesskey|apikey|clientsecret)\s*[:=]\s*[\"']?[^,;\s\"']+",
    re.IGNORECASE,
)
BEARER_TOKEN = re.compile(r"Bearer\s+[A-Za-z0-9._~+/=-]+", re.IGNORECASE)

# Padrões vistos no console.log do AppServer. Critical vem antes de Error porque a
# primeira correspondência decide o nível.
CRITICAL_PATTERNS = (
    re.compile(r"\bfatal\b", re.IGNORECASE),
    re.compile(r"\bcritical\b", re.IGNORECASE),
    re.compile(r"access violation", re.IGNORECASE),
    re.compile(r"unhandled exception", re.IGNORECASE),
    re.compile(r"out of memory", re.IGNORECASE),
)
ERROR_PATTERNS = (
    re.compile(r"\berror\b", re.IGNORECASE),
    re.compile(r"\berro\b", re.IGNORECASE),
    re.compile(r"\bexception\b", re.IGNORECASE),
    re.compile(r"thread error", re.IGNORECASE),
    re.compile(r"helpstop", re.IGNORECASE),
    re.compile(r"\bmsgstop\b", re.IGNORECASE),
    re.compile(r"cannot open|can't open|nao foi possivel|não foi possível", re.IGNORECASE),
    re.compile(r"connection (refused|failed|lost)", re.IGNORECASE),
)
WARNING_PATTERNS = (
    re.compile(r"\bwarn(ing)?\b", re.IGNORECASE),
    re.compile(r"\baviso\b", re.IGNORECASE),
)
# Continuação de um bloco de erro do ADVPL (pilha de chamada, detalhes indentados).
CONTINUATION_PATTERNS = (
    re.compile(r"^\s+\S"),
    re.compile(r"^\s*called from", re.IGNORECASE),
    re.compile(r"^\s*at\s+\S", re.IGNORECASE),
    re.compile(r"^\s*\[\s*\d+\s*\]"),
)

logger = logging.getLogger("pulse-log-agent")


class ConfigurationError(Exception):
    """Configuração ausente ou inválida no arquivo INI."""


@dataclass(frozen=True)
class PulseConfig:
    base_url: str
    state_file: Path
    log_file: Path | None = None
    interval_seconds: int = DEFAULT_INTERVAL_SECONDS
    timeout_seconds: int = DEFAULT_TIMEOUT_SECONDS
    verify_tls: bool = True
    ca_bundle: str | None = None
    max_bytes_per_cycle: int = DEFAULT_MAX_BYTES_PER_CYCLE
    max_events_per_cycle: int = DEFAULT_MAX_EVENTS_PER_CYCLE
    send_warnings: bool = False


@dataclass(frozen=True)
class AgentTarget:
    name: str
    agent_key: str
    token: str
    log_path: Path
    encoding: str = DEFAULT_ENCODING


@dataclass
class LogEvent:
    level: str
    message: str
    fingerprint: str
    occurrence_count: int = 1
    observed_at: datetime = field(default_factory=lambda: datetime.now(timezone.utc))

    def to_payload(self) -> dict[str, object]:
        return {
            "observedAt": self.observed_at.isoformat(),
            "level": self.level,
            "message": self.message,
            "occurrenceCount": self.occurrence_count,
        }


@dataclass(frozen=True)
class ReadResult:
    events: list[LogEvent]
    offset: int
    identity: str
    restarted: bool = False


def sanitize(line: str) -> str:
    """Remove controles, mascara segredos e limita o tamanho da mensagem."""
    clean = "".join(" " if character < " " or character == "\x7f" else character for character in line)
    # O Bearer vem primeiro: em "Authorization: Bearer xyz" a regra de atribuição
    # consumiria só a palavra "Bearer" e deixaria o token à mostra.
    clean = BEARER_TOKEN.sub("Bearer [REDACTED]", clean)
    clean = SENSITIVE_ASSIGNMENT.sub(lambda match: f"{match.group(1)}=[REDACTED]", clean)
    clean = " ".join(clean.split())
    return clean[:MAXIMUM_MESSAGE_LENGTH]


def classify(line: str, extra_error_patterns: Sequence[re.Pattern[str]] = ()) -> str | None:
    """Devolve Critical, Error, Warning ou None para linhas que não interessam."""
    for pattern in CRITICAL_PATTERNS:
        if pattern.search(line):
            return "Critical"
    for pattern in (*ERROR_PATTERNS, *extra_error_patterns):
        if pattern.search(line):
            return "Error"
    for pattern in WARNING_PATTERNS:
        if pattern.search(line):
            return "Warning"
    return None


def is_continuation(line: str) -> bool:
    return any(pattern.match(line) for pattern in CONTINUATION_PATTERNS)


def fingerprint(message: str) -> str:
    """Assinatura estável: dígitos viram # para agrupar a mesma falha."""
    normalized = re.sub(r"\d", "#", message.lower())
    return sha256(normalized.encode("utf-8")).hexdigest().upper()


def file_identity(path: Path) -> str:
    """Identidade do arquivo, para separar rotação de simples acréscimo.

    Índice do arquivo mais volume identificam o arquivo tanto no Windows quanto no
    Unix e não mudam quando o AppServer só acrescenta linhas. A data de criação
    entra apenas quando o sistema de arquivos não informa o índice, o que acontece
    em alguns compartilhamentos de rede.
    """
    info = path.stat()
    inode = getattr(info, "st_ino", 0) or 0
    device = getattr(info, "st_dev", 0) or 0
    if inode:
        return f"{device}:{inode}"
    created = getattr(info, "st_birthtime", None)
    if created is None:
        created = info.st_ctime if os.name == "nt" else 0
    return f"{device}:created:{created}"


def extract_events(
    text: str,
    *,
    send_warnings: bool,
    max_events: int,
    extra_error_patterns: Sequence[re.Pattern[str]] = (),
    now: datetime | None = None,
) -> list[LogEvent]:
    """Converte o trecho lido em eventos agrupados por assinatura."""
    observed_at = now or datetime.now(timezone.utc)
    accepted_levels = {"Critical", "Error"} | ({"Warning"} if send_warnings else set())
    grouped: dict[str, LogEvent] = {}
    lines = text.splitlines()
    index = 0
    while index < len(lines):
        level = classify(lines[index], extra_error_patterns)
        if level is None or level not in accepted_levels:
            index += 1
            continue

        # A mensagem do ADVPL costuma continuar nas linhas seguintes; sem elas o
        # e-mail chega sem a pilha de chamada, que é justamente o que ajuda.
        parts = [lines[index]]
        lookahead = index + 1
        while (
            lookahead < len(lines)
            and len(parts) <= MAXIMUM_CONTINUATION_LINES
            and is_continuation(lines[lookahead])
            and classify(lines[lookahead], extra_error_patterns) is None
        ):
            parts.append(lines[lookahead])
            lookahead += 1

        message = sanitize(" | ".join(parts))
        index = lookahead
        if not message:
            continue

        signature = fingerprint(message)
        existing = grouped.get(signature)
        if existing is not None:
            existing.occurrence_count += 1
            continue
        if len(grouped) >= max_events:
            continue
        grouped[signature] = LogEvent(level=level, message=message, fingerprint=signature, observed_at=observed_at)

    return list(grouped.values())


def read_new_lines(
    target: AgentTarget,
    state: dict[str, object],
    config: PulseConfig,
    *,
    from_start: bool,
) -> ReadResult:
    """Lê apenas o que entrou no arquivo desde o último ciclo bem-sucedido."""
    identity = file_identity(target.log_path)
    size = target.log_path.stat().st_size
    known_identity = str(state.get("identity") or "")
    offset = int(state.get("offset") or 0)
    restarted = False

    if not known_identity:
        # Primeira execução: começa do fim para não reenviar o histórico inteiro.
        offset = 0 if from_start else size
    elif known_identity != identity or offset > size:
        # Arquivo rotacionado ou truncado: o conteúdo antigo já não está lá.
        offset = 0
        restarted = True

    if size - offset > config.max_bytes_per_cycle:
        offset = size - config.max_bytes_per_cycle

    if offset >= size:
        return ReadResult(events=[], offset=size, identity=identity, restarted=restarted)

    with target.log_path.open("rb") as handle:
        handle.seek(offset)
        chunk = handle.read(config.max_bytes_per_cycle)

    consumed = len(chunk)
    if not chunk.endswith(b"\n"):
        # Linha pela metade: o AppServer ainda está escrevendo. Ela fica para o
        # próximo ciclo, quando a quebra de linha tiver chegado.
        last_break = chunk.rfind(b"\n")
        if last_break < 0:
            return ReadResult(events=[], offset=offset, identity=identity, restarted=restarted)
        chunk = chunk[: last_break + 1]
        consumed = last_break + 1

    text = chunk.decode(target.encoding, errors="replace")
    events = extract_events(
        text,
        send_warnings=config.send_warnings,
        max_events=config.max_events_per_cycle,
    )
    return ReadResult(events=events, offset=offset + consumed, identity=identity, restarted=restarted)


class PulseClient:
    """Cliente HTTP mínimo para a API de ingestão do Pulse."""

    def __init__(self, config: PulseConfig) -> None:
        self.config = config
        self.context: ssl.SSLContext | None = None
        if config.base_url.lower().startswith("https"):
            if config.verify_tls:
                self.context = ssl.create_default_context(cafile=config.ca_bundle)
            else:
                self.context = ssl._create_unverified_context()  # noqa: SLF001
                logger.warning("Validação de TLS desligada: use apenas em laboratório.")

    def send(self, target: AgentTarget, events: Sequence[LogEvent]) -> bool:
        """Envia o lote. Devolve True apenas quando o Pulse confirma o recebimento."""
        payload = {
            "source": str(target.log_path),
            "events": [event.to_payload() for event in events],
        }
        body = json.dumps(payload).encode("utf-8")
        url = f"{self.config.base_url.rstrip('/')}/api/v1/log-agents/{target.agent_key}/events"
        request = urllib.request.Request(url, data=body, method="POST")  # noqa: S310
        request.add_header("Content-Type", "application/json")
        request.add_header("X-Pulse-Agent-Token", target.token)
        request.add_header("User-Agent", f"protheus-pulse-log-agent/{VERSION}")

        for attempt in range(1, SEND_ATTEMPTS + 1):
            try:
                with urllib.request.urlopen(  # noqa: S310
                    request, timeout=self.config.timeout_seconds, context=self.context
                ) as response:
                    logger.info(
                        "[%s] %d evento(s) aceito(s) pelo Pulse (HTTP %d).",
                        target.name,
                        len(events),
                        response.status,
                    )
                    return True
            except urllib.error.HTTPError as error:
                detail = error.read().decode("utf-8", errors="replace")[:300]
                logger.error("[%s] o Pulse recusou o envio (HTTP %s): %s", target.name, error.code, detail)
                # 4xx não melhora com repetição: token errado continua errado.
                if 400 <= error.code < 500 and error.code != 429:
                    return False
            except (urllib.error.URLError, TimeoutError, ssl.SSLError, OSError) as error:
                logger.error("[%s] falha ao falar com o Pulse: %s", target.name, error)

            if attempt < SEND_ATTEMPTS:
                time.sleep(min(2 ** attempt, 10))

        return False


class StateStore:
    """Cursor de leitura por alvo, gravado em disco de forma atômica."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.data: dict[str, dict[str, object]] = {}
        self.load()

    def load(self) -> None:
        try:
            self.data = json.loads(self.path.read_text(encoding="utf-8"))
        except FileNotFoundError:
            self.data = {}
        except (json.JSONDecodeError, OSError) as error:
            logger.warning("Estado ilegível em %s (%s); recomeçando do fim dos arquivos.", self.path, error)
            self.data = {}

    def get(self, name: str) -> dict[str, object]:
        return self.data.get(name, {})

    def update(self, name: str, *, path: Path, offset: int, identity: str) -> None:
        self.data[name] = {"path": str(path), "offset": offset, "identity": identity}

    def save(self) -> None:
        temporary = self.path.with_suffix(self.path.suffix + ".tmp")
        try:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            temporary.write_text(json.dumps(self.data, indent=2), encoding="utf-8")
            os.replace(temporary, self.path)
        except OSError as error:
            logger.error("Não foi possível gravar o estado em %s: %s", self.path, error)


def load_configuration(path: Path) -> tuple[PulseConfig, list[AgentTarget]]:
    parser = configparser.ConfigParser()
    if not parser.read(path, encoding="utf-8"):
        raise ConfigurationError(f"Arquivo de configuração não encontrado: {path}")
    if not parser.has_section("pulse"):
        raise ConfigurationError("A seção [pulse] é obrigatória.")

    section = parser["pulse"]
    base_url = section.get("base_url", "").strip()
    if not base_url:
        raise ConfigurationError("Informe base_url na seção [pulse].")

    config = PulseConfig(
        base_url=base_url,
        state_file=resolve_path(section.get("state_file", "pulse-agent-state.json"), path),
        log_file=resolve_path(section["log_file"], path) if section.get("log_file", "").strip() else None,
        interval_seconds=max(10, section.getint("interval_seconds", DEFAULT_INTERVAL_SECONDS)),
        timeout_seconds=max(5, section.getint("timeout_seconds", DEFAULT_TIMEOUT_SECONDS)),
        verify_tls=section.getboolean("verify_tls", True),
        ca_bundle=section.get("ca_bundle", "").strip() or None,
        max_bytes_per_cycle=max(4_096, section.getint("max_bytes_per_cycle", DEFAULT_MAX_BYTES_PER_CYCLE)),
        max_events_per_cycle=min(200, max(1, section.getint("max_events_per_cycle", DEFAULT_MAX_EVENTS_PER_CYCLE))),
        send_warnings=section.getboolean("send_warnings", False),
    )

    targets: list[AgentTarget] = []
    for name in parser.sections():
        if not name.startswith("agent:"):
            continue
        agent_section = parser[name]
        label = name.split(":", 1)[1].strip() or "agente"
        missing = [key for key in ("agent_key", "token", "log_path") if not agent_section.get(key, "").strip()]
        if missing:
            raise ConfigurationError(f"[{name}] está sem: {', '.join(missing)}.")
        targets.append(
            AgentTarget(
                name=label,
                agent_key=agent_section["agent_key"].strip(),
                token=agent_section["token"].strip(),
                log_path=Path(agent_section["log_path"].strip()),
                encoding=agent_section.get("encoding", DEFAULT_ENCODING).strip() or DEFAULT_ENCODING,
            )
        )

    if not targets:
        raise ConfigurationError("Cadastre ao menos uma seção [agent:NOME].")
    return config, targets


def resolve_path(value: str, configuration_path: Path) -> Path:
    """Caminho relativo é resolvido a partir da pasta do arquivo de configuração."""
    candidate = Path(value.strip())
    return candidate if candidate.is_absolute() else (configuration_path.parent / candidate).resolve()


def configure_logging(log_file: Path | None, verbose: bool) -> None:
    logger.setLevel(logging.DEBUG if verbose else logging.INFO)
    logger.handlers.clear()
    formatter = logging.Formatter("%(asctime)s %(levelname)-8s %(message)s", "%Y-%m-%d %H:%M:%S")
    console = logging.StreamHandler(sys.stdout)
    console.setFormatter(formatter)
    logger.addHandler(console)
    if log_file is not None:
        try:
            log_file.parent.mkdir(parents=True, exist_ok=True)
            rotating = logging.handlers.RotatingFileHandler(
                log_file, maxBytes=5 * 1024 * 1024, backupCount=3, encoding="utf-8"
            )
            rotating.setFormatter(formatter)
            logger.addHandler(rotating)
        except OSError as error:
            logger.warning("Sem log em arquivo (%s): %s", log_file, error)


def run_cycle(
    config: PulseConfig,
    targets: Iterable[AgentTarget],
    state: StateStore,
    client: PulseClient,
    *,
    dry_run: bool,
    from_start: bool,
) -> int:
    """Executa um ciclo completo e devolve quantos eventos foram enviados."""
    total = 0
    for target in targets:
        try:
            if not target.log_path.is_file():
                logger.warning("[%s] log não encontrado: %s", target.name, target.log_path)
                continue
            result = read_new_lines(target, state.get(target.name), config, from_start=from_start)
        except OSError as error:
            logger.error("[%s] não foi possível ler %s: %s", target.name, target.log_path, error)
            continue

        if result.restarted:
            logger.info("[%s] arquivo rotacionado ou truncado; a leitura recomeçou do início.", target.name)

        if not result.events:
            # Sem erro novo o cursor ainda avança: as linhas já foram examinadas.
            state.update(target.name, path=target.log_path, offset=result.offset, identity=result.identity)
            continue

        if dry_run:
            logger.info("[%s] %d evento(s) seriam enviados:", target.name, len(result.events))
            for event in result.events:
                logger.info("  [%s] %dx %s", event.level, event.occurrence_count, event.message)
            continue

        if not client.send(target, result.events):
            # O cursor não avança: o mesmo trecho é tentado de novo no próximo ciclo.
            logger.warning("[%s] o lote será reenviado no próximo ciclo.", target.name)
            continue

        total += len(result.events)
        state.update(target.name, path=target.log_path, offset=result.offset, identity=result.identity)

    if not dry_run:
        state.save()
    return total


def test_connection(targets: Iterable[AgentTarget], client: PulseClient) -> int:
    """Envia um lote vazio por alvo só para validar URL, chave e token."""
    failures = 0
    for target in targets:
        if client.send(target, []):
            logger.info("[%s] conexão e token válidos.", target.name)
        else:
            logger.error("[%s] falha na validação.", target.name)
            failures += 1
    return failures


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="pulse_log_agent",
        description="Envia os erros do console.log do AppServer para o Protheus Pulse.",
    )
    parser.add_argument("--config", default="pulse-agent.ini", help="Arquivo INI de configuração.")
    parser.add_argument("--once", action="store_true", help="Executa um único ciclo e sai.")
    parser.add_argument("--dry-run", action="store_true", help="Mostra o que seria enviado, sem enviar.")
    parser.add_argument("--from-start", action="store_true", help="Na primeira execução, lê o arquivo desde o início.")
    parser.add_argument("--test-connection", action="store_true", help="Valida URL, chave e token de cada alvo.")
    parser.add_argument("--verbose", action="store_true", help="Registra também as mensagens de depuração.")
    parser.add_argument("--version", action="version", version=f"%(prog)s {VERSION}")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = build_parser().parse_args(argv)
    configuration_path = Path(arguments.config).expanduser()
    try:
        config, targets = load_configuration(configuration_path)
    except ConfigurationError as error:
        configure_logging(None, arguments.verbose)
        logger.error("%s", error)
        return 2

    configure_logging(config.log_file, arguments.verbose)
    logger.info("Agente de log do Protheus Pulse %s · %d alvo(s) · destino %s", VERSION, len(targets), config.base_url)
    client = PulseClient(config)

    if arguments.test_connection:
        return 1 if test_connection(targets, client) else 0

    state = StateStore(config.state_file)
    stopping = False

    def request_stop(*_: object) -> None:
        nonlocal stopping
        stopping = True
        logger.info("Encerrando após o ciclo atual.")

    for name in ("SIGINT", "SIGTERM"):
        received = getattr(signal, name, None)
        if received is not None:
            signal.signal(received, request_stop)

    while not stopping:
        run_cycle(config, targets, state, client, dry_run=arguments.dry_run, from_start=arguments.from_start)
        if arguments.once:
            break
        for _ in range(config.interval_seconds):
            if stopping:
                break
            time.sleep(1)

    return 0


if __name__ == "__main__":
    sys.exit(main())
