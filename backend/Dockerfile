# =====================================
# BUILD STAGE
# =====================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + project files first (better caching)
COPY *.sln ./
COPY *.csproj ./

RUN dotnet restore

# Copy everything else
COPY . .

# Publish optimized build
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5247

EXPOSE 5247

ENTRYPOINT ["dotnet", "invoice-v1.dll"]