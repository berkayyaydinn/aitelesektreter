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

# LiveKit Agents production worker: LIVEKIT_URL'e bağlanır, gelen çağrı job'larını işler.
CMD ["python", "agent.py", "start"]
