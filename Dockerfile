FROM mcr.microsoft.com/dotnet/aspnet:10.0

LABEL maintainer="Felipe Quintella <felipe@email>"
LABEL version="0.8.1"
LABEL description="This image contains the adrapi api."


ENV LANG=C.UTF-8
ENV LANGUAGE=C.UTF-8
ENV LC_ALL=C.UTF-8


#ENV FACTER_XXX


ENV FACTER_PRE_RUN_CMD=""
ENV FACTER_EXTRA_PACKS=""

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates wget \
    && wget -q https://apt.voxpupuli.org/openvox8-release-ubuntu24.04.deb -O /tmp/openvox8-release.deb \
    && dpkg -i /tmp/openvox8-release.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends openvox-agent \
    && rm -f /tmp/openvox8-release.deb \
    && rm -rf /var/lib/apt/lists/*

# Puppet stuff all the instalation is donne by puppet
# Just after it we clean up everthing so the end image isn't too big

RUN  mkdir -p /opt/scripts
COPY cfg/puppet/manifests /etc/puppet/manifests/
COPY cfg/puppet/modules /etc/puppet/modules/
COPY start-service.sh /opt/scripts/start-service.sh
RUN chmod +x /opt/scripts/start-service.sh ; ln -s /opt/scripts/start-service.sh /usr/local/bin/start-service  
#RUN chmod +x /opt/scripts/start-service.sh ; /opt/puppetlabs/puppet/bin/puppet apply -l /tmp/puppet.log  --modulepath=/etc/puppet/modules /etc/puppet/manifests/base.pp  ;\
# yum clean all ; rm -rf /tmp/* ; rm -rf /var/cache/* ; rm -rf /var/tmp/* ; rm -rf /var/opt/staging

RUN mkdir /app
COPY artifacts/app /app

#RUN dotnet dev-certs https -q

# Aspnet webserver
EXPOSE 5000/tcp
EXPOSE 5001/tcp

WORKDIR /app

# Configurations folder, install dir
#VOLUME  XXX

#CMD /opt/puppetlabs/puppet/bin/puppet apply -l /tmp/puppet.log  --modulepath=/etc/puppet/modules /etc/puppet/manifests/start.pp
CMD ["/usr/local/bin/start-service"]
