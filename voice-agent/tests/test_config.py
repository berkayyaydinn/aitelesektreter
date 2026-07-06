import pytest

from config import Settings


def test_load_reads_env_and_defaults(monkeypatch):
    monkeypatch.setenv("BACKEND_BASE_URL", "http://x")
    monkeypatch.setenv("INTERNAL_API_KEY", "k")
    monkeypatch.delenv("STT_PROVIDER", raising=False)
    monkeypatch.delenv("SPEECH_LANGUAGE", raising=False)

    s = Settings.load()
    assert s.backend_base_url == "http://x"
    assert s.internal_api_key == "k"
    assert s.stt_provider == "deepgram"   # varsayılan
    assert s.speech_language == "tr"      # varsayılan


def test_load_raises_when_required_missing(monkeypatch):
    monkeypatch.delenv("BACKEND_BASE_URL", raising=False)
    monkeypatch.setenv("INTERNAL_API_KEY", "k")
    with pytest.raises(RuntimeError):
        Settings.load()


def test_latency_defaults(monkeypatch):
    monkeypatch.setenv("BACKEND_BASE_URL", "http://x")
    monkeypatch.setenv("INTERNAL_API_KEY", "k")
    for key in ("TURN_DETECTION", "MIN_ENDPOINTING_DELAY",
                "MAX_ENDPOINTING_DELAY", "TOOL_TIMEOUT_SECONDS"):
        monkeypatch.delenv(key, raising=False)

    s = Settings.load()
    assert s.turn_detection == "multilingual"
    assert s.min_endpointing_delay == 0.4
    assert s.max_endpointing_delay == 5.0
    assert s.tool_timeout_seconds == 4.0


def test_latency_overrides(monkeypatch):
    monkeypatch.setenv("BACKEND_BASE_URL", "http://x")
    monkeypatch.setenv("INTERNAL_API_KEY", "k")
    monkeypatch.setenv("TURN_DETECTION", "vad")
    monkeypatch.setenv("MIN_ENDPOINTING_DELAY", "0.6")
    monkeypatch.setenv("MAX_ENDPOINTING_DELAY", "8")
    monkeypatch.setenv("TOOL_TIMEOUT_SECONDS", "2.5")

    s = Settings.load()
    assert s.turn_detection == "vad"
    assert s.min_endpointing_delay == 0.6
    assert s.max_endpointing_delay == 8.0
    assert s.tool_timeout_seconds == 2.5
