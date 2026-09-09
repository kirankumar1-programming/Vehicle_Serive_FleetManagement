# Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build image with SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for caching restore layers
COPY ["src/VehicleService.Domain/VehicleService.Domain.csproj", "src/VehicleService.Domain/"]
COPY ["src/VehicleService.Application/VehicleService.Application.csproj", "src/VehicleService.Application/"]
COPY ["src/VehicleService.Persistence/VehicleService.Persistence.csproj", "src/VehicleService.Persistence/"]
COPY ["src/VehicleService.Infrastructure/VehicleService.Infrastructure.csproj", "src/VehicleService.Infrastructure/"]
COPY ["src/VehicleService.API/VehicleService.API.csproj", "src/VehicleService.API/"]
COPY ["src/VehicleService.Web/VehicleService.Web.csproj", "src/VehicleService.Web/"]
COPY ["tests/VehicleService.Tests/VehicleService.Tests.csproj", "tests/VehicleService.Tests/"]

RUN dotnet restore "src/VehicleService.Web/VehicleService.Web.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/src/VehicleService.Web"
RUN dotnet build "VehicleService.Web.csproj" -c Release -o /app/build

# Publish application
FROM build AS publish
RUN dotnet publish "VehicleService.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final production stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "VehicleService.Web.dll"]
