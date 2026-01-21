FROM mcr.microsoft.com/dotnet/sdk:10.0.101
ARG servicename

# Install FFmpeg for audio format conversion (WebM/Opus, M4A -> WAV)
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY out/$servicename .