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
