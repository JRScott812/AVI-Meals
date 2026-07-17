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
# Ensure wwwroot is included in publish output
RUN cp -r ./AVI-Meals.Server/wwwroot ./AVI-Meals.Server/wwwroot.bak || true
RUN CI=true dotnet publish AVI-Meals.Server/AVI-Meals.Server.csproj -c Release -o /app/publish
RUN cp -r ./AVI-Meals.Server/wwwroot.bak /app/publish/wwwroot || true

# Final runtime image used by Heroku container stack.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=server-build /app/publish .
COPY AVI-Meals.Server/docker-entrypoint.sh /app/docker-entrypoint.sh
RUN sed -i 's/\r$//' /app/docker-entrypoint.sh \
	&& chmod +x /app/docker-entrypoint.sh \
	&& ls -lh /app \
	&& (ls -lh /app/wwwroot || echo "No wwwroot directory")
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["/app/docker-entrypoint.sh"]
