# Egebis Lead Finder — uretim imaji (cok asamali)
# Derleme:  docker build -t egebis-lead-finder .
# Calistirma icin docker-compose.yml kullanin (PostgreSQL ile birlikte).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Once yalnizca proje dosyasi: kod degisse de paket geri yukleme katmani onbellekte kalir.
COPY EgebisLeadFinder/EgebisLeadFinder.csproj EgebisLeadFinder/
RUN dotnet restore EgebisLeadFinder/EgebisLeadFinder.csproj

COPY EgebisLeadFinder/ EgebisLeadFinder/
RUN dotnet publish EgebisLeadFinder/EgebisLeadFinder.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8091 \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=Europe/Istanbul

COPY --from=build /app/publish .

# Npgsql baglanirken GSSAPI kutuphanesini arar; yoksa her acilista hata satiri yazar.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Log ve oturum anahtari klasorleri root olmayan uygulama kullanicisina ait olmali.
RUN mkdir -p /app/logs /app/keys && chown -R $APP_UID /app/logs /app/keys
USER $APP_UID

EXPOSE 8091
ENTRYPOINT ["dotnet", "EgebisLeadFinder.dll"]
