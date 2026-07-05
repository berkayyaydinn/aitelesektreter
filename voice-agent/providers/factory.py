"""Provider fabrikası — .env seçimine göre LiveKit plugin örneği döndürür.

Yeni sağlayıcı eklemek: ilgili sözlüğe bir dal ekle. agent.py değişmez.
Sadece seçilen sağlayıcının paketi import edilir (lazy), gereksiz bağımlılık yüklenmez.
"""
from __future__ import annotations

import os


def build_stt(provider: str, language: str):
    """Konuşma → metin. Türkçe + telefon (8kHz) için ayarlanır."""
    if provider == "deepgram":
        from livekit.plugins import deepgram
        return deepgram.STT(model="nova-3", language=language)
    if provider == "azure":
        from livekit.plugins import azure
        return azure.STT(language=language)
    if provider == "openai":
        from livekit.plugins import openai
        return openai.STT(language=language)
    if provider == "whisper":
        # Self-host: OpenAI-uyumlu yerel Whisper sunucusu (faster-whisper / speaches).
        # Mevcut openai plugin'i base_url override ile kullanılır → yeni bağımlılık yok.
        from livekit.plugins import openai
        return openai.STT(
            language=language,
            model=os.getenv("WHISPER_MODEL", "small"),
            base_url=os.getenv("STT_BASE_URL", "http://whisper-stt:8000/v1"),
            api_key=os.getenv("STT_API_KEY", "sk-local"),
        )
    raise ValueError(f"Bilinmeyen STT_PROVIDER: {provider}")


def build_llm(provider: str, model: str):
    """Diyalog beyni — tool-calling destekli model.

    provider=openai            → gerçek OpenAI (LLM_BASE_URL boşsa).
    provider=local|openai_compatible → self-host OpenAI-uyumlu sunucu (Ollama / vLLM / LM Studio).
      LLM_BASE_URL verilirse openai plugin oraya yönlenir → yeni bağımlılık yok. Whisper/Piper ile
      aynı desen. Yerel model TOOL-CALLING desteklemeli (tools.py yoğun kullanır). Bkz. docs/llm-setup.md
    """
    if provider in ("openai", "local", "openai_compatible"):
        from livekit.plugins import openai
        base_url = os.getenv("LLM_BASE_URL")
        if base_url:
            return openai.LLM(
                model=model,
                base_url=base_url,
                api_key=os.getenv("LLM_API_KEY", "sk-local"),
            )
        return openai.LLM(model=model)  # base_url yok → gerçek OpenAI (geriye uyumlu)
    raise ValueError(f"Bilinmeyen LLM_PROVIDER: {provider}")


def build_tts(provider: str, language: str):
    """Metin → konuşma. Doğal Türkçe ses."""
    if provider == "azure":
        from livekit.plugins import azure
        return azure.TTS(language=language, voice=os.getenv("AZURE_TTS_VOICE", "tr-TR-EmelNeural"))
    if provider == "elevenlabs":
        from livekit.plugins import elevenlabs
        return elevenlabs.TTS(voice_id=os.getenv("ELEVENLABS_VOICE_ID", ""))
    if provider == "openai":
        from livekit.plugins import openai
        return openai.TTS()
    if provider == "piper":
        # Self-host: OpenAI-uyumlu yerel Piper sunucusu (openedai-speech).
        # Türkçe ses; openai plugin base_url override ile kullanılır → yeni bağımlılık yok.
        from livekit.plugins import openai
        return openai.TTS(
            model=os.getenv("PIPER_MODEL", "piper"),
            voice=os.getenv("PIPER_VOICE", "tr_TR-dfki-medium"),
            base_url=os.getenv("TTS_BASE_URL", "http://piper-tts:8000/v1"),
            api_key=os.getenv("TTS_API_KEY", "sk-local"),
        )
    raise ValueError(f"Bilinmeyen TTS_PROVIDER: {provider}")
