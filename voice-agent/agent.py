"""LiveKit Agents giriş noktası — gelen çağrıyı karşılayan Türkçe sesli telesekreter.

Akış:
  1. SIP çağrısı LiveKit odasına düşer; çağrılan DID metadata'da gelir.
  2. DID'den backend tenant config çekilir (prompt, hizmetler, çalışma saatleri).
  3. KVKK kayıt onayı anonsu çalınır + backend'e işlenir.
  4. STT → LLM(tool-calling) → TTS hattı, tenant'a özel prompt + araçlarla konuşur.

Provider'lar (STT/LLM/TTS) swappable — agent bu dosyada hangi sağlayıcı olduğunu bilmez.
"""
from __future__ import annotations

import logging
import os
from datetime import datetime, timezone

from livekit import agents
from livekit.agents import Agent, AgentSession, JobContext, RoomInputOptions
from livekit.plugins import silero

from backend_client import BackendClient
from config import settings
from did import extract_caller, extract_did
from prompts import OWNER_MODE_NOTE, RECORDING_NOTICE, build_system_prompt
from providers import build_llm, build_stt, build_tts
from recording import recording_filepath, recording_url
from static_audio import say_cached
from tools import build_invoice_tools, build_tools
from transcript import count_tool_calls, extract_turns

logger = logging.getLogger("telesekreter")


def _recording_enabled() -> bool:
    return os.getenv("RECORDING_ENABLED", "false").strip().lower() in ("1", "true", "yes")


async def _start_recording(ctx: JobContext, state: dict) -> None:
    """Audio-only RoomComposite egress başlat (self-host MinIO'ya OGG). Best-effort.

    RECORDING_ENABLED=false ise hiçbir şey yapmaz. Hata olursa çağrı etkilenmez — kayıt
    opsiyoneldir, asla çağrıyı düşürmez.
    """
    if not _recording_enabled():
        return
    try:
        from livekit import api

        bucket = os.environ["RECORDING_S3_BUCKET"]
        key = recording_filepath(ctx.room.name, datetime.now(timezone.utc).isoformat())
        req = api.RoomCompositeEgressRequest(
            room_name=ctx.room.name,
            audio_only=True,  # CPU-only VPS: sadece ses, video transcode yok
            file_outputs=[
                api.EncodedFileOutput(
                    file_type=api.EncodedFileType.OGG,
                    filepath=key,
                    s3=api.S3Upload(
                        access_key=os.environ["RECORDING_S3_ACCESS_KEY"],
                        secret=os.environ["RECORDING_S3_SECRET"],
                        bucket=bucket,
                        endpoint=os.getenv("RECORDING_S3_ENDPOINT", "http://minio:9000"),
                        region=os.getenv("RECORDING_S3_REGION", "us-east-1"),
                        force_path_style=True,  # MinIO path-style erişim
                    ),
                )
            ],
        )
        info = await ctx.api.egress.start_room_composite_egress(req)
        state["egress_id"] = getattr(info, "egress_id", None)
        state["url"] = recording_url(bucket, key)
        logger.info("Kayıt başladı: egress=%s url=%s", state["egress_id"], state["url"])
    except Exception:  # noqa: BLE001 — kayıt opsiyonel, çağrıyı bloklamaz
        logger.warning("Kayıt başlatılamadı (best-effort)", exc_info=True)


async def _stop_recording(ctx: JobContext, state: dict) -> None:
    """Egress'i açıkça durdur (oda kapanınca otomatik biter; bu yedek). Best-effort."""
    egress_id = state.get("egress_id")
    if not egress_id:
        return
    try:
        from livekit import api

        await ctx.api.egress.stop_egress(api.StopEgressRequest(egress_id=egress_id))
    except Exception:  # noqa: BLE001
        logger.warning("Kayıt durdurulamadı (best-effort)", exc_info=True)


async def entrypoint(ctx: JobContext) -> None:
    await ctx.connect()

    backend = BackendClient()
    # Çağrı durumu — hata yolunda da call_ended'in doğru endReason ile gitmesini sağlar.
    call_state = {"reason": "normal", "reported": False}
    shutdown = None  # call_started sonrası atanır; except bunu çağırabilir (idempotent)
    try:
        attributes = [p.attributes for p in ctx.room.remote_participants.values()]
        did = extract_did(attributes, ctx.job.metadata)
        caller = extract_caller(attributes)
        tenant = await backend.get_tenant_by_did(did)

        # Sahip modu: arayan, kayıtlı işletme sahibi numarası ise fatura kesme açılır.
        is_owner = bool(caller) and caller == tenant.get("ownerPhone")
        logger.info("Çağrı: DID=%s tenant=%s sahip=%s", did, tenant.get("tenantId"), is_owner)

        started = await backend.report_call_event(
            {
                "tenantId": tenant["tenantId"],
                "did": did,
                "event": "call_started",
                "consent": "call_recording_notified",
            }
        )
        # Doğru kayda yapışsın diye callLogId'yi yakala (yoksa backend latest-open lookup'a düşer).
        call_log_id = (started or {}).get("callLogId")

        # Kayıt + session: on_shutdown bunları çağrı anında okur (closure), önce None.
        recording: dict = {"url": None, "egress_id": None}
        session = None

        async def on_shutdown() -> None:
            # Çağrı bitişi: transkript + analitik topla (best-effort), bildir, istemciyi kapat.
            # Tek sefer raporlar (idempotent); hata yolundan da çağrılabilir.
            if call_state["reported"]:
                return
            call_state["reported"] = True
            payload: dict = {"tenantId": tenant["tenantId"], "did": did, "event": "call_ended",
                             "endReason": call_state["reason"]}
            if call_log_id:
                payload["callLogId"] = call_log_id
            try:
                items = getattr(getattr(session, "history", None), "items", None)
                payload["transcript"] = extract_turns(items)
                payload["toolCallCount"] = count_tool_calls(items)
            except Exception:  # noqa: BLE001 — analitik opsiyonel, çağrıyı bloklamaz
                logger.warning("Transkript çıkarımı başarısız", exc_info=True)
            await _stop_recording(ctx, recording)
            if recording["url"]:
                payload["recordingUrl"] = recording["url"]
            await backend.report_call_event(payload)
            await backend.aclose()

        # call_started sonrası hemen register: erken hata yolunda bile call_ended garanti.
        ctx.add_shutdown_callback(on_shutdown)
        shutdown = on_shutdown

        # Çağrı kaydı (opsiyonel, RECORDING_ENABLED ile) — best-effort, çağrıyı bloklamaz.
        await _start_recording(ctx, recording)

        tenant_tools = build_tools(backend, tenant["tenantId"])
        instructions = build_system_prompt(tenant)
        if is_owner:
            tenant_tools = tenant_tools + build_invoice_tools(backend, tenant["tenantId"], caller)
            instructions = instructions + OWNER_MODE_NOTE

        agent = Agent(instructions=instructions, tools=tenant_tools)

        tts = build_tts(settings.tts_provider, settings.speech_language)
        session = AgentSession(
            vad=silero.VAD.load(),
            stt=build_stt(settings.stt_provider, settings.speech_language),
            llm=build_llm(settings.llm_provider, settings.llm_model),
            tts=tts,
        )

        await session.start(
            agent=agent,
            room=ctx.room,
            room_input_options=RoomInputOptions(),
        )

        # KVKK kayıt onayı anonsu — diyalog başlamadan önce.
        # Sabit metin: worker ömründe bir kez sentezlenir, sonraki çağrılarda cache'ten oynatılır.
        # Maliyet: tekrarlı TTS karakteri ~sıfır. Hata olursa düz say'e düşer (anons asla atlanmaz).
        await say_cached(session, tts, RECORDING_NOTICE, allow_interruptions=False)

    except Exception:
        # Hata: call_started gönderildiyse call_ended'i endReason="error" ile garanti et.
        call_state["reason"] = "error"
        if shutdown is not None:
            try:
                await shutdown()  # idempotent — framework de çalıştırsa çift gitmez
            except Exception:  # noqa: BLE001
                logger.warning("Hata yolunda call_ended bildirilemedi", exc_info=True)
        await backend.aclose()
        raise


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    agents.cli.run_app(agents.WorkerOptions(entrypoint_fnc=entrypoint))
