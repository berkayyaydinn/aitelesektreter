"""Saf prompt yardımcıları — livekit bağımlılığı YOK (kolay birim test).

agent.py bu modülü kullanır. Tenant config dict'ini Türkçe sistem promptuna çevirir.
"""
from __future__ import annotations

RECORDING_NOTICE = (
    "Merhaba, hizmet kalitesi ve kayıtların doğruluğu için bu görüşme kayıt altına "
    "alınmaktadır. Kişisel verilerinizin işlenmesine dair aydınlatma metnine işletmemizden "
    "talep ederek veya tarafınıza iletilen bağlantı üzerinden ulaşabilirsiniz. "
    "Görüşmeye devam etmeniz hâlinde kaydı kabul etmiş sayılırsınız. "
    "Size nasıl yardımcı olabilirim?"
)

# Arayan işletme sahibi olduğunda sistem promptuna eklenir (fatura kesme modu).
OWNER_MODE_NOTE = (
    " NOT: Bu arama işletme sahibinden geliyor. İstenirse create_invoice ile fatura kesebilirsin; "
    "müşteri adı ve tutarı net al, tutarı onaylat."
)


def build_system_prompt(tenant: dict) -> str:
    """Tenant config'inden Türkçe sistem promptu kurar."""
    services = ", ".join(s.get("name", "") for s in tenant.get("services", [])) or "—"
    return (
        f"Sen {tenant.get('businessName', 'bir işletme')} için Türkçe konuşan bir telesekretersin. "
        "Kibar, kısa ve net konuş. Amacın randevu veya sipariş almak. "
        # Maliyet/latency: yanıtı kısa tut → daha az TTS karakteri ve LLM çıktı token'ı,
        # daha hızlı seslendirme. Konuşma hızı ve doğallığı bozulmaz.
        "Her yanıtın en fazla 1-2 kısa cümle olsun; tekrar etme, gereksiz nezaket cümlesi kurma, "
        "soruyu doğrudan sor. Tek seferde tek bilgi iste. "
        f"Hizmetler: {services}. "
        f"Çalışma saatleri: {tenant.get('businessHoursText', 'belirtilmemiş')}. "
        "Randevu verirken önce check_availability ile uygunluğu doğrula, sonra create_appointment çağır. "
        "Müşterinin adını ve telefonunu mutlaka al. Bilmediğin bir şeyi uydurma. "
        + (tenant.get("extraPrompt") or "")
    )
