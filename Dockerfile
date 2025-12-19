FROM mcr.microsoft.com/dotnet/sdk:10.0.101
ARG servicename
WORKDIR /app
COPY out/$servicename