FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY global.json ./
COPY SylvanBot/SylvanBot.csproj SylvanBot/
COPY SylvanBot.Application/SylvanBot.Application.csproj SylvanBot.Application/

RUN dotnet restore SylvanBot/SylvanBot.csproj

COPY . .
RUN dotnet publish SylvanBot/SylvanBot.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:11.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "SylvanBot.dll"]
