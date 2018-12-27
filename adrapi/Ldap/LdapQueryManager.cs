﻿using System;
using Novell.Directory.Ldap;
using Novell.Directory.Ldap.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace adrapi.Ldap
{
    public class LdapQueryManager
    {
        public NLog.Logger logger;

        private LdapConfig config;

        #region SINGLETON

        private static readonly Lazy<LdapQueryManager> lazy = new Lazy<LdapQueryManager>(() => new LdapQueryManager());

        public static LdapQueryManager Instance { get { return lazy.Value; } }

        private LdapQueryManager()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
            config = new Ldap.LdapConfig();
        }
        #endregion

        #region READ
        public LdapMessageQueue SendSearch(string searchBase, LdapSearchType type)
        {
            switch (type) {
                case LdapSearchType.User:
                    logger.Debug("Serching all users");
                    return SendSearch(searchBase, $"(&(objectClass=user)(objectCategory=person))");
                case LdapSearchType.Group:
                    logger.Debug("Serching all groups");
                    return SendSearch(searchBase, $"(objectClass=group)");
                case LdapSearchType.OU:
                    logger.Debug("Serching all OUs");
                    return SendSearch(searchBase, $"(&(ou=*)(objectClass=organizationalunit))");
                case LdapSearchType.Machine:
                    logger.Debug("Serching all computers");
                    return SendSearch(searchBase, $"(objectClass=computer)");
                default:
                    logger.Error("Search type not specified.");
                    throw new domain.Exceptions.WrongParameterException("Search type not specified");
            }
        }

        public LdapMessageQueue SendSearch(string searchBase, string filter)
        {
            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection();

            var sb = searchBase + config.searchBase;

            var req = new LdapSearchRequest(sb, LdapConnection.ScopeSub, filter, null, LdapSearchConstraints.DerefNever, config.maxResults, 0, false, null);
            
            var queue = con.SendRequest(req, null);
                        
            return queue;

        }

        public List<LdapEntry> ExecuteLimitedSearch(string searchBase, LdapSearchType type, int start, int end)
        {
            switch (type)
            {
                case LdapSearchType.User:
                    logger.Debug("Serching all users");
                    return ExecuteLimitedSearch(searchBase, $"(&(objectClass=user)(objectCategory=person))", start, end);
                case LdapSearchType.Group:
                    logger.Debug("Serching all groups");
                    return ExecuteLimitedSearch(searchBase, $"(objectClass=group)", start, end);
                case LdapSearchType.OU:
                    logger.Debug("Serching all OUs");
                    return ExecuteLimitedSearch(searchBase, $"(&(ou=*)(objectClass=organizationalunit))", start, end);
                case LdapSearchType.Machine:
                    logger.Debug("Serching all computers");
                    return ExecuteLimitedSearch(searchBase, $"(objectClass=computer)", start, end);
                default:
                    logger.Error("Search type not specified.");
                    throw new domain.Exceptions.WrongParameterException("Search type not specified");
            }
        }

        /// <summary>
        /// Executes the limited search.
        /// </summary>
        /// <returns>The limited search.</returns>
        /// <param name="searchBase">Search base.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="start">Must be 1 or greater</param>
        /// <param name="end">End.</param>
        public List<LdapEntry> ExecuteLimitedSearch(string searchBase, string filter, int start, int end)
        {
            var results = new List<LdapEntry>();

            var lcm = LdapConnectionManager.Instance;
            var conn = lcm.GetConnection();

            var sb = searchBase + config.searchBase;

            LdapControl[] requestControls = new LdapControl[2];

            LdapSortKey[] keys = new LdapSortKey[1];
            keys[0] = new LdapSortKey("name");

            // Create the sort control 
            requestControls[0] = new LdapSortControl(keys, true);


            requestControls[1] = new LdapVirtualListControl(start,
                                     0, end, config.maxResults);

            // Set the controls to be sent as part of search request
            LdapSearchConstraints cons = conn.SearchConstraints;
            cons.SetControls(requestControls);
            conn.Constraints = cons;


            // Send the search request - Synchronous Search is being used here 
            logger.Debug("Calling Asynchronous Search...");
            ILdapSearchResults res = (LdapSearchResults)conn.Search(sb, LdapConnection.ScopeSub, filter, null, false, (LdapSearchConstraints)null);

            // Loop through the results and print them out
            while (res.HasMore())
            {

                /* Get next returned entry.  Note that we should expect a Ldap-
                *Exception object as well just in case something goes wrong
                */
                LdapEntry nextEntry = null;
                try
                {
                    nextEntry = res.Next();
                    results.Add(nextEntry);
                }
                catch (Exception e)
                {
                    if (e is LdapReferralException)
                        continue;
                    else
                    {
                        logger.Error("Search stopped with exception " + e.ToString());
                        break;
                    }
                }

                /* Print out the returned Entries distinguished name.  */
                logger.Debug(nextEntry.Dn);

            }

            return results;
        }

        public List<LdapEntry> ExecutePagedSearch(string searchBase, LdapSearchType type)
        {
            switch (type)
            {
                case LdapSearchType.User:
                    logger.Debug("Serching all users");
                    return ExecutePagedSearch(searchBase, $"(&(objectClass=user)(objectCategory=person))");
                case LdapSearchType.Group:
                    logger.Debug("Serching all groups");
                    return ExecutePagedSearch(searchBase, $"(objectClass=group)");
                case LdapSearchType.OU:
                    logger.Debug("Serching all OUs");
                    return ExecutePagedSearch(searchBase, $"(&(ou=*)(objectClass=organizationalunit))");
                case LdapSearchType.Machine:
                    logger.Debug("Serching all computers");
                    return ExecutePagedSearch(searchBase, $"(objectClass=computer)");
                default:
                    logger.Error("Search type not specified.");
                    throw new domain.Exceptions.WrongParameterException("Search type not specified");
            }
        }


        /// <summary>
        /// Executes the paged search.
        /// </summary>
        /// <returns>The paged search.</returns>
        /// <param name="searchBase">Search base.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="page">Page.</param>
        public List<LdapEntry> ExecutePagedSearch(string searchBase, string filter, int page=0)
        {
            var results = new List<LdapEntry>();

            var lcm = LdapConnectionManager.Instance;
            var conn = lcm.GetConnection();

            var sb = searchBase + config.searchBase;


            // We will be sending two controls to the server 
            LdapControl[] requestControls = new LdapControl[2];


            /* Create the sort key to be used by the sort control 
            * Results should be sorted based on the cn attribute. 
            * See the "NDS and Ldap Integration Guide" for information on
            * Novell eDirectory support of this functionaliry.
            */
            LdapSortKey[] keys = new LdapSortKey[1];
            keys[0] = new LdapSortKey("name");

            // Create the sort control 
            requestControls[0] = new LdapSortControl(keys, true);

            /* Create the VLV Control.
            * These two fields in the VLV Control identify the before and 
            * after count of entries to be returned 
            */
            int beforeCount = 0;
            int afterCount = config.maxResults -1;


            /* The VLV control request can specify the index
            * using one of the two methods described below:
            * 
            * TYPED INDEX: Here we request all objects that have cn greater
            * than or equal to the letter "a" 
            */
            //requestControls[1] = new LdapVirtualListControl("a", beforeCount, afterCount);

            /* The following code needs to be enabled to specify the index 
            * directly */

            /*int offset = 0; //- offset of the index
            int contentCount = 3; // - our estimate of the search result size

            requestControls[1] = new LdapVirtualListControl(offset, 
                                     beforeCount, afterCount, contentCount);
                                     */

            requestControls[1] = new LdapVirtualListControl((page * config.maxResults) + 1,
                                     beforeCount, afterCount, config.maxResults);

            // Set the controls to be sent as part of search request
            LdapSearchConstraints cons = conn.SearchConstraints;
            cons.SetControls(requestControls);
            conn.Constraints = cons;


            // Send the search request - Synchronous Search is being used here 
            logger.Debug("Calling Asynchronous Search...");
            ILdapSearchResults res = (LdapSearchResults)conn.Search(sb, LdapConnection.ScopeSub, filter, null, false, (LdapSearchConstraints)null);

            // Loop through the results and print them out
            while (res.HasMore())
            {

                /* Get next returned entry.  Note that we should expect a Ldap-
                *Exception object as well just in case something goes wrong
                */
                LdapEntry nextEntry = null;
                try
                {
                    nextEntry = res.Next();
                    results.Add(nextEntry);
                }
                catch (Exception e)
                {
                    if (e is LdapReferralException)
                        continue;
                    else
                    {
                        logger.Error("Search stopped with exception " + e.ToString());
                        break;
                    }
                }

                /* Print out the returned Entries distinguished name.  */
                logger.Debug(nextEntry.Dn);



            }

            // Server should send back a control irrespective of the 
            // status of the search request
            LdapControl[] controls = ((LdapSearchResults)res).ResponseControls;
            if (controls == null)
            {
                logger.Debug("No controls returned");
            }
            else
            {

                // We are likely to have multiple controls returned 
                for (int i = 0; i < controls.Length; i++)
                {

                    /* Is this the Sort Response Control. */
                    if (controls[i] is LdapSortResponse)
                    {
                        
                        logger.Debug("Received Ldap Sort Control from " + "Server");

                        /* We could have an error code and maybe a string 
                        * identifying erring attribute in the response control.
                        */
                        System.String bad = ((LdapSortResponse)controls[i]).FailedAttribute;
                        int result = ((LdapSortResponse)controls[i]).ResultCode;

                        // Print out error code (0 if no error) and any 
                        // returned attribute
                        logger.Debug("Error code: " + result);
                        if ((System.Object)bad != null)
                            logger.Debug("Offending " + "attribute: " + bad);
                        else
                            logger.Debug("No offending " + "attribute " + "returned");
                    }

                    /* Is this a VLV Response Control */
                    if (controls[i] is LdapVirtualListResponse)
                    {

                        logger.Debug("Received VLV Response Control from " + "Server...");

                        /* Get all returned fields */
                        int firstPosition = ((LdapVirtualListResponse)controls[i]).FirstPosition;
                        int ContentCount = ((LdapVirtualListResponse)controls[i]).ContentCount;
                        int resultCode = ((LdapVirtualListResponse)controls[i]).ResultCode;
                        System.String context = ((LdapVirtualListResponse)controls[i]).Context;


                        //var vals = ((LdapVirtualListResponse)controls[i]).GetValue();

                        /* Print out the returned fields.  Typically you would 
                        * have used these fields to reissue another VLV request
                        * or to display the list on a GUI 
                        */
                        logger.Debug("Result Code    => " + resultCode);
                        logger.Debug("First Position => " + firstPosition);
                        logger.Debug("Content Count  => " + ContentCount);
                        if ((System.Object)context != null)
                            logger.Debug("Context String => " + context);
                        else
                            logger.Debug("No Context String in returned" + " control");

                        if (ContentCount > config.maxResults * (page + 1)) results.AddRange(ExecutePagedSearch(searchBase, filter, page + 1));
                    }
                }
            }


            return results; 
        }

        public LdapEntry GetRegister(string DN)
        {
            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection(true);

            var res = con.Read(DN);

            return res;

        }

        #endregion

        #region WRITE

        public void SaveEntry(LdapEntry entry)
        {
            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection(true);

            //Add the entry to the directory
            con.Add(entry);
            

            return;
        }

        #endregion


    }
}
