FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AiAssistant.Agentic.API/AiAssistant.Agentic.API.csproj", "AiAssistant.Agentic.API/"]
RUN dotnet restore "./AiAssistant.Agentic.API/AiAssistant.Agentic.API.csproj"
COPY . .
WORKDIR "/src/AiAssistant.Agentic.API"
RUN dotnet build "./AiAssistant.Agentic.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AiAssistant.Agentic.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AiAssistant.Agentic.API.dll"]
