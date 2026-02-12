using System;
using Novell.Directory.Ldap;
using System.Collections.Generic;
using adrapi.domain.Exceptions;
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

        private List<LdapConnection> ConnectionPool;
        private List<LdapConnection> CleanConnectionPool;
        private readonly SemaphoreSlim poolInitSemaphore = new SemaphoreSlim(1, 1);

        public async Task<LdapConnection> GetConnectionAsync(bool clean = false)
        {
            var ldapConf = new Ldap.LdapConfig();
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

            await EnsureConnectionPoolsAsync(config, LdapVersion);

            // GET a Random open Connection
            var rnd = new Random();

            var pool = clean ? CleanConnectionPool : ConnectionPool;
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

        private async Task EnsureConnectionPoolsAsync(LdapConfig config, int ldapVersion)
        {
            if (ConnectionPool != null && CleanConnectionPool != null
                && ConnectionPool.Count > 0 && CleanConnectionPool.Count > 0)
            {
                return;
            }

            await poolInitSemaphore.WaitAsync();
            try
            {
                if (ConnectionPool != null && CleanConnectionPool != null
                    && ConnectionPool.Count > 0 && CleanConnectionPool.Count > 0)
                {
                    return;
                }

                var pool = new List<LdapConnection>();
                var cleanPool = new List<LdapConnection>();

                try
                {
                    for (short openConn = 0; openConn < config.poolSize; openConn++)
                    {
                        LdapConnectionOptions options;
                        if (config.ssl)
                        {
                            options = new LdapConnectionOptions()
                                .ConfigureRemoteCertificateValidationCallback(
                                    new RemoteCertificateValidationCallback((a, b, c, d) => true))
                                .UseSsl();
                        }
                        else
                        {
                            options = new LdapConnectionOptions();
                        }

                        var cn = new LdapConnection(options);
                        var cnClean = new LdapConnection(options);

                        var server = GetOptimalSever(config.servers);
                        var server2 = GetOptimalSever(config.servers);

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

                    ConnectionPool = pool;
                    CleanConnectionPool = cleanPool;
                    logger.Info("LDAP connection pool initialized with {poolSize} connections.", pool.Count);
                }
                catch
                {
                    foreach (var c in pool) c.Dispose();
                    foreach (var c in cleanPool) c.Dispose();
                    ConnectionPool = null;
                    CleanConnectionPool = null;
                    throw;
                }
            }
            finally
            {
                poolInitSemaphore.Release();
            }
        }


        public async Task<bool> ValidateAuthenticationAsync(string login, string password)
        {
            int LdapVersion = LdapConnection.LdapV3;

            var ldapConf = new Ldap.LdapConfig();

            var server = GetOptimalSever(ldapConf.servers);

            logger.Debug("Authenticating user: {login} on server: {server}", login, server);


            LdapConnectionOptions options;
            if (ldapConf.ssl)
            {
                options = new LdapConnectionOptions()
                    .ConfigureRemoteCertificateValidationCallback(
                        new RemoteCertificateValidationCallback((a, b, c, d) => true))
                    .UseSsl();
            }
            else
            {
                options = new LdapConnectionOptions();
            }

            using var cn = new LdapConnection(options);

            await cn.ConnectAsync(server.FQDN, server.Port);

            ldapConf.bindDn = login;
            ldapConf.bindCredentials = password;


            try
            {
                await cn.BindAsync(LdapVersion, ldapConf.bindDn, ldapConf.bindCredentials);
                cn.Disconnect();
                return true;
            }
            catch (Exception ex)
            {
                logger.Info(ex, "Authentication failed for login:{user}", login);
                return false;
            }

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
