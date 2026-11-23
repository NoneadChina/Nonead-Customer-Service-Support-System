# 基础版 Dockerfile（直连 Docker Hub，.NET 10 + Node 20 多阶段构建）
# 运行时：mcr.microsoft.com/dotnet/aspnet:10.0  是 Debian bookworm-slim，使用 groupadd/useradd
#
# 注意：本文件位于仓库根目录，build context 就是仓库根目录
#
# 用法：
#   cd <仓库根目录>
#   docker build -t noneadcss:latest .

# ========== 阶段 1: 构建前端 ==========
FROM node:20-alpine AS frontend-build
WORKDIR /app/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# ========== 阶段 2: 构建后端 ==========
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /app
COPY NoneadCSS.Api/NoneadCSS.Api.csproj         NoneadCSS.Api/
COPY NoneadCSS.Application/NoneadCSS.Application.csproj NoneadCSS.Application/
COPY NoneadCSS.Domain/NoneadCSS.Domain.csproj   NoneadCSS.Domain/
COPY NoneadCSS.Infrastructure/NoneadCSS.Infrastructure.csproj NoneadCSS.Infrastructure/
RUN dotnet restore NoneadCSS.Api/NoneadCSS.Api.csproj
COPY . .
COPY --from=frontend-build /app/frontend/dist NoneadCSS.Api/wwwroot
# 注意：不带 --no-restore，restore/publish 两层 NuGet 上下文不一致会导致 MSB4018
RUN dotnet publish NoneadCSS.Api/NoneadCSS.Api.csproj -c Release -o /app/publish

# ========== 阶段 3: 运行时（Debian bookworm-slim） ==========
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# aspnet:10.0 基于 Debian，使用 groupadd/useradd（不是 Alpine 的 addgroup/adduser）
RUN groupadd --system appgroup \
 && useradd  --system --gid appgroup --shell /usr/sbin/nologin --home-dir /app appuser
RUN mkdir -p /app/Data /app/wwwroot && chown -R appuser:appgroup /app

# curl:   HEALTHCHECK 探测 /health（aspnet slim 镜像未预装）
# gosu:   entrypoint 以 root 修复 /app/Data 属主后降权执行 dotnet（PID 1 信号处理正确）
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl gosu \
 && rm -rf /var/lib/apt/lists/*

COPY --from=backend-build /app/publish .
# 入口脚本：root 启动 → chown 数据卷属主 → gosu 降权 → exec dotnet（PID 1 信号处理正确）
COPY Docker/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh \
 && chown -R appuser:appgroup /app

# 注意：不在此 USER appuser。容器启动时 entrypoint 先 chown /app/Data，再 gosu 切换到 appuser。
# 原因：bind mount 的 /app/Data 目录继承主机属主（root/uid 1000 等），直接 USER appuser 会导致
# SQLite "attempt to write a readonly database"。

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5000
ENV Attachment__UploadPath=Data/Attachments
ENV ForwardedHeaders__KnownProxies=

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -sf -o /dev/null --data-binary "" http://127.0.0.1:5000/health || exit 1

EXPOSE 5000

VOLUME ["/app/Data"]

ENTRYPOINT ["/app/entrypoint.sh"]
CMD ["dotnet", "NoneadCSS.Api.dll"]
