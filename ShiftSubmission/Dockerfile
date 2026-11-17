FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app

# Koyebの標準ポート8000を明示
EXPOSE 8000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# ★修正点1：サブフォルダ指定（ShiftSubmission/）を削除し、カレント直下からコピー
COPY ["ShiftSubmission.csproj", "./"]
RUN dotnet restore "./ShiftSubmission.csproj"
COPY . .

# ★修正点2：WORKDIR "/src/ShiftSubmission" を削除し、そのまま /src でビルド
RUN dotnet build "./ShiftSubmission.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ShiftSubmission.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Koyeb無料枠(512MB)でのメモリ不足強制終了を防ぐ設定
ENV DOTNET_GCHeapHardLimit=0x14000000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "ShiftSubmission.dll"]