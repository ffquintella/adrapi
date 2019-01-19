#!/bin/bash

set -e

/opt/puppetlabs/puppet/bin/puppet apply  --modulepath=/etc/puppet/modules /etc/puppet/manifests/start.pp



while [ ! -f /var/log/adrapi/internal-nlog.txt ]
do
  sleep 2
done
ls -l /var/log/adrapi/internal-nlog.txt

tail -n 0 -f /var/log/adrapi/internal-nlog.txt &
wait