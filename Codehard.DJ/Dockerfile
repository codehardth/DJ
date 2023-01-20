FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Codehard.DJ/Codehard.DJ.csproj", "Codehard.DJ/"]
RUN dotnet restore "Codehard.DJ/Codehard.DJ.csproj"
COPY . .
WORKDIR "/src/Codehard.DJ"
RUN dotnet build "Codehard.DJ.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Codehard.DJ.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Codehard.DJ.dll"]
