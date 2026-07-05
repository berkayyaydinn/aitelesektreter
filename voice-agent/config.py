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
        )


settings = Settings.load()
