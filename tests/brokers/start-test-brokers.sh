#!/usr/bin/env bash
# Starts the throwaway Mosquitto brokers used by the MqttBroker integration tests (Linux).
#   1883: anonymous access allowed
#   1884: requires user "relay" / password "correct-horse", like the Home Assistant Mosquitto add-on
# Never point the tests at your real Home Assistant broker.
set -euo pipefail

dir="${RUNNER_TEMP:-/tmp}/shr-test-brokers"
mkdir -p "$dir"

# Anonymous broker (skip if something already listens on 1883, e.g. the distro's mosquitto service).
if ! (exec 3<>/dev/tcp/127.0.0.1/1883) 2>/dev/null; then
  mosquitto -p 1883 -d
fi

# Password-protected broker.
rm -f "$dir/passwd"
touch "$dir/passwd"
chmod 600 "$dir/passwd"
mosquitto_passwd -b "$dir/passwd" relay correct-horse
cat > "$dir/auth.conf" <<CONF
listener 1884
allow_anonymous false
password_file $dir/passwd
CONF

# Newer Ubuntu (26.04+, the current ubuntu-latest runner) ships an AppArmor profile that only lets
# /usr/sbin/mosquitto read files under /etc/mosquitto. AppArmor profiles attach by program path, so a
# copy of the binary in our temp folder can read the config above. No sudo or system config changes.
cp "$(command -v mosquitto)" "$dir/mosquitto"
"$dir/mosquitto" -c "$dir/auth.conf" -d

# Wait until both answer.
for port in 1883 1884; do
  for _ in $(seq 50); do
    if (exec 3<>/dev/tcp/127.0.0.1/$port) 2>/dev/null; then break; fi
    sleep 0.1
  done
done
echo "Test brokers ready on 1883 (anonymous) and 1884 (relay/correct-horse)."
