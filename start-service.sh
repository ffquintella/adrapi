#!/bin/bash

set -e

if command -v puppet >/dev/null 2>&1; then
  PUPPET_BIN="$(command -v puppet)"
elif [ -x /opt/puppetlabs/bin/puppet ]; then
  PUPPET_BIN="/opt/puppetlabs/bin/puppet"
elif [ -x /opt/puppetlabs/puppet/bin/puppet ]; then
  PUPPET_BIN="/opt/puppetlabs/puppet/bin/puppet"
else
  echo "No Puppet/OpenVox binary found. Checked PATH, /opt/puppetlabs/bin/puppet, /opt/puppetlabs/puppet/bin/puppet" >&2
  exit 1
fi

"$PUPPET_BIN" apply --modulepath=/etc/puppet/modules /etc/puppet/manifests/start.pp



while [ ! -f /var/log/adrapi/internal-nlog.txt ]
do
  sleep 2
done
ls -l /var/log/adrapi/internal-nlog.txt

tail -n 0 -f /var/log/adrapi/internal-nlog.txt &
wait
