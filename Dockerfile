FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY ["DateroWuaylluy/DateroWuaylluy.csproj", "DateroWuaylluy/"]
RUN dotnet restore "DateroWuaylluy/DateroWuaylluy.csproj"

COPY . .
WORKDIR "/src/DateroWuaylluy"
RUN dotnet publish "DateroWuaylluy.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

ENV TZ=America/Lima

RUN apt-get update && apt-get install -y --no-install-recommends \
    tzdata \
    curl \
    ca-certificates \
    && ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd -g 10001 appuser && \
    useradd -u 10001 -g 10001 -m -s /usr/sbin/nologin appuser && \
    mkdir -p /app/logs && \
    chown -R appuser:appuser /app

COPY --from=build --chown=appuser:appuser /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1

USER appuser

ENTRYPOINT ["dotnet", "DateroWuaylluy.dll"]
