#!/bin/sh
set -eu
umask 077

: "${TURN_REALM:?required}"
: "${LISTENING_IP:?required}"
: "${RELAY_IP:?required}"
: "${EXTERNAL_IP:?required}"
: "${MIN_RELAY_PORT:?required}"
: "${MAX_RELAY_PORT:?required}"
: "${USER_QUOTA:?required}"
: "${TOTAL_QUOTA:?required}"
: "${MAX_BPS:?required}"
: "${BPS_CAPACITY:?required}"
: "${TURN_LISTENER_MODE:?required}"
case "$TURN_REALM" in *[!A-Za-z0-9.-]*|'') echo 'invalid TURN_REALM' >&2; exit 64 ;; esac
case "$TURN_LISTENER_MODE" in UDP|TCP|TLS) ;; *) echo 'TURN_LISTENER_MODE must be UDP, TCP, or TLS' >&2; exit 64 ;; esac
for value in "$LISTENING_IP" "$RELAY_IP" "$EXTERNAL_IP"; do
  case "$value" in *[!A-Fa-f0-9:.]*|'') echo 'invalid TURN IP address text' >&2; exit 64 ;; esac
done
for value in "$MIN_RELAY_PORT" "$MAX_RELAY_PORT" "$USER_QUOTA" "$TOTAL_QUOTA" "$MAX_BPS" "$BPS_CAPACITY"; do
  case "$value" in *[!0-9]*|'') echo 'invalid numeric TURN setting' >&2; exit 64 ;; esac
done

secret_file=/run/secrets/turn_shared_secret
redis_file=/run/secrets/turn_redis_statsdb
deny_file=/run/secrets/turn_denied_peers
if [ ! -r "$secret_file" ] || [ ! -r "$redis_file" ] || [ ! -r "$deny_file" ]; then
  echo 'TURN shared-secret, Redis stats, or control-network deny secret is missing' >&2
  exit 64
fi
turn_secret=$(cat "$secret_file")
redis_statsdb=$(cat "$redis_file")
case "$turn_secret" in ''|*[!A-Za-z0-9+/=]*) echo 'TURN shared secret must be single-line base64' >&2; exit 64 ;; esac
if ! secret_bytes=$(printf '%s' "$turn_secret" | base64 -d 2>/dev/null | wc -c) || [ "$secret_bytes" -lt 32 ]; then
  echo 'TURN shared secret must decode to at least 256 bits' >&2
  exit 64
fi
case "$redis_statsdb" in ''|*'"'*|*'
'*) echo 'invalid TURN Redis stats connection text' >&2; exit 64 ;; esac

template=/opt/rsp/turnserver.conf.template
output=/tmp/turnserver.conf
while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in
    'static-auth-secret={{TURN_SHARED_SECRET}}') printf 'static-auth-secret=%s\n' "$turn_secret" ;;
    'redis-statsdb="{{REDIS_STATSDB}}"') printf 'redis-statsdb="%s"\n' "$redis_statsdb" ;;
    '{{LISTENER_POLICY}}')
      case "$TURN_LISTENER_MODE" in
        UDP) printf 'no-tcp\nno-tls\nno-dtls\n' ;;
        TCP) printf 'no-udp\nno-tls\nno-dtls\n' ;;
        TLS) printf 'no-udp\nno-tcp\nno-dtls\n' ;;
      esac
      ;;
    '{{EXTRA_DENIED_PEERS}}')
      deny_count=0
      while IFS= read -r deny || [ -n "$deny" ]; do
        case "$deny" in
          denied-peer-ip=*[!A-Fa-f0-9:.-]*) echo 'invalid extra denied-peer range' >&2; exit 64 ;;
          denied-peer-ip=*) printf '%s\n' "$deny"; deny_count=$((deny_count + 1)) ;;
          *) echo 'extra denied-peer file must contain denied-peer-ip lines' >&2; exit 64 ;;
        esac
      done < "$deny_file"
      if [ "$deny_count" -eq 0 ]; then echo 'extra denied-peer file must not be empty' >&2; exit 64; fi
      ;;
    *)
      line=$(printf '%s' "$line" | sed \
        -e "s|{{TURN_REALM}}|$TURN_REALM|g" \
        -e "s|{{LISTENING_IP}}|$LISTENING_IP|g" \
        -e "s|{{RELAY_IP}}|$RELAY_IP|g" \
        -e "s|{{EXTERNAL_IP}}|$EXTERNAL_IP|g" \
        -e "s|{{MIN_RELAY_PORT}}|$MIN_RELAY_PORT|g" \
        -e "s|{{MAX_RELAY_PORT}}|$MAX_RELAY_PORT|g" \
        -e "s|{{USER_QUOTA}}|$USER_QUOTA|g" \
        -e "s|{{TOTAL_QUOTA}}|$TOTAL_QUOTA|g" \
        -e "s|{{MAX_BPS}}|$MAX_BPS|g" \
        -e "s|{{BPS_CAPACITY}}|$BPS_CAPACITY|g")
      printf '%s\n' "$line"
      ;;
  esac
done < "$template" > "$output"

exec turnserver -c "$output"
