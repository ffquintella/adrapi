using System;
using Novell.Directory.Ldap;
using System.Collections.Generic;
using System.Collections.Concurrent;
using adrapi.domain.Exceptions;
using adrapi.domain.Security;
using adrapi.Ldap.Security;
using NLog;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Threading.Tasks;
using System.Threading;
using RemoteCertificateValidationCallback = System.Net.Security.RemoteCertificateValidationCallback;


namespace adrapi.Ldap
{
    public class LdapConnectionManager
    {

        public NLog.Logger logger;

        #region SINGLETON

        private static readonly Lazy<LdapConnectionManager> Lazy = new Lazy<LdapConnectionManager>(() => new LdapConnectionManager());

        public static LdapConnectionManager Instance { get { return Lazy.Value; } }

        private LdapConnectionManager()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
        }

        #endregion

        // Connection pools are bucketed per-domain (keyed by LdapConfig.DomainKey)
        // so requests to different directories never share a bound connection.
        private readonly ConcurrentDictionary<string, List<LdapConnection>> connectionPools = new();
        private readonly ConcurrentDictionary<string, List<LdapConnection>> cleanConnectionPools = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> poolInitSemaphores = new();

        public async Task<LdapConnection> GetConnectionAsync(bool clean = false)
        {
            var ldapConf = LdapDomainRegistry.Instance.GetConfig(null);
            return await this.GetConnectionAsync(ldapConf, clean);
        }


        public async Task<LdapConnection> GetConnectionAsync(LdapConfig config, bool clean = false)
        {

            int LdapVersion = LdapConnection.LdapV3;

            if (config == null) throw new NullException("Config cannot be null");
            if (config.servers == null || config.servers.Length == 0)
                throw new WrongParameterException("No LDAP servers configured. Set ldap.servers in appsettings.");

            if (config.poolSize <= 0)
            {
                logger.Warn("Invalid ldap.poolSize={poolSize}. Falling back to 1.", config.poolSize);
                config.poolSize = 1;
            }

            var domainKey = LdapDomainRegistry.NormalizeKey(config.DomainKey);

            await EnsureConnectionPoolsAsync(domainKey, config, LdapVersion);

            // GET a Random open Connection
            var rnd = new Random();

            var pool = clean
                ? cleanConnectionPools.GetValueOrDefault(domainKey)
                : connectionPools.GetValueOrDefault(domainKey);
            if (pool == null || pool.Count == 0)
                throw new WrongParameterException("LDAP connection pool is empty. Check ldap.poolSize and ldap.servers settings.");

            var sorted = rnd.Next(0, pool.Count);
            var con = pool[sorted];


            if (!con.Connected)
            {
                var srv = GetOptimalSever(config.servers);
                try
                {
                    await con.ConnectAsync(srv.FQDN, srv.Port);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error connecting to LDAP server {server}:{port}", srv.FQDN, srv.Port);
                    throw new WrongParameterException(
                        $"Failed to connect to LDAP server {srv.FQDN}:{srv.Port}. Check ldap.servers and ldap.ssl settings.");
                }
                try
                {
                    await con.BindAsync(LdapVersion, config.bindDn, config.bindCredentials);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error on bind opperation");

                    throw new domain.Exceptions.InvalidCredentialsException(ex.Message);
                }
            }

            if(!con.Connected)
            {
                logger.Error("Error using a closed connection");
            }
            else
            {
                logger.Debug("Connected to server: {server} on port: {port}", con.Host, con.Port);
            }

            return con;
        }

        private async Task EnsureConnectionPoolsAsync(string domainKey, LdapConfig config, int ldapVersion)
        {
            if (connectionPools.TryGetValue(domainKey, out var existing) && existing.Count > 0
                && cleanConnectionPools.TryGetValue(domainKey, out var existingClean) && existingClean.Count > 0)
            {
                return;
            }

            var semaphore = poolInitSemaphores.GetOrAdd(domainKey, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                if (connectionPools.TryGetValue(domainKey, out var ready) && ready.Count > 0
                    && cleanConnectionPools.TryGetValue(domainKey, out var readyClean) && readyClean.Count > 0)
                {
                    return;
                }

                var pool = new List<LdapConnection>();
                var cleanPool = new List<LdapConnection>();

                try
                {
                    // Pin store is only needed when SSL is enabled; constructing it
                    // requires a non-empty path which plain-LDAP configs don't set.
                    var pinStore = config.ssl
                        ? new LdapCertificatePinStore(config.trustedCertificatesFile ?? "cfg/ldap-trusted-certs.json")
                        : null;

                    for (short openConn = 0; openConn < config.poolSize; openConn++)
                    {
                        var server = GetOptimalSever(config.servers);
                        var server2 = GetOptimalSever(config.servers);

                        var cn = new LdapConnection(BuildOptions(config, pinStore, server.FQDN));
                        var cnClean = new LdapConnection(BuildOptions(config, pinStore, server2.FQDN));

                        try
                        {
                            await cn.ConnectAsync(server.FQDN, server.Port);
                            await cnClean.ConnectAsync(server2.FQDN, server2.Port);
                        }
                        catch (Exception ex)
                        {
                            cn.Dispose();
                            cnClean.Dispose();
                            logger.Error(ex, "Error connecting to LDAP servers while initializing pool.");
                            throw new WrongParameterException(
                                $"Failed to connect to LDAP server(s). Check ldap.servers and ldap.ssl settings. Last tried: {server.FQDN}:{server.Port}");
                        }

                        try
                        {
                            await cn.BindAsync(ldapVersion, config.bindDn, config.bindCredentials);
                            await cnClean.BindAsync(ldapVersion, config.bindDn, config.bindCredentials);
                        }
                        catch (Exception ex)
                        {
                            cn.Dispose();
                            cnClean.Dispose();
                            logger.Error(ex, "Error on bind opperation");
                            throw new domain.Exceptions.InvalidCredentialsException(ex.Message);
                        }

                        pool.Add(cn);
                        cleanPool.Add(cnClean);
                    }

                    connectionPools[domainKey] = pool;
                    cleanConnectionPools[domainKey] = cleanPool;
                    logger.Info("LDAP connection pool for domain '{domain}' initialized with {poolSize} connections.", domainKey, pool.Count);
                }
                catch
                {
                    foreach (var c in pool) c.Dispose();
                    foreach (var c in cleanPool) c.Dispose();
                    connectionPools.TryRemove(domainKey, out _);
                    cleanConnectionPools.TryRemove(domainKey, out _);
                    throw;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }


        public async Task<bool> ValidateAuthenticationAsync(string login, string password, LdapConfig config = null)
        {
            int LdapVersion = LdapConnection.LdapV3;

            var ldapConf = config ?? LdapDomainRegistry.Instance.GetConfig(null);

            var server = GetOptimalSever(ldapConf.servers);

            logger.Debug("Authenticating user: {login} on server: {server}", login, server);


            var pinStore = ldapConf.ssl
                ? new LdapCertificatePinStore(ldapConf.trustedCertificatesFile ?? "cfg/ldap-trusted-certs.json")
                : null;
            using var cn = new LdapConnection(BuildOptions(ldapConf, pinStore, server.FQDN));

            await cn.ConnectAsync(server.FQDN, server.Port);

            // Bind with the supplied credentials directly; never mutate the
            // (possibly cached) domain config with caller credentials.
            try
            {
                await cn.BindAsync(LdapVersion, login, password);
                cn.Disconnect();
                return true;
            }
            catch (Exception ex)
            {
                logger.Info(ex, "Authentication failed for login:{user}", login);
                return false;
            }

        }



        /// <summary>
        /// Builds connection options with strict TLS validation when SSL is enabled.
        /// The validator accepts certificates trusted by the system CA store, or
        /// those pinned (by SHA-256) for the target host in the pin store.
        /// </summary>
        private static LdapConnectionOptions BuildOptions(LdapConfig config, LdapCertificatePinStore pinStore, string targetHost)
        {
            if (!config.ssl) return new LdapConnectionOptions();

            var validator = new LdapCertificateValidator(pinStore, targetHost);
            return new LdapConnectionOptions()
                .ConfigureRemoteCertificateValidationCallback(
                    new RemoteCertificateValidationCallback(validator.Validate))
                .UseSsl();
        }

        private LdapServer GetOptimalSever(string[] servers)
        {
            //TODO: Implement sorting logic -- for now it's just random
            if (servers == null || servers.Length == 0)
                throw new WrongParameterException("No LDAP servers configured.");

            var rnd = new Random();

            int sorted = rnd.Next(0, servers.Length);

            string srvStr = servers[sorted];
            if (string.IsNullOrWhiteSpace(srvStr) || !srvStr.Contains(":"))
                throw new WrongParameterException($"Invalid LDAP server format: '{srvStr}'. Expected 'host:port'.");

            var lserver = new LdapServer();
            lserver.FQDN = srvStr.Split(':')[0];
            lserver.Port = Convert.ToInt16(srvStr.Split(':')[1]);

            return lserver;

        }

    }
}
