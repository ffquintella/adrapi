using System;
using Novell.Directory.Ldap;
using System.Collections.Generic;
using adrapi.domain.Exceptions;
using NLog;


namespace adrapi.Ldap
{
    public class LdapConnectionManager
    {

        public NLog.Logger logger;

        #region SINGLETON

        private static readonly Lazy<LdapConnectionManager> lazy = new Lazy<LdapConnectionManager>(() => new LdapConnectionManager());

        public static LdapConnectionManager Instance { get { return lazy.Value; } }

        private LdapConnectionManager()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
        }

        #endregion

        private List<LdapConnection> ConnectionPool;

        public LdapConnection GetConnection()
        {
            var ldapConf = new Ldap.LdapConfig();
            return this.GetConnection(ldapConf);
        }


        public LdapConnection GetConnection(LdapConfig config)
        {

            if (config == null) throw new NullException("Config cannot be null");

            if(ConnectionPool == null)
            {
                ConnectionPool = new List<LdapConnection>();

                for(short openConn = 0; openConn < config.poolSize; openConn++)
                {
                    var cn = new LdapConnection();

                    if (config.ssl) cn.SecureSocketLayer = true;
  
                    var server = GetOptimalSever(config.servers);

                    cn.Connect(server.FQDN, server.Port);

                    try
                    {
                        cn.Bind(config.bindDn, config.bindCredentials);
                    }catch(Exception ex)
                    {
                        logger.Error(ex, "Error on bind opperation");

                        throw new domain.Exceptions.InvalidCredentialsException(ex.Message);
                    }

                    //TODO: Verify connection and treat errors

                    ConnectionPool.Add(cn);

                }

            }

            // GET a Random open Connection
            //TODO: Optimize code
            var rnd = new Random();

            int sorted = rnd.Next(0, ConnectionPool.Count);

            var con = ConnectionPool[sorted];

            if (!con.Connected)
            {
                var srv = GetOptimalSever(config.servers);
                con.Connect(srv.FQDN, srv.Port);
                try
                {
                    con.Bind(config.bindDn, config.bindCredentials);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error on bind opperation");

                    throw new domain.Exceptions.InvalidCredentialsException(ex.Message);
                }
            }

            if(!con.Connected || !con.Connected)
            {
                logger.Error("Error using a closed connection");
            }
            else
            {
                logger.Debug("Connected to server: {server} on port: {port}", con.Host, con.Port);
            }

            return con;
        }


        private LdapServer GetOptimalSever(string[] servers)
        {
            //TODO: Implement sorting logic -- for now it's just random

            var rnd = new Random();

            int sorted = rnd.Next(0, servers.Length);

            string srvStr = servers[sorted];

            var lserver = new LdapServer();
            lserver.FQDN = srvStr.Split(':')[0];
            lserver.Port = Convert.ToInt16(srvStr.Split(':')[1]);

            return lserver;

        }

    }
}
