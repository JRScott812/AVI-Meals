# Build client assets first so ASP.NET publish can include static web assets.
FROM node:22-bookworm-slim AS client-build
WORKDIR /src/avi-meals.client
COPY avi-meals.client/package*.json ./
RUN npm ci
COPY avi-meals.client/ ./
RUN npm run build

# Publish the .NET server with the prebuilt client dist output.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
WORKDIR /src
ENV BuildingInsideDocker=true
COPY . .
COPY --from=client-build /src/avi-meals.client/dist ./avi-meals.client/dist
RUN CI=true dotnet publish AVI-Meals.Server/AVI-Meals.Server.csproj -c Release -o /app/publish

# Final runtime image used by Heroku container stack.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=server-build /app/publish .
# Debug: List contents of /app and wwwroot to verify DLL and static assets presence
RUN ls -lh /app && ls -lh /app/wwwroot || echo "No wwwroot directory"
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AVI-Meals.Server.dll"]