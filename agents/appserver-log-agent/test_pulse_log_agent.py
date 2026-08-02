#!/usr/bin/env python3
"""Testes do agente de log. Rode com: python -m unittest discover -s . -v"""

from __future__ import annotations

import re
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

import pulse_log_agent as agent

CONSOLE_SAMPLE = """\
[TOTVS][INFO ][2026-08-02 08:00:01] AppServer iniciado na porta 1234
[TOTVS][INFO ][2026-08-02 08:00:02] Ambiente ENVIRONMENT carregado
[TOTVS][ERROR][2026-08-02 08:05:10] Thread Error: variable does not exist CNAME
    Called from U_MEUFONTE(120)
    Called from MATA010(45)
[TOTVS][INFO ][2026-08-02 08:05:11] Sessao 4021 encerrada
[TOTVS][ERROR][2026-08-02 08:06:10] Thread Error: variable does not exist CNAME
    Called from U_MEUFONTE(120)
    Called from MATA010(45)
[TOTVS][WARN ][2026-08-02 08:07:00] Licenca proxima do limite
"""


def build_config(directory: Path, **overrides: object) -> agent.PulseConfig:
    values: dict[str, object] = {
        "base_url": "http://127.0.0.1:5058",
        "state_file": directory / "state.json",
        "max_bytes_per_cycle": 65_536,
        "max_events_per_cycle": 200,
        "send_warnings": False,
    }
    values.update(overrides)
    return agent.PulseConfig(**values)  # type: ignore[arg-type]


def build_target(path: Path, encoding: str = "cp1252") -> agent.AgentTarget:
    return agent.AgentTarget(
        name="erp-producao",
        agent_key="agt_exemplo",
        token="token-sintetico-para-teste",
        log_path=path,
        encoding=encoding,
    )


class SanitizeTests(unittest.TestCase):
    def test_masks_password_assignment(self) -> None:
        result = agent.sanitize("Conectando com password=SenhaSuperSecreta no DBAccess")
        self.assertNotIn("SenhaSuperSecreta", result)
        self.assertIn("[REDACTED]", result)

    def test_masks_bearer_token(self) -> None:
        result = agent.sanitize("Authorization: Bearer abc.def-123_456")
        self.assertNotIn("abc.def-123_456", result)

    def test_removes_control_characters_and_bounds_length(self) -> None:
        result = agent.sanitize("erro\x00grave\t no  ambiente" + "x" * 2_000)
        self.assertNotIn("\x00", result)
        self.assertLessEqual(len(result), agent.MAXIMUM_MESSAGE_LENGTH)
        self.assertIn("erro grave no ambiente", result)


class ClassifyTests(unittest.TestCase):
    def test_recognises_protheus_levels(self) -> None:
        self.assertEqual(agent.classify("[TOTVS][ERROR] Thread Error: algo"), "Error")
        self.assertEqual(agent.classify("Access Violation em MATA010"), "Critical")
        self.assertEqual(agent.classify("[WARN] licenca proxima do limite"), "Warning")
        self.assertIsNone(agent.classify("[INFO] AppServer iniciado"))

    def test_accepts_extra_pattern(self) -> None:
        extra = (re.compile(r"RPO desatualizado", re.IGNORECASE),)
        self.assertEqual(agent.classify("RPO desatualizado", extra), "Error")
        self.assertIsNone(agent.classify("RPO desatualizado"))


class ExtractEventsTests(unittest.TestCase):
    def test_groups_repeated_errors_and_keeps_stack(self) -> None:
        events = agent.extract_events(CONSOLE_SAMPLE, send_warnings=False, max_events=100)
        self.assertEqual(len(events), 1)
        event = events[0]
        self.assertEqual(event.level, "Error")
        self.assertEqual(event.occurrence_count, 2)
        self.assertIn("Called from U_MEUFONTE(120)", event.message)
        self.assertIn("Called from MATA010(45)", event.message)

    def test_ignores_information_lines(self) -> None:
        events = agent.extract_events(CONSOLE_SAMPLE, send_warnings=False, max_events=100)
        self.assertNotIn("AppServer iniciado", " ".join(item.message for item in events))

    def test_includes_warnings_only_when_asked(self) -> None:
        without = agent.extract_events(CONSOLE_SAMPLE, send_warnings=False, max_events=100)
        with_warnings = agent.extract_events(CONSOLE_SAMPLE, send_warnings=True, max_events=100)
        self.assertEqual(len(without), 1)
        self.assertEqual(len(with_warnings), 2)
        self.assertIn("Warning", [item.level for item in with_warnings])

    def test_respects_max_events(self) -> None:
        text = "\n".join(f"[ERROR] falha distinta numero {index} tipo {chr(65 + index)}" for index in range(20))
        events = agent.extract_events(text, send_warnings=False, max_events=5)
        self.assertEqual(len(events), 5)

    def test_payload_uses_iso_utc(self) -> None:
        moment = datetime(2026, 8, 2, 12, 0, 0, tzinfo=timezone.utc)
        events = agent.extract_events("[ERROR] falha", send_warnings=False, max_events=10, now=moment)
        payload = events[0].to_payload()
        self.assertEqual(payload["observedAt"], "2026-08-02T12:00:00+00:00")
        self.assertEqual(payload["level"], "Error")
        self.assertEqual(payload["occurrenceCount"], 1)


class ReadNewLinesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = Path(tempfile.mkdtemp(prefix="pulse-agent-tests-"))
        self.log = self.directory / "console.log"
        self.config = build_config(self.directory)
        self.target = build_target(self.log)

    def write(self, text: str, mode: str = "a") -> None:
        with self.log.open(mode, encoding="cp1252") as handle:
            handle.write(text)

    def test_first_run_starts_at_end_of_file(self) -> None:
        self.write(CONSOLE_SAMPLE, mode="w")
        result = agent.read_new_lines(self.target, {}, self.config, from_start=False)
        self.assertEqual(result.events, [])
        self.assertEqual(result.offset, self.log.stat().st_size)

    def test_first_run_from_start_reads_everything(self) -> None:
        self.write(CONSOLE_SAMPLE, mode="w")
        result = agent.read_new_lines(self.target, {}, self.config, from_start=True)
        self.assertEqual(len(result.events), 1)
        self.assertEqual(result.events[0].occurrence_count, 2)

    def test_only_new_lines_are_returned(self) -> None:
        self.write(CONSOLE_SAMPLE, mode="w")
        first = agent.read_new_lines(self.target, {}, self.config, from_start=False)
        state = {"offset": first.offset, "identity": first.identity}
        self.write("[TOTVS][ERROR][2026-08-02 09:00:00] Falha nova ao abrir tabela SB1\n")
        second = agent.read_new_lines(self.target, state, self.config, from_start=False)
        self.assertEqual(len(second.events), 1)
        self.assertIn("Falha nova ao abrir tabela SB1", second.events[0].message)

    def test_partial_last_line_waits_for_next_cycle(self) -> None:
        self.write("[TOTVS][ERROR][2026-08-02 09:00:00] Falha completa\n", mode="w")
        first = agent.read_new_lines(self.target, {}, self.config, from_start=True)
        self.assertEqual(len(first.events), 1)
        state = {"offset": first.offset, "identity": first.identity}

        self.write("[TOTVS][ERROR][2026-08-02 09:01:00] Falha pela met")
        second = agent.read_new_lines(self.target, state, self.config, from_start=False)
        self.assertEqual(second.events, [])
        self.assertEqual(second.offset, first.offset)

        self.write("ade agora terminada\n")
        third = agent.read_new_lines(
            self.target,
            {"offset": second.offset, "identity": second.identity},
            self.config,
            from_start=False,
        )
        self.assertEqual(len(third.events), 1)
        self.assertIn("Falha pela metade agora terminada", third.events[0].message)

    def test_truncated_file_restarts_from_zero(self) -> None:
        self.write(CONSOLE_SAMPLE, mode="w")
        first = agent.read_new_lines(self.target, {}, self.config, from_start=False)
        state = {"offset": first.offset, "identity": first.identity}

        self.write("[TOTVS][ERROR][2026-08-02 10:00:00] Log recriado com falha\n", mode="w")
        result = agent.read_new_lines(self.target, state, self.config, from_start=False)
        self.assertTrue(result.restarted)
        self.assertEqual(len(result.events), 1)
        self.assertIn("Log recriado com falha", result.events[0].message)

    def test_huge_backlog_is_capped(self) -> None:
        self.write("[INFO] linha antiga\n" * 20_000, mode="w")
        self.write("[TOTVS][ERROR][2026-08-02 11:00:00] Erro no fim do arquivo\n")
        small = build_config(self.directory, max_bytes_per_cycle=4_096)
        result = agent.read_new_lines(self.target, {}, small, from_start=True)
        self.assertEqual(len(result.events), 1)
        self.assertEqual(result.offset, self.log.stat().st_size)

    def test_accents_survive_cp1252(self) -> None:
        self.write("[TOTVS][ERROR][2026-08-02 11:30:00] Não foi possível gravar a configuração\n", mode="w")
        result = agent.read_new_lines(self.target, {}, self.config, from_start=True)
        self.assertIn("Não foi possível gravar a configuração", result.events[0].message)


class StateStoreTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = Path(tempfile.mkdtemp(prefix="pulse-agent-state-"))
        self.path = self.directory / "nested" / "state.json"

    def test_round_trip(self) -> None:
        store = agent.StateStore(self.path)
        store.update("erp", path=Path("D:/log/console.log"), offset=42, identity="abc:1")
        store.save()

        reloaded = agent.StateStore(self.path)
        self.assertEqual(reloaded.get("erp")["offset"], 42)
        self.assertEqual(reloaded.get("erp")["identity"], "abc:1")

    def test_corrupted_state_is_ignored(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text("{ isto não é json", encoding="utf-8")
        store = agent.StateStore(self.path)
        self.assertEqual(store.data, {})


class ConfigurationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = Path(tempfile.mkdtemp(prefix="pulse-agent-config-"))
        self.path = self.directory / "pulse-agent.ini"

    def write(self, content: str) -> None:
        self.path.write_text(content, encoding="utf-8")

    def test_loads_targets_and_resolves_relative_paths(self) -> None:
        self.write(
            "[pulse]\n"
            "base_url = http://127.0.0.1:5058\n"
            "state_file = estado/agent.json\n"
            "interval_seconds = 30\n"
            "\n"
            "[agent:producao]\n"
            "agent_key = agt_abc\n"
            "token = token-de-exemplo\n"
            "log_path = D:\\TOTVS\\Protheus\\appserver\\console.log\n"
        )
        config, targets = agent.load_configuration(self.path)
        self.assertEqual(config.interval_seconds, 30)
        self.assertEqual(config.state_file, (self.directory / "estado/agent.json").resolve())
        self.assertEqual(len(targets), 1)
        self.assertEqual(targets[0].name, "producao")
        self.assertEqual(targets[0].encoding, agent.DEFAULT_ENCODING)

    def test_rejects_target_without_token(self) -> None:
        self.write(
            "[pulse]\n"
            "base_url = http://127.0.0.1:5058\n"
            "\n"
            "[agent:producao]\n"
            "agent_key = agt_abc\n"
            "log_path = D:\\log\\console.log\n"
        )
        with self.assertRaises(agent.ConfigurationError) as context:
            agent.load_configuration(self.path)
        self.assertIn("token", str(context.exception))

    def test_rejects_missing_base_url(self) -> None:
        self.write("[pulse]\nstate_file = x.json\n")
        with self.assertRaises(agent.ConfigurationError):
            agent.load_configuration(self.path)

    def test_interval_has_a_floor(self) -> None:
        self.write(
            "[pulse]\n"
            "base_url = http://127.0.0.1:5058\n"
            "interval_seconds = 1\n"
            "\n"
            "[agent:producao]\n"
            "agent_key = agt_abc\n"
            "token = token-de-exemplo\n"
            "log_path = D:\\log\\console.log\n"
        )
        config, _ = agent.load_configuration(self.path)
        self.assertEqual(config.interval_seconds, 10)


class RunCycleTests(unittest.TestCase):
    class RecordingClient:
        def __init__(self, succeed: bool = True) -> None:
            self.succeed = succeed
            self.batches: list[list[agent.LogEvent]] = []

        def send(self, target: agent.AgentTarget, events) -> bool:  # noqa: ANN001
            self.batches.append(list(events))
            return self.succeed

    def setUp(self) -> None:
        self.directory = Path(tempfile.mkdtemp(prefix="pulse-agent-cycle-"))
        self.log = self.directory / "console.log"
        self.log.write_text(CONSOLE_SAMPLE, encoding="cp1252")
        self.config = build_config(self.directory)
        self.target = build_target(self.log)
        self.state = agent.StateStore(self.config.state_file)

    def test_successful_cycle_advances_cursor(self) -> None:
        client = self.RecordingClient()
        sent = agent.run_cycle(self.config, [self.target], self.state, client, dry_run=False, from_start=True)
        self.assertEqual(sent, 1)
        self.assertEqual(self.state.get("erp-producao")["offset"], self.log.stat().st_size)

        again = agent.run_cycle(self.config, [self.target], self.state, client, dry_run=False, from_start=True)
        self.assertEqual(again, 0)
        self.assertEqual(len(client.batches), 1)

    def test_failed_delivery_keeps_cursor_for_retry(self) -> None:
        client = self.RecordingClient(succeed=False)
        agent.run_cycle(self.config, [self.target], self.state, client, dry_run=False, from_start=True)
        self.assertEqual(self.state.get("erp-producao"), {})

        working = self.RecordingClient()
        agent.run_cycle(self.config, [self.target], self.state, working, dry_run=False, from_start=True)
        self.assertEqual(len(working.batches), 1)
        self.assertEqual(working.batches[0][0].occurrence_count, 2)

    def test_dry_run_sends_nothing_and_keeps_state(self) -> None:
        client = self.RecordingClient()
        agent.run_cycle(self.config, [self.target], self.state, client, dry_run=True, from_start=True)
        self.assertEqual(client.batches, [])
        self.assertEqual(self.state.get("erp-producao"), {})

    def test_missing_log_is_reported_without_crashing(self) -> None:
        client = self.RecordingClient()
        missing = build_target(self.directory / "nao-existe.log")
        sent = agent.run_cycle(self.config, [missing], self.state, client, dry_run=False, from_start=True)
        self.assertEqual(sent, 0)
        self.assertEqual(client.batches, [])


if __name__ == "__main__":
    unittest.main()
