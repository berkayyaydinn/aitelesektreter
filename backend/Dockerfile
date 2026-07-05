# ai-telesekreter .NET 8 Minimal API — multi-stage (SDK ile derle, ince aspnet runtime ile çalış).
# Build context: backend/  (compose: context ../../backend)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Önce csproj kopyala + restore → katman cache (kod değişince restore tekrar koşmaz).
COPY src/VoiceReception.Api/VoiceReception.Api.csproj src/VoiceReception.Api/
RUN dotnet restore src/VoiceReception.Api/VoiceReception.Api.csproj

COPY src/ src/
RUN dotnet publish src/VoiceReception.Api/VoiceReception.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app ./

# Container içi port (Caddy buraya proxy'ler). Gerçek config env'den gelir (DB_PROVIDER, DATABASE_URL, ...).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "VoiceReception.Api.dll"]
