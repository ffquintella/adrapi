using System;
using System.Buffers.Text;
using Novell.Directory.Ldap;
using Novell.Directory.Ldap.Controls;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using adrapi.Ldap.Security;

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

        private static string GetTypeFilter(LdapSearchType type, string filter = "")
        {
            switch (type) {
                case LdapSearchType.User:
                    if (filter == "")
                    {
                        return $"(&(objectClass=user)(objectCategory=person))";
                    }

                    return $"(&(objectClass=user)(objectCategory=person)(" +
                           LdapInjectionControll.EscapeForSearchFilterAllowWC(filter) + "))";
                case LdapSearchType.Group:
                    if (filter == "")
                    {
                        return $"((objectClass=group)"; 
                    }

                    return $"(&(objectClass=group)(" + LdapInjectionControll.EscapeForSearchFilterAllowWC(filter) +
                           "))";
                case LdapSearchType.OU:
                    if (filter == "")
                    {
                        return $"(&(ou=*)(objectClass=organizationalunit))"; 
                    }

                    return $"(&(ou=*)(objectClass=organizationalunit)(" +
                           LdapInjectionControll.EscapeForSearchFilterAllowWC(filter) + "))";
                case LdapSearchType.Machine:
                    if (filter == "")
                    {
                        return $"(objectClass=computer)";
                    }

                    return $"(&(objectClass=computer)(" + LdapInjectionControll.EscapeForSearchFilterAllowWC(filter) +
                           "))";
                default:
                    throw new domain.Exceptions.WrongParameterException("Search type not specified");
            }
        }
        
        public LdapMessageQueue SendSearch(string searchBase, LdapSearchType type, string filter = "")
        {
            return SendSearch(searchBase, GetTypeFilter(type, filter));
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

        public List<LdapEntry> ExecuteSearch(string searchBase, LdapSearchType type, string filter = "")
        {
            return ExecuteSearch(searchBase, GetTypeFilter(type, filter));
        }

        public List<LdapEntry> ExecuteSearch(string searchBase, string filter = "")
        {
            var results = new List<LdapEntry>();
            
            var lcm = LdapConnectionManager.Instance;
            var conn = lcm.GetConnection();
            var sb = searchBase + config.searchBase;
            
            LdapControl[] requestControls = new LdapControl[1];

            LdapSortKey[] keys = new LdapSortKey[1];
            keys[0] = new LdapSortKey("cn"); //samaccountname
            // Create the sort control 
            requestControls[0] = new LdapSortControl(keys, true);
            
            // Set the controls to be sent as part of search request
            LdapSearchConstraints cons = conn.SearchConstraints;
            cons.SetControls(requestControls);
            conn.Constraints = cons;
            
            LdapSearchResults resps = (LdapSearchResults)conn.Search(sb, LdapConnection.ScopeSub, filter, null, false, (LdapSearchConstraints)null);

            //var resps = SendSearch(searchBase, type, filter);
            
            while (resps.HasMore())
            {

                /* Get next returned entry.  Note that we should expect a Ldap-
                *Exception object as well just in case something goes wrong
                */
                LdapEntry nextEntry = null;
                try
                {
                    nextEntry = resps.Next();
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
        

        public List<LdapEntry> ExecuteLimitedSearch(string searchBase, LdapSearchType type, int start, int end, string filter = "")
        {
            return ExecuteLimitedSearch(searchBase, GetTypeFilter(type, filter), start, end);
        }

        private int getSearchSize(string searchBase, string filter)
        {
            var results = new List<LdapEntry>();

            var lcm = LdapConnectionManager.Instance;
            var conn = lcm.GetConnection();

            var sb = searchBase + config.searchBase;

            LdapControl[] requestControls = new LdapControl[2];

            LdapSortKey[] keys = new LdapSortKey[1];
            keys[0] = new LdapSortKey("cn"); //samaccountname

            // Create the sort control 
            requestControls[0] = new LdapSortControl(keys, true);

            requestControls[1] = new LdapVirtualListControl(1, 0, 1, config.maxResults);
            
            //requestControls[1] = new LdapVirtualListControl(sb,0, config.maxResults, null);

            // Set the controls to be sent as part of search request
            LdapSearchConstraints cons = conn.SearchConstraints;
            cons.SetControls(requestControls);
            conn.Constraints = cons;


            // Send the search request - Synchronous Search is being used here 
            logger.Debug("Calling Asynchronous Search...");
            LdapSearchResults res = (LdapSearchResults) conn.Search(sb, LdapConnection.ScopeSub, filter, null, false,
                (LdapSearchConstraints) null);
            while (res.HasMore())
            {
                res.Next();
            }

            // Server should send back a control irrespective of the
            // status of the search request
            LdapControl[] controls = res.ResponseControls;
            if (controls == null)
            {
                logger.Debug("No controls returned");
            }
            else
            {

                // We are likely to have multiple controls returned
                for (int i = 0; i < controls.Length; i++)
                {


                    /* Is this a VLV Response Control */
                    if (controls[i] is LdapVirtualListResponse)
                    {

                        logger.Debug("Received VLV Response Control from " + "Server...");

                        /* Get all returned fields */
                        int firstPosition = ((LdapVirtualListResponse) controls[i]).FirstPosition;
                        int ContentCount = ((LdapVirtualListResponse) controls[i]).ContentCount;
                        int resultCode = ((LdapVirtualListResponse) controls[i]).ResultCode;
                        System.String context = ((LdapVirtualListResponse) controls[i]).Context;

                        /* Print out the returned fields.  Typically you would
                        * have used these fields to reissue another VLV request
                        * or to display the list on a GUI
                        */
                        logger.Debug("Result Code    => " + resultCode);
                        logger.Debug("First Position => " + firstPosition);
                        logger.Debug("Content Count  => " + ContentCount);
                        if ((System.Object) context != null)
                            logger.Debug("Context String => " + context);
                        else
                            logger.Debug("No Context String in returned" + " control");

                        return ContentCount;
                    }
                }
            }

            return -1;
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
            
            int sSize = getSearchSize(searchBase, filter);
            
            var results = new List<LdapEntry>();

            var lcm = LdapConnectionManager.Instance;
            var conn = lcm.GetConnection();

            var sb = searchBase + config.searchBase;

            LdapControl[] requestControls = new LdapControl[2];

            LdapSortKey[] keys = new LdapSortKey[1];
            keys[0] = new LdapSortKey("cn"); //samaccountname

            // Create the sort control 
            requestControls[0] = new LdapSortControl(keys, true);
            
            logger.Debug("Search Size:" + sSize);

            requestControls[1] = new LdapVirtualListControl(start, 0, end, sSize);
            
            //requestControls[1] = new LdapVirtualListControl(filter,0, end, null);

            // Set the controls to be sent as part of search request
            LdapSearchConstraints cons = conn.SearchConstraints;
            cons.SetControls(requestControls);
            conn.Constraints = cons;


            // Send the search request - Synchronous Search is being used here 
            logger.Debug("Calling Asynchronous Search...");
            LdapSearchResults res = (LdapSearchResults)conn.Search(sb, LdapConnection.ScopeSub, filter, null, false, (LdapSearchConstraints)null);

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
            LdapControl[] controls = res.ResponseControls;
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
                        System.String bad = ((LdapSortResponse) controls[i]).FailedAttribute;
                        int result = ((LdapSortResponse) controls[i]).ResultCode;

                        // Print out error code (0 if no error) and any
                        // returned attribute
                        logger.Debug("Error code: " + result);
                        if ((System.Object) bad != null)
                            logger.Debug("Offending " + "attribute: " + bad);
                        else
                            logger.Debug("No offending " + "attribute " + "returned");
                    }

                    /* Is this a VLV Response Control */
                    if (controls[i] is LdapVirtualListResponse)
                    {

                        logger.Debug("Received VLV Response Control from " + "Server...");

                        /* Get all returned fields */
                        int firstPosition = ((LdapVirtualListResponse) controls[i]).FirstPosition;
                        int ContentCount = ((LdapVirtualListResponse) controls[i]).ContentCount;
                        int resultCode = ((LdapVirtualListResponse) controls[i]).ResultCode;
                        System.String context = ((LdapVirtualListResponse) controls[i]).Context;

                        /* Print out the returned fields.  Typically you would
                        * have used these fields to reissue another VLV request
                        * or to display the list on a GUI
                        */
                        logger.Debug("Result Code    => " + resultCode);
                        logger.Debug("First Position => " + firstPosition);
                        logger.Debug("Content Count  => " + ContentCount);
                        if ((System.Object) context != null)
                            logger.Debug("Context String => " + context);
                        else
                            logger.Debug("No Context String in returned" + " control");
                    }
                }
            }

            return results;
        }

        public LdapPagedResponse ExecutePagedSearch(string searchBase, LdapSearchType type, string filter = "", string cookie = "")
        {
            
            return ExecutePagedSearch(searchBase, GetTypeFilter(type,filter), cookie);
            
        }


        /// <summary>
        /// Executes the paged search.
        /// </summary>
        /// <returns>The paged search.</returns>
        /// <param name="searchBase">Search base.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="cookie">Cookie to restore last search.</param>
        public LdapPagedResponse ExecutePagedSearch(string searchBase, string filter, string cookie = "")
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
            keys[0] = new LdapSortKey("cn");

            // Create the sort control 
            requestControls[0] = new LdapSortControl(keys, true);

            /* Create the VLV Control.
            * These two fields in the VLV Control identify the before and 
            * after count of entries to be returned 
            */
            int beforeCount = 0;
            int afterCount = 0;
            //int afterCount = config.maxResults -1;

            //System.String cookie = "";

            if (cookie != "")
            {
                byte[] data = System.Convert.FromBase64String(cookie);
                cookie = System.Text.ASCIIEncoding.ASCII.GetString(data);
            }
            
            
            requestControls[1] = new LdapPagedResultsControl(config.maxResults, cookie);
            
            // Set the controls to be sent as part of search request
            LdapSearchConstraints cons = conn.SearchConstraints;
            cons.SetControls(requestControls);
            conn.Constraints = cons;


            // Send the search request - Synchronous Search is being used here 
            logger.Debug("Calling Asynchronous Search...");

            string[] attrs = null;
            
            ILdapSearchResults res = (LdapSearchResults)conn.Search(sb, LdapConnection.ScopeSub, filter, attrs, false, (LdapSearchConstraints)null);

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

            var response = new LdapPagedResponse {Entries = results};

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
                    if (controls[i] is LdapPagedResultsResponse)
                    {
                        
                        logger.Debug("Received Ldap Paged Control from Server");
                        
                        LdapPagedResultsResponse cresp = new LdapPagedResultsResponse(controls[i].Id, controls[i].Critical, controls[i].GetValue());

                        cookie = cresp.Cookie;

                        byte[] hexCookie = System.Text.Encoding.ASCII.GetBytes(cookie);
                        response.Cookie = Convert.ToBase64String(hexCookie);

                        /*
                        // Cookie is an opaque octet string. The chacters it contains might not be printable.
                        byte[] hexCookie = System.Text.Encoding.ASCII.GetBytes(cookie);
                        StringBuilder hex = new StringBuilder(hexCookie.Length);
                        foreach (byte b in hexCookie)
                            hex.AppendFormat("{0:x}", b);

                        System.Console.Out.WriteLine("Cookie: {0}", hex.ToString());
                        System.Console.Out.WriteLine("Size: {0}", cresp.Size);
                        */
                    }

       
                }
            }


            return response; 
        }

        public LdapEntry GetRegister(string DN, string[] attrs = null)
        {
            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection(true);

            LdapEntry res;
            if (attrs == null)
                res = con.Read(DN);
            else
                res = con.Read(DN, attrs);
           
            return res;

        }

        #endregion

        #region WRITE

        public void AddEntry(LdapEntry entry)
        {
            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection(true);

            //Add the entry to the directory
            con.Add(entry);
            

            return;
        }

        public void DeleteEntry(String dn)
        {
            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection(true);

            con.Delete(dn);

            return;
        }

        public void SaveEntry(String dn, LdapModification[] modList)
        {
            var lcm = LdapConnectionManager.Instance;
            var con = lcm.GetConnection(true);

            //Add the entry to the directory
            con.Modify(dn, modList);

            return;
        }

        #endregion


    }
}
