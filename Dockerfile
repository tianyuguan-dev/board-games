# Stage 1: Build frontend
FROM node:20-alpine AS frontend
WORKDIR /app
COPY BoardGames.Web/package*.json ./
RUN npm install
COPY BoardGames.Web/ ./
RUN npm run build

# Stage 2: Build backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend
WORKDIR /src
COPY BoardGames/BoardGames.csproj BoardGames/
RUN dotnet restore BoardGames/BoardGames.csproj
COPY BoardGames/ BoardGames/
# Copy frontend build to wwwroot
COPY --from=frontend /app/dist BoardGames/wwwroot/
RUN dotnet publish BoardGames/BoardGames.csproj -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=backend /app/publish .
ENV ASPNETCORE_URLS=http://+:5087
EXPOSE 5087
ENTRYPOINT ["dotnet", "BoardGames.dll"]
