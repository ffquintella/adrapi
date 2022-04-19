using System;
using System.Collections.Generic;
using adrapi.domain;
using adrapi.Ldap;
using System.Linq;
using Novell.Directory.Ldap;
using System.Text;
using adrapi.Ldap.Security;
using adrapi.Models;
using NLog;
using adrapi.Tools;

namespace adrapi
{
    public class UserManager : ObjectManager
    {
        private static readonly string[] userAttrs = new string[] {
            "name",
            "givenName",
            "sn",
            "mail",
            "userPrincipalName",
            "sAMAccountName",
            "description",
            "objectSid",
            "distinguishedName",
            "memberOf",
            "mobile"
        };

        #region SINGLETON

        private static readonly Lazy<UserManager> lazy = new Lazy<UserManager>(() => new UserManager());

        public static UserManager Instance { get { return lazy.Value; } }

        private UserManager()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
        }

        #endregion

        /// <summary>
        /// Return a string list of the users DNs
        /// </summary>
        /// <returns>The list.</returns>
        public UserListResponse GetList(string attribute = "", string filter = "", string cookie = "")
        {
            
            var response = new UserListResponse();
            
            var userNames = new List<string>();
            var users = new List<User>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;

            string formatedFilter = "";

            if (filter != "")
            {
                if (attribute != "")
                    formatedFilter = attribute + "=" + filter;
                else
                    formatedFilter = "cn=" + filter;
            }

            List<LdapEntry> resps;
            

            var presp = sMgmt.ExecutePagedSearch("", LdapSearchType.User, formatedFilter, cookie);

            response.Cookie = presp.Cookie;
            resps = presp.Entries;
            

            foreach(var entry in resps)
            {
                if (attribute == "")
                {
                    //users.Add(entry.GetAttribute("distinguishedName").StringValue);
                    userNames.Add(entry.GetAttribute("samaccountname").StringValue);
                    var user = new User();
                    user.Account = entry.GetAttribute("samaccountname").StringValue;
                    user.ID = entry.GetAttribute("objectSid").StringValue;
                    user.GivenName = entry.GetAttribute("cn").StringValue;


                    if (entry.GetAttributeSet().ContainsKey("memberOf"))
                    {
                        var groupsStr = entry.GetAttribute("memberOf").StringValueArray;
                        foreach (var grp in groupsStr)
                        {
                            var group = new Group();
                            group.Name = grp;
                            user.MemberOf.Add(group);
                        }
                    }

                    users.Add(user);

                }
                else
                    userNames.Add(entry.GetAttribute(attribute).StringValue);
                results++;
            }

            response.UserNames = userNames;
            response.SearchType = "User";
            response.SearchMethod = LdapSearchMethod.Paged;
            response.Users = users;
            
            
            logger.Debug("User search executed results:{result}", results);


            return response;
        }

        /// <summary>
        /// Gets the list. Limited to a start and end number based on the total colection sorted by the name
        /// </summary>
        /// <returns>The list.</returns>
        /// <param name="start">Start.</param>
        /// <param name="end">End.</param>
        /// <param name="attribute">The attribute name to appear on the list</param>
        public UserListResponse GetList(int start, int end, string attribute = "" , string filter = "")
        {
            var response = new UserListResponse();
            
            var users = new List<String>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;

            string formatedFilter = "";

            if (filter != "")
            {
                if (attribute != "")
                    formatedFilter = attribute + "=" + filter;
                else
                    formatedFilter = "cn=" + filter;
            }
            
            var resps = sMgmt.ExecuteLimitedSearch("", LdapSearchType.User, start, end, formatedFilter);
         

            foreach (var entry in resps)
            {
                string u = "";
                if (attribute == "")
                    u = entry.GetAttribute("distinguishedName").StringValue;
                else
                    u = entry.GetAttribute(attribute).StringValue;
                users.Add(u);

                results++;
            }

            response.SearchType = "User";
            response.SearchMethod = LdapSearchMethod.Limited;
            response.UserNames = users;
            logger.Debug("User search executed results:{result}", results);


            return response;
        }

        /// <summary>
        /// Gets the list of all users.
        /// </summary>
        /// <returns>The users.</returns>
        public UserListResponse GetUsers()
        {
            
            var response = new UserListResponse();

            var users = new List<User>();

            var sMgmt = LdapQueryManager.Instance;

            var resps = sMgmt.ExecuteSearch("", LdapSearchType.User);
            int results = 0;

            response.UserNames = new List<string>();
            
            foreach (var entry in resps)
            {
                var u = ConvertfromLdap(entry);
                response.UserNames.Add(u.Name);
                users.Add(u);
                results++;
            }

            logger.Debug("User search executed results:{result}", results);

            response.Users = users;
            
            response.SearchType = "User";
            response.SearchMethod = LdapSearchMethod.Simple;

            return response;
        }


        public UserListResponse GetUsers(int start, int end)
        {
            var response = new UserListResponse();
            var users = new List<User>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;


            var resps = sMgmt.ExecuteLimitedSearch("", LdapSearchType.User, start, end);

            foreach (var entry in resps)
            {
                users.Add(ConvertfromLdap(entry));
                results++;
            }

            logger.Debug("User search executed results:{result}", results);

            response.Users = users;
            
            response.SearchType = "User";
            response.SearchMethod = LdapSearchMethod.Paged;

            return response;
        }

        /// <summary>
        /// Gets the user.
        /// </summary>
        /// <returns>The user.</returns>
        /// <param name="DN">The Disitnguesh name of the user</param>
        /// <<param name="attribute">Optional attribute to use as search base</param>
        public User GetUser (string userID, string attribute = "")
        {
            var sMgmt = LdapQueryManager.Instance;

            try
            {
                
                LdapEntry entry;
                
                if (attribute != "")
                {
                    
                    var results = sMgmt.ExecutePagedSearch("", "(&(objectClass=user)(objectCategory=person)("+LdapInjectionControll.EscapeForSearchFilter(attribute)+"="+LdapInjectionControll.EscapeForSearchFilter(userID)+"))");


                    if (results.Entries.Count == 0)
                    {
                        logger.Debug("User not found {0}", userID);
                        return null;
                    }
                    
                    entry = results.Entries.First();

                }
                else
                {
                    entry = sMgmt.GetRegister(userID, userAttrs);
                }
                
                //entry = sMgmt.GetRegister(userID);
                var user = ConvertfromLdap(entry);
                return user;
            }catch(LdapException ex)
            {
                logger.Debug("User not found {0} Ex: {1}", userID, ex.Message);
                return null;
            }

        }

        /// <summary>
        /// Creates the user on LDAP Directory.
        /// </summary>
        /// <returns> -1 Error </returns>
        /// <returns> 0 OK </returns>
        /// <param name="user">User.</param>
        public int CreateUser(User user)
        {

            //Creates the List attributes of the entry and add them to attributeset

            LdapAttributeSet attributeSet = GetAttributeSet(user);

            // DN of the entry to be added
            string dn = user.DN;

            LdapEntry newEntry = new LdapEntry(dn, attributeSet);


            var qMgmt = LdapQueryManager.Instance;

            try
            {
                qMgmt.AddEntry(newEntry);
                return 0;

            }catch(Exception ex)
            {
                logger.Error("Error saving user");
                logger.Log(LogLevel.Error, ex);
                return -1;
            }

        }

        /// <summary>
        /// Saves the user.
        /// </summary>
        /// <returns>The user. Must have DN set</returns>
        /// <param name="user">User.</param>
        public int SaveUser(User user)
        {

            var qMgmt = LdapQueryManager.Instance;

            var modList = new List<LdapModification>();

            var atributes = GetAttributeSet(user);

            //Get user from the Directory
            try
            {
                var duser = GetUser(user.DN);

                var dattrs = GetAttributeSet(duser);

   
                foreach (LdapAttribute attr in atributes)
                {
                    //TODO: Threat the userAccountControl
                    if (
                        attr.Name != "cn"
                        && attr.Name != "objectclass"
                        && attr.Name != "userAccountControl"
                      )
                    {

                        var b1 = attr.ByteValue;
                        if (dattrs.GetAttribute(attr.Name) != null)
                        {
                            var b2 = dattrs.GetAttribute(attr.Name).ByteValue;

                            var equal = ByteTools.Equality(b1, b2);

                            if (!equal)
                                modList.Add(new LdapModification(LdapModification.Replace, attr));
                        }
                        else
                        {
                            modList.Add(new LdapModification(LdapModification.Replace, attr));
                        }
                    }

           
                }



                try
                {
                    if(modList.Count > 0)
                        qMgmt.SaveEntry(user.DN, modList.ToArray());
                    return 0;

                }
                catch (Exception ex)
                {
                    logger.Error("Error updating user");
                    logger.Log(LogLevel.Error, ex);
                    return -1;
                }

            }catch(Exception ex)
            {
                logger.Error("Error user not found");
                logger.Log(LogLevel.Error, ex);
                return -1;
            }


        }

        public bool ValidateAuthentication(string login, string password)
        {

            LdapConnectionManager lcm = LdapConnectionManager.Instance;

            return lcm.ValidateAuthentication(login, password);

        }


        private LdapAttributeSet GetAttributeSet(User user)
        {
            LdapAttributeSet attributeSet = new LdapAttributeSet();

            attributeSet.Add(new LdapAttribute("objectclass", new string[] { "top", "person", "organizationalPerson", "user" }));
            attributeSet.Add(new LdapAttribute("cn", new string[] { user.Account }));
            attributeSet.Add(new LdapAttribute("name", user.Name));
            attributeSet.Add(new LdapAttribute("sAMAccountName", user.Account));
            if(user.Login != null) attributeSet.Add(new LdapAttribute("userPrincipalName", user.Login));
            if(user.Surname != null) attributeSet.Add(new LdapAttribute("sn", user.Surname));
            if(user.GivenName != null) attributeSet.Add(new LdapAttribute("givenName", user.GivenName));

            attributeSet.Add(new LdapAttribute("displayName", user.Name));
            if(user.Description != null) attributeSet.Add(new LdapAttribute("description", user.Description));

            if(user.Mail != null) attributeSet.Add(new LdapAttribute("mail", user.Mail));
            if(user.Mobile != null) attributeSet.Add(new LdapAttribute("mobile", user.Mobile)); 
                 
               
            if (user.Password == null )
            {
                if(user.IsDisabled == null) user.IsDisabled = true;
            }
            else
            {
                if (user.IsDisabled == null) user.IsDisabled = false;
                var ldapCfg = new LdapConfig();
                if (ldapCfg.ssl == false)
                {
                    throw new domain.Exceptions.SSLRequiredException();
                }

                string quotePwd = String.Format(@"""{0}""", user.Password);
                byte[] encodedBytes = Encoding.Unicode.GetBytes(quotePwd);
                attributeSet.Add(new LdapAttribute("unicodePwd", encodedBytes));


            }

            attributeSet.Add(new LdapAttribute("userAccountControl", user.accountControl.ToString()));

            //attributeSet.Add(new LdapAttribute("givenname", "James"));
            //attributeSet.Add(new LdapAttribute("sn", "Smith"));
            //attributeSet.Add(new LdapAttribute("mail", "JSmith@Acme.com"));

            return attributeSet;
        }

        private User ConvertfromLdap(LdapEntry entry)
        {
            var user = new User();

            user.Name = entry.GetAttribute("name").StringValue;
            
            user.Account = entry.GetAttribute("sAMAccountName").StringValue;
            
            if(entry.GetAttribute("userPrincipalName") != null) user.Login = entry.GetAttribute("userPrincipalName").StringValue;

            if(entry.GetAttribute("description") != null) user.Description = entry.GetAttribute("description").StringValue;

            var sid = ConvertByteToStringSid((byte[])(Array)entry.GetAttribute("objectSid").ByteValue);

            user.ID = sid;

            user.DN = entry.GetAttribute("distinguishedName").StringValue;

            if(entry.GetAttribute("givenName") != null) user.GivenName = entry.GetAttribute("givenName").StringValue;
            if(entry.GetAttribute("sn") != null) user.Surname = entry.GetAttribute("sn").StringValue;
            if(entry.GetAttribute("mail") != null) user.Mail = entry.GetAttribute("mail").StringValue;
            if(entry.GetAttribute("mobile") != null) user.Mobile = entry.GetAttribute("mobile").StringValue;
            
            var attrMo = entry.GetAttribute("memberOf");

            if ( attrMo != null)
            {
                var mofs = attrMo.StringValues;

                while (mofs.MoveNext())
                {
                    var group = new Group();
                    if (mofs != null && mofs.Current != null)
                        group.DN = mofs.Current;
                    user.MemberOf.Add(group);
                }
            }


            return user;
        }

        /// <summary>
        /// Deletes the user.
        /// </summary>
        /// <returns>0 for success -1 for error.</returns>
        /// <param name="user">User.</param>
        public int DeleteUser(User user)
        {
        

            var qMgmt = LdapQueryManager.Instance;

            try
            {
                qMgmt.DeleteEntry(user.DN);
                return 0;

            }
            catch (Exception ex)
            {
                logger.Error("Error deleting user");
                logger.Log(LogLevel.Error, ex);
                return -1;
            }

        }
    }
}
