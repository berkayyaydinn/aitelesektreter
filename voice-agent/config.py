"""Ortam yapılandırması. Tüm sırlar/seçimler .env'den; kodda sabit yok."""
from __future__ import annotations

import os
from dataclasses import dataclass

from dotenv import load_dotenv

load_dotenv()


def _require(key: str) -> str:
    value = os.getenv(key)
    if not value:
        raise RuntimeError(f"Eksik ortam değişkeni: {key}")
    return value


@dataclass(frozen=True)
class Settings:
    backend_base_url: str
    internal_api_key: str
    speech_language: str

    stt_provider: str
    llm_provider: str
    tts_provider: str
    llm_model: str

    # Gecikme ayarı: turn detection modeli + endpointing penceresi + araç timeout'u.
    turn_detection: str
    min_endpointing_delay: float
    max_endpointing_delay: float
    tool_timeout_seconds: float

    @staticmethod
    def load() -> "Settings":
        return Settings(
            backend_base_url=_require("BACKEND_BASE_URL"),
            internal_api_key=_require("INTERNAL_API_KEY"),
            speech_language=os.getenv("SPEECH_LANGUAGE", "tr"),
            stt_provider=os.getenv("STT_PROVIDER", "deepgram"),
            llm_provider=os.getenv("LLM_PROVIDER", "openai"),
            tts_provider=os.getenv("TTS_PROVIDER", "azure"),
            llm_model=os.getenv("LLM_MODEL", "gpt-4o-mini"),
            turn_detection=os.getenv("TURN_DETECTION", "multilingual"),
            min_endpointing_delay=float(os.getenv("MIN_ENDPOINTING_DELAY", "0.4")),
            max_endpointing_delay=float(os.getenv("MAX_ENDPOINTING_DELAY", "5.0")),
            tool_timeout_seconds=float(os.getenv("TOOL_TIMEOUT_SECONDS", "4.0")),
        )


settings = Settings.load()
