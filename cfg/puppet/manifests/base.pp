package {'sudo':
  ensure => present
}->
package{'zip':
  ensure => present
}

package{'dos2unix':
  ensure => present
}

file{ '/etc/yum.repos.d/epel.repo':
  ensure => present,
  content => '[epel]
name=Extra Packages for Enterprise Linux 7 - $basearch
#baseurl=http://download.fedoraproject.org/pub/epel/7/$basearch
mirrorlist=https://mirrors.fedoraproject.org/metalink?repo=epel-7&arch=$basearch
failovermethod=priority
enabled=1
gpgcheck=0'
}

class { 'jdk_oracle':
  version     => $java_version,
  install_dir => $java_home,
  version_update => $java_version_update,
  version_build  => $java_version_build,
  version_hash  => $java_version_hash,
  package     => 'server-jre'
}

-> file { '/etc/pki/tls/certs/java':
  ensure  => directory
}

-> file { '/etc/pki/tls/certs/java/cacerts':
  ensure  => link,
  target  => '/etc/pki/ca-trust/extracted/java/cacerts'
}

-> file { "/opt/java_home/jdk1.${java_version}.0_${java_version_update}/jre/lib/security/cacerts":
  ensure  => link,
  target  => '/etc/pki/tls/certs/java/cacerts'
}

-> class { 'jira':
  javahome       => $java_home,
  version        => $jira_version,
  installdir     => $jira_installdir,
  homedir        => $jira_home,
  service_manage => false
}

-> file {'/opt/jira-config':
  ensure  => directory,
  source  => "file:///${jira_installdir}/atlassian-jira-software-${jira_version}-standalone/conf",
  recurse => 'true'
} ->

file {'/opt/scripts/fixline.sh':
  mode    => '0777',
  content => 'find . -iname \'*.sh\' | xargs dos2unix',
  require => Package['dos2unix']
} ->

# Fix dos2unix
exec {'dos2unix-fix':
  path    => '/bin:/sbin:/usr/bin:/usr/sbin',
  cwd     => "${jira_installdir}/atlassian-jira-software-${jira_version}-standalone/bin",
  command => '/opt/scripts/fixline.sh'
} ->

exec {'dos2unix-fix-start-service':
  path    => '/bin:/sbin:/usr/bin:/usr/sbin',
  cwd     => "/opt/scripts",
  command => '/opt/scripts/fixline.sh'
} ->

file { '/usr/bin/start-service':
  ensure => link,
  target => '/opt/scripts/start-service.sh',
}

exec {'erase dbconfig':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => "rm -f ${jira_home}/dbconfig.xml"
} ->

# Full update
exec {'Full update':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'yum -y update'
} ->
# Cleaning unused packages to decrease image size
exec {'erase installer':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /tmp/*; rm -rf /opt/staging/*'
} ->

exec {'erase cache':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/cache/*'
} ->
exec {'erase logs':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/log/*'
}


package {'openssh': ensure => absent }
package {'openssh-clients': ensure => absent }
package {'openssh-server': ensure => absent }
package {'rhn-check': ensure => absent }
package {'rhn-client-tools': ensure => absent }
package {'rhn-setup': ensure => absent }
package {'rhnlib': ensure => absent }

package {'/usr/share/kde4':
  ensure => absent
}
