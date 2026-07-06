# ai-telesekreter Python LiveKit voice worker.
# Build context: voice-agent/  (compose: context ../../voice-agent)

FROM python:3.12-slim

WORKDIR /app

# ca-certificates: TLS (LiveKit wss + provider API'leri). ffmpeg: ses kod/çöz (livekit-agents).
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Önce requirements → katman cache.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Turn detector + VAD model dosyalarını image'a göm (runtime'da indirme gecikmesi olmasın).
# config.py import'ta zorunlu env ister — build'de sahte değer yeterli (ağ çağrısı yapılmaz).
RUN BACKEND_BASE_URL=http://build INTERNAL_API_KEY=build python agent.py download-files

# LiveKit Agents production worker: LIVEKIT_URL'e bağlanır, gelen çağrı job'larını işler.
CMD ["python", "agent.py", "start"]
