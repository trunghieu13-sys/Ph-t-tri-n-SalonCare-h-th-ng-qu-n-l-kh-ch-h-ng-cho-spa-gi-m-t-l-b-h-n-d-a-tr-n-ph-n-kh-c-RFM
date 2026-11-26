# =============================================================================
# SALONCARE CRM (ASP.NET Core MVC - .NET 10.0)
# Multi-Stage Dockerfile (Build & Runtime trên cổng 8080)
# =============================================================================

# Stage 1: Build & Publish ứng dụng
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Sao chép file csproj và khôi phục các gói NuGet
COPY ["CRM V2.csproj", "./"]
RUN dotnet restore "CRM V2.csproj"

# Sao chép toàn bộ mã nguồn và publish bản Release
COPY . .
RUN dotnet publish "CRM V2.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime Image gọn nhẹ
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Cấu hình ASP.NET Core lắng nghe trên cổng 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CRM V2.dll"]
