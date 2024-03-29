﻿using System;
using Novell.Directory.Ldap;
using System.Collections.Generic;
using adrapi.domain.Exceptions;
using NLog;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using RemoteCertificateValidationCallback = System.Net.Security.RemoteCertificateValidationCallback;


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
        private List<LdapConnection> CleanConnectionPool;

        public LdapConnection GetConnection(bool clean = false)
        {
            var ldapConf = new Ldap.LdapConfig();
            return this.GetConnection(ldapConf, clean);
        }


        public LdapConnection GetConnection(LdapConfig config, bool clean = false)
        {

            int LdapVersion = LdapConnection.LdapV3;

            if (config == null) throw new NullException("Config cannot be null");

            if(ConnectionPool == null)
            {
                ConnectionPool = new List<LdapConnection>();
                CleanConnectionPool = new List<LdapConnection>();

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

                    //LdapConnection connection = new LdapConnection(options);

                    var cn = new LdapConnection(options);
                    var cnClean = new LdapConnection(options);


                    var server = GetOptimalSever(config.servers);
                    var server2 = GetOptimalSever(config.servers);

                    cn.Connect(server.FQDN, server.Port);
                    cnClean.Connect(server2.FQDN, server2.Port);

                    try
                    {
                        cn.Bind(LdapVersion, config.bindDn, config.bindCredentials);
                        cnClean.Bind(LdapVersion, config.bindDn, config.bindCredentials);
                    }
                    catch(Exception ex)
                    {
                        logger.Error(ex, "Error on bind opperation");

                        throw new domain.Exceptions.InvalidCredentialsException(ex.Message);
                    }

                    //TODO: Verify connection and treat errors

                    ConnectionPool.Add(cn);
                    CleanConnectionPool.Add(cnClean);

                }

            }

            // GET a Random open Connection
            //TODO: Optimize code
            var rnd = new Random();

            int sorted;
            LdapConnection con;

            if (clean)
            {
                sorted = rnd.Next(0, CleanConnectionPool.Count);
                con = CleanConnectionPool[sorted];
            }
            else
            {
                sorted = rnd.Next(0, ConnectionPool.Count);
                con = ConnectionPool[sorted];
            }


            if (!con.Connected)
            {
                var srv = GetOptimalSever(config.servers);
                con.Connect(srv.FQDN, srv.Port);
                try
                {
                    con.Bind(LdapVersion, config.bindDn, config.bindCredentials);
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

            /*
            LdapControl[] requestControls = new LdapControl[0];

            LdapConstraints lc = new LdapConstraints();
            lc.SetControls(requestControls);

            con.Constraints = lc;
            */


            return con;
        }


        public bool ValidateAuthentication(string login, string password)
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

            var cn = new LdapConnection(options);

            cn.Connect(server.FQDN, server.Port);

            ldapConf.bindDn = login;
            ldapConf.bindCredentials = password;


            try
            {
                cn.Bind(LdapVersion, ldapConf.bindDn, ldapConf.bindCredentials);
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
