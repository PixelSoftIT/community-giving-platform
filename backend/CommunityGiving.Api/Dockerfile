FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CommunityGiving.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as non-root for defense-in-depth
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app .
# Port binding is handled in Program.cs via the PORT env var (defaults to 8080 if unset,
# which is what this EXPOSE documents — Render and similar platforms override it automatically).
EXPOSE 8080
ENTRYPOINT ["dotnet", "CommunityGiving.Api.dll"]
