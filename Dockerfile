FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY global.json ./
COPY RatBot/RatBot.csproj RatBot/
COPY RatBot.Application/RatBot.Application.csproj RatBot.Application/

RUN dotnet restore RatBot/RatBot.csproj

COPY . .
RUN dotnet publish RatBot/RatBot.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "RatBot.dll"]
