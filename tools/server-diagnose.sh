#!/usr/bin/env bash
# Lead Finder sunucu yavaslik teshisi (Ubuntu + Docker).
# Kullanim (depo klasorunde):  bash tools/server-diagnose.sh > teshis.txt 2>&1
# Ciktida sifre/anahtar yoktur; teshis.txt dosyasini paylasabilirsiniz.
set -u
APP=egebis-app
DB=egebis-db
PORT=${APP_PORT:-8091}
line() { printf '\n==== %s ====\n' "$1"; }

line "Sistem"
uname -r; lsb_release -ds 2>/dev/null; nproc; uptime
line "Bellek / swap (swap kullanimi yuksekse sunucu her seyi yavaslatir)"
free -m
line "Disk doluluk (% 90 ustu yavaslatir)"
df -h / /var/lib/docker 2>/dev/null
line "Disk gecikmesi (1 kez 64 MB senkron yazma)"
dd if=/dev/zero of=/tmp/lf-ddtest bs=1M count=64 oflag=dsync 2>&1 | tail -1; rm -f /tmp/lf-ddtest

line "Konteynerler"
docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}'
line "Konteyner kaynak kullanimi (anlik)"
docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}\t{{.BlockIO}}'
line "Docker log dosyasi boyutlari (cok buyukse disk/IO sorunu)"
for c in $APP $DB; do f=$(docker inspect --format '{{.LogPath}}' $c 2>/dev/null); [ -n "$f" ] && sudo du -h "$f" 2>/dev/null || echo "$c: log yolu okunamadi (sudo gerekebilir)"; done
line "Uygulamanin ortami"
docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' $APP 2>/dev/null | grep -E '^ASPNETCORE_ENVIRONMENT|^TZ' || true
line "Son 1 saatte log satiri sayisi (binlerce ise eski surum: log yuku)"
docker logs --since 1h $APP 2>&1 | wc -l

line "Sunucunun kendisinden yanit sureleri (ag haric)"
for u in /health /Account/Login; do
  curl -s -o /dev/null -w "$u  baglanti %{time_connect}s  ilk bayt %{time_starttransfer}s  toplam %{time_total}s  boyut %{size_download}B\n" "http://127.0.0.1:$PORT$u"
done
line "Ayni olcum 5 kez (/Account/Login) - tutarsizsa CPU/IO sikismasi"
for i in 1 2 3 4 5; do curl -s -o /dev/null -w "%{time_starttransfer}s\n" "http://127.0.0.1:$PORT/Account/Login"; done

line "Konteyner icinden DNS ve dis servis erisimi (yavassa: DNS/IPv6/proxy sorunu)"
for h in fonts.googleapis.com generativelanguage.googleapis.com api.groq.com google.serper.dev api.apollo.io; do
  docker exec $APP sh -c "command -v getent >/dev/null && (time -p getent hosts $h) 2>&1 | tr '\n' ' '" 2>/dev/null; echo " <- $h"
done
line "Sunucudan dis HTTPS erisim suresi"
for u in https://fonts.googleapis.com https://api.groq.com https://google.serper.dev; do
  curl -s -o /dev/null -m 15 -w "$u  dns %{time_namelookup}s  baglanti %{time_connect}s  toplam %{time_total}s  kod %{http_code}\n" "$u" || echo "$u ulasilamadi"
done
line "IPv6 (konteyner IPv6 adresi bulup baglanamiyorsa her dis cagri zaman asimina kadar bekler)"
docker exec $APP sh -c 'cat /proc/net/if_inet6 2>/dev/null | wc -l' 2>/dev/null | sed 's/^/IPv6 arayuz sayisi: /'

line "Veritabani"
docker exec $DB psql -U "${POSTGRES_USER:-egebis}" -d "${POSTGRES_DB:-egebisleadfinder}" -tc \
  "select 'veritabani boyutu: ' || pg_size_pretty(pg_database_size(current_database()));" 2>/dev/null
docker exec $DB psql -U "${POSTGRES_USER:-egebis}" -d "${POSTGRES_DB:-egebisleadfinder}" -tc \
  "select relname || ': ' || n_live_tup from pg_stat_user_tables order by n_live_tup desc limit 8;" 2>/dev/null

line "2 saniyeyi asan istekler (yeni surumde WRN olarak yazilir)"
docker logs --since 24h $APP 2>&1 | grep -E 'WRN\] HTTP' | tail -20
line "Bitti"
