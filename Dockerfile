# Egebis Lead Finder — uretim imaji (cok asamali)
# Derleme:  docker build -t egebis-lead-finder .
# Calistirma icin docker-compose.yml kullanin (PostgreSQL ile birlikte).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Once yalnizca proje dosyasi: kod degisse de paket geri yukleme katmani onbellekte kalir.
COPY EgebisLeadFinder/EgebisLeadFinder.csproj EgebisLeadFinder/
RUN dotnet restore EgebisLeadFinder/EgebisLeadFinder.csproj -r linux-x64 /p:PublishReadyToRun=true

COPY EgebisLeadFinder/ EgebisLeadFinder/
# ReadyToRun: kod imajda onceden derlenir. Yogun (CPU'su dolu) sunucuda ilk isteklerin
# JIT derlemesini beklemesi (ilk sayfa 10-20 sn) ortadan kalkar.
RUN dotnet publish EgebisLeadFinder/EgebisLeadFinder.csproj -c Release -o /app/publish \
    -r linux-x64 --self-contained false --no-restore /p:UseAppHost=false /p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# DOTNET_gcServer=0: kucuk uygulama icin hafif cop toplayici; CPU'su dolu sunucuda cekirdek
# basina ayri GC is parcacigi acip diger islerle yarismaz, bellegi de az kullanir.
ENV ASPNETCORE_HTTP_PORTS=8091 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
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
