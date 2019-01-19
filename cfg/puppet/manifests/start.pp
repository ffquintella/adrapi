

$packs = split($extra_packs, ";")

$packs.each |String $value| {
  package{$value:
    ensure => present
  }
}

if $pre_run_cmd != '' {
  $real_pre_run_cmd = $pre_run_cmd
} else {
  $real_pre_run_cmd = "echo 0;"
}

# Using Pre-run CMD
exec {'Pre Run CMD':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => $real_pre_run_cmd
} 

# Starting adrapi
exec {'Starting Adrapi':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => "echo \"Starting Adrapi Server ...\"; cd /app;  dotnet adrapi.dll --hosturl http://0.0.0.0:5000 &",
}
