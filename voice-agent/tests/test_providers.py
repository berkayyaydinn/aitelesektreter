"""Provider fabrikası — bilinmeyen sağlayıcı ValueError vermeli (livekit kurmadan test edilir).
Geçerli sağlayıcı dalları livekit plugin import eder; burada hata yolu + self-host (whisper/piper)
dallarının env'den doğru parametrelerle kurulduğu doğrulanır (openai plugin mock'lanır)."""
import sys
import types

import pytest

from providers.factory import build_llm, build_stt, build_tts

_OPENAI_COMPAT_LLM = ["openai", "local", "openai_compatible"]


def test_unknown_stt_raises():
    with pytest.raises(ValueError):
        build_stt("bogus", "tr")


def test_unknown_llm_raises():
    with pytest.raises(ValueError):
        build_llm("bogus", "model")


def test_unknown_tts_raises():
    with pytest.raises(ValueError):
        build_tts("bogus", "tr")


@pytest.fixture
def fake_openai_plugin(monkeypatch):
    """`from livekit.plugins import openai` import'unu, kwargs'ı yakalayan sahte modülle değiştirir."""
    captured = {}

    def _factory(name):
        def _ctor(*args, **kwargs):
            captured[name] = kwargs
            return object()
        return _ctor

    fake_openai = types.SimpleNamespace(
        STT=_factory("STT"), TTS=_factory("TTS"), LLM=_factory("LLM"))
    fake_plugins = types.ModuleType("livekit.plugins")
    fake_plugins.openai = fake_openai
    monkeypatch.setitem(sys.modules, "livekit", types.ModuleType("livekit"))
    monkeypatch.setitem(sys.modules, "livekit.plugins", fake_plugins)
    return captured


def test_whisper_stt_uses_local_base_url(monkeypatch, fake_openai_plugin):
    monkeypatch.setenv("STT_BASE_URL", "http://whisper-stt:8000/v1")
    monkeypatch.setenv("WHISPER_MODEL", "small")
    build_stt("whisper", "tr")
    kwargs = fake_openai_plugin["STT"]
    assert kwargs["base_url"] == "http://whisper-stt:8000/v1"
    assert kwargs["model"] == "small"
    assert kwargs["language"] == "tr"


def test_piper_tts_uses_local_voice_and_base_url(monkeypatch, fake_openai_plugin):
    monkeypatch.setenv("TTS_BASE_URL", "http://piper-tts:8000/v1")
    monkeypatch.setenv("PIPER_VOICE", "tr_TR-dfki-medium")
    build_tts("piper", "tr")
    kwargs = fake_openai_plugin["TTS"]
    assert kwargs["base_url"] == "http://piper-tts:8000/v1"
    assert kwargs["voice"] == "tr_TR-dfki-medium"


@pytest.mark.parametrize("provider", ["local", "openai_compatible"])
def test_local_llm_uses_base_url_override(monkeypatch, fake_openai_plugin, provider):
    monkeypatch.setenv("LLM_BASE_URL", "http://llm:8000/v1")
    monkeypatch.setenv("LLM_API_KEY", "sk-test")
    build_llm(provider, "Qwen2.5-14B-Instruct")
    kwargs = fake_openai_plugin["LLM"]
    assert kwargs["base_url"] == "http://llm:8000/v1"
    assert kwargs["api_key"] == "sk-test"
    assert kwargs["model"] == "Qwen2.5-14B-Instruct"


def test_openai_llm_without_base_url_stays_cloud(monkeypatch, fake_openai_plugin):
    monkeypatch.delenv("LLM_BASE_URL", raising=False)
    build_llm("openai", "gpt-4o-mini")
    kwargs = fake_openai_plugin["LLM"]
    assert "base_url" not in kwargs
    assert kwargs["model"] == "gpt-4o-mini"
