FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY apps/api/SkillsHub.Api.csproj apps/api/
RUN dotnet restore apps/api/SkillsHub.Api.csproj
COPY apps/api apps/api
RUN dotnet publish apps/api/SkillsHub.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
COPY apps/api/migrations ./migrations
EXPOSE 8080
ENTRYPOINT ["dotnet", "SkillsHub.Api.dll"]
