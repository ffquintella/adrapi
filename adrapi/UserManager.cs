using System;
using System.Collections.Generic;
using adrapi.domain;
using adrapi.Ldap;
using System.Linq;
using Novell.Directory.Ldap;
using System.Text;
using System.Threading.Tasks;
using adrapi.Ldap.Security;
using adrapi.Models;
using NLog;
using adrapi.Tools;

namespace adrapi
{
    public class UserManager : ObjectManager
    {
        private const int MemberOfRangeWindow = 1500;

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
        public async Task<UserListResponse> GetListAsync(string attribute = "", string filter = "", string cookie = "")
        {
            
            var response = new UserListResponse();
            
            var userNames = new List<string>();
            var users = new List<User>();
            var attributes = ParseRequestedAttributes(attribute);

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;

            string formatedFilter = "";

            if (filter != "")
            {
                var filterAttribute = attributes.Count > 0 ? attributes[0] : attribute;
                if (filterAttribute != "")
                    formatedFilter = filterAttribute + "=" + filter;
                else
                    formatedFilter = "cn=" + filter;
            }

            List<LdapEntry> resps;
            

            var presp = await sMgmt.ExecutePagedSearchAsync("", LdapSearchType.User, formatedFilter, cookie);

            response.Cookie = presp.Cookie;
            resps = presp.Entries;
            

            foreach(var entry in resps)
            {
                if (attributes.Count == 0)
                {
                    //users.Add(entry.GetAttribute("distinguishedName").StringValue);
                    userNames.Add(entry.GetStringValueOrDefault("samaccountname"));
                    var user = new User();
                    user.Account = entry.GetStringValueOrDefault("samaccountname");
                    user.ID = entry.GetStringValueOrDefault("objectSid");
                    user.GivenName = entry.GetStringValueOrDefault("cn");


                    var memberOfDns = GetAttributeStringValues(entry, "memberOf");
                    foreach (var groupDn in memberOfDns)
                    {
                        user.MemberOf.Add(CreateGroupFromDn(groupDn));
                    }

                    users.Add(user);

                }
                else if (attributes.Count == 1)
                {
                    userNames.Add(entry.GetStringValueOrDefault(attributes[0]));
                }
                else
                {
                    var projected = ProjectUserFromAttributes(entry, attributes);
                    users.Add(projected);

                    var usernameKey = attributes.Contains("sAMAccountName") ? "sAMAccountName" : attributes[0];
                    userNames.Add(entry.GetStringValueOrDefault(usernameKey));
                }
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
        public async Task<UserListResponse> GetListAsync(int start, int end, string attribute = "" , string filter = "")
        {
            var response = new UserListResponse();
            
            var users = new List<string>();
            var projectedUsers = new List<User>();
            var attributes = ParseRequestedAttributes(attribute);

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;
            if (start < 1) start = 1;
            if (end < start) end = start;

            string formatedFilter = "";

            if (filter != "")
            {
                var filterAttribute = attributes.Count > 0 ? attributes[0] : attribute;
                if (filterAttribute != "")
                    formatedFilter = filterAttribute + "=" + filter;
                else
                    formatedFilter = "cn=" + filter;
            }
            
            string cookie = "";
            string previousCookie = null;
            int absoluteIndex = 0;

            while (absoluteIndex < end)
            {
                var presp = await sMgmt.ExecutePagedSearchAsync("", LdapSearchType.User, formatedFilter, cookie);
                var entries = presp?.Entries ?? new List<LdapEntry>();
                if (entries.Count == 0)
                {
                    response.Cookie = string.Empty;
                    break;
                }

                foreach (var entry in entries)
                {
                    absoluteIndex++;
                    if (absoluteIndex < start)
                    {
                        continue;
                    }

                    if (absoluteIndex > end)
                    {
                        break;
                    }

                    string u = "";
                    if (attributes.Count == 0)
                    {
                        u = entry.GetStringValueOrDefault("distinguishedName");
                    }
                    else if (attributes.Count == 1)
                    {
                        u = entry.GetStringValueOrDefault(attributes[0]);
                    }
                    else
                    {
                        var projected = ProjectUserFromAttributes(entry, attributes);
                        projectedUsers.Add(projected);

                        var usernameKey = attributes.Contains("sAMAccountName") ? "sAMAccountName" : attributes[0];
                        u = entry.GetStringValueOrDefault(usernameKey);
                    }
                    users.Add(u);
                    results++;
                }

                var nextCookie = presp?.Cookie ?? string.Empty;
                response.Cookie = nextCookie;
                if (string.IsNullOrWhiteSpace(nextCookie) || string.Equals(previousCookie, nextCookie, StringComparison.Ordinal))
                {
                    break;
                }

                previousCookie = nextCookie;
                cookie = nextCookie;
            }

            response.SearchType = "User";
            response.SearchMethod = LdapSearchMethod.Paged;
            response.UserNames = users;
            if (projectedUsers.Count > 0)
            {
                response.Users = projectedUsers;
            }
            logger.Debug("User search executed results:{result}", results);


            return response;
        }

        private static List<string> ParseRequestedAttributes(string attribute)
        {
            if (string.IsNullOrWhiteSpace(attribute))
            {
                return new List<string>();
            }

            return attribute
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => NormalizeAttributeName(a.Trim()))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeAttributeName(string attribute)
        {
            return attribute.ToLowerInvariant() switch
            {
                "samaccountname" => "sAMAccountName",
                "dn" => "distinguishedName",
                "distinguishedname" => "distinguishedName",
                "displayname" => "displayName",
                "givenname" => "givenName",
                "surname" => "sn",
                _ => attribute
            };
        }

        private static User ProjectUserFromAttributes(LdapEntry entry, List<string> attributes)
        {
            var user = new User();
            foreach (var attribute in attributes)
            {
                var value = entry.GetStringValueOrDefault(attribute);
                if (value == null)
                {
                    continue;
                }

                switch (attribute.ToLowerInvariant())
                {
                    case "samaccountname":
                        user.Account = value;
                        break;
                    case "mail":
                        user.Mail = value;
                        break;
                    case "givenname":
                        user.GivenName = value;
                        break;
                    case "sn":
                        user.Surname = value;
                        break;
                    case "displayname":
                    case "name":
                        user.Name = value;
                        break;
                    case "distinguishedname":
                        user.DN = value;
                        break;
                }
            }

            return user;
        }

        /// <summary>
        /// Gets the list of all users.
        /// </summary>
        /// <returns>The users.</returns>
        public async Task<UserListResponse> GetUsersAsync()
        {
            
            var response = new UserListResponse();

            var users = new List<User>();

            var sMgmt = LdapQueryManager.Instance;

            var resps = await sMgmt.ExecuteSearchAsync("", LdapSearchType.User);
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


        public async Task<UserListResponse> GetUsers(int start, int end)
        {
            var response = new UserListResponse();
            var users = new List<User>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;


            var resps = await sMgmt.ExecuteLimitedSearchAsync("", LdapSearchType.User, start, end);

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
        public async Task<User> GetUserAsync (string userID, string attribute = "")
        {
            var sMgmt = LdapQueryManager.Instance;

            try
            {
                if (string.IsNullOrWhiteSpace(userID))
                {
                    return null;
                }

                LdapEntry entry = null;
                var normalizedAttribute = string.IsNullOrWhiteSpace(attribute) ? "" : NormalizeAttributeName(attribute.Trim());
                var lookupValue = userID.Trim();

                if (normalizedAttribute != "")
                {
                    if (string.Equals(normalizedAttribute, "distinguishedName", StringComparison.OrdinalIgnoreCase) || LooksLikeDistinguishedName(lookupValue))
                    {
                        entry = await TryGetUserEntryByDnAsync(lookupValue);
                    }

                    if (entry == null)
                    {
                        entry = await FindUserEntryByAttributeAsync(lookupValue, normalizedAttribute);
                    }
                }
                else
                {
                    entry = await ResolveUserEntryAsync(lookupValue);
                }

                if (entry == null)
                {
                    logger.Debug("User not found {0}", userID);
                    return null;
                }

                var user = ConvertfromLdap(entry);
                var userDn = !string.IsNullOrWhiteSpace(user.DN) ? user.DN : entry.Dn;
                var fullMemberOfDns = await GetCompleteMemberOfDnsAsync(userDn, entry);
                user.MemberOf = fullMemberOfDns
                    .Select(CreateGroupFromDn)
                    .ToList();
                return user;
            }catch(LdapException ex)
            {
                logger.Debug("User not found {0} Ex: {1}", userID, ex.Message);
                return null;
            }

        }

        public async Task<UserAttributeInspectionResponse> InspectUserAttributesAsync(string lookupValue, string lookupAttribute = "sAMAccountName")
        {
            if (string.IsNullOrWhiteSpace(lookupValue))
            {
                return null;
            }

            var sMgmt = LdapQueryManager.Instance;
            var lookupAttr = string.IsNullOrWhiteSpace(lookupAttribute) ? "sAMAccountName" : lookupAttribute;

            LdapEntry entry = null;
            if (lookupValue.Contains("="))
            {
                try
                {
                    entry = await sMgmt.GetRegister(lookupValue);
                }
                catch
                {
                    entry = null;
                }
            }

            if (entry == null)
            {
                var filter = $"(&(objectClass=user)(objectCategory=person)({LdapInjectionControll.EscapeForSearchFilter(lookupAttr)}={LdapInjectionControll.EscapeForSearchFilter(lookupValue)}))";
                var search = await sMgmt.ExecutePagedSearchAsync("", filter);
                entry = search.Entries.FirstOrDefault();
            }

            if (entry == null)
            {
                return null;
            }

            var response = new UserAttributeInspectionResponse
            {
                LookupValue = lookupValue,
                LookupAttribute = lookupAttr,
                DistinguishedName = entry.GetStringValueOrDefault("distinguishedName")
            };

            foreach (LdapAttribute attr in entry.GetAttributeSet())
            {
                var values = new List<string>();
                if (attr.StringValueArray != null && attr.StringValueArray.Length > 0)
                {
                    values.AddRange(attr.StringValueArray.Where(v => !string.IsNullOrWhiteSpace(v)));
                }
                else if (attr.ByteValue != null)
                {
                    values.Add("base64:" + Convert.ToBase64String(attr.ByteValue));
                }

                response.Attributes[attr.Name] = values;
            }

            var memberOf = await GetCompleteMemberOfDnsAsync(response.DistinguishedName, entry);
            if (memberOf != null)
            {
                response.MemberOfDns = memberOf;
                response.MemberOfCns = memberOf
                    .Select(dn => dn?.Split(',').FirstOrDefault())
                    .Where(part => !string.IsNullOrWhiteSpace(part) && part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    .Select(part => part.Substring(3))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return response;
        }

        /// <summary>
        /// Creates the user on LDAP Directory.
        /// </summary>
        /// <returns> -1 Error </returns>
        /// <returns> 0 OK </returns>
        /// <param name="user">User.</param>
        public async Task<int> CreateUserAsync(User user)
        {

            //Creates the List attributes of the entry and add them to attributeset

            LdapAttributeSet attributeSet = GetAttributeSet(user);

            // DN of the entry to be added
            string dn = user.DN;

            LdapEntry newEntry = new LdapEntry(dn, attributeSet);


            var qMgmt = LdapQueryManager.Instance;

            try
            {
                await qMgmt.AddEntryAsync(newEntry);
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
        public async Task<int> SaveUserAsync(User user)
        {

            var qMgmt = LdapQueryManager.Instance;

            var modList = new List<LdapModification>();

            var atributes = GetAttributeSet(user);

            //Get user from the Directory
            try
            {
                var duser = await GetUserAsync(user.DN);

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
                        await qMgmt.SaveEntry(user.DN, modList.ToArray());
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

        public async Task<bool> ValidateAuthenticationAsync(string login, string password)
        {
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            var bindLogin = login.Trim();

            // If caller provides a plain account name, resolve it first so bind is deterministic.
            if (!bindLogin.Contains("=") && !bindLogin.Contains("@") && !bindLogin.Contains("\\"))
            {
                var adUser = await GetUserAsync(bindLogin, "sAMAccountName");
                if (adUser != null)
                {
                    if (!string.IsNullOrWhiteSpace(adUser.DN))
                    {
                        bindLogin = adUser.DN;
                    }
                    else if (!string.IsNullOrWhiteSpace(adUser.Login))
                    {
                        bindLogin = adUser.Login;
                    }
                }
            }

            LdapConnectionManager lcm = LdapConnectionManager.Instance;
            return await lcm.ValidateAuthenticationAsync(bindLogin, password);

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

            user.Name = entry.GetStringValueOrDefault("name");
            
            user.Account = entry.GetStringValueOrDefault("sAMAccountName");
            
            if(entry.GetAttributeSet().ContainsKey("userPrincipalName")) user.Login = entry.GetStringValueOrDefault("userPrincipalName");

            if(entry.GetAttributeSet().ContainsKey("description")) user.Description = entry.GetStringValueOrDefault("description");

            var sid = ConvertByteToStringSid((byte[])(Array)entry.GetBytesValueOrDefault("objectSid"));

            user.ID = sid;

            user.DN = entry.GetStringValueOrDefault("distinguishedName");

            if(entry.GetAttributeSet().ContainsKey("givenName")) user.GivenName = entry.GetStringValueOrDefault("givenName");
            if(entry.GetAttributeSet().ContainsKey("sn")) user.Surname = entry.GetStringValueOrDefault("sn");
            if(entry.GetAttributeSet().ContainsKey("mail")) user.Mail = entry.GetStringValueOrDefault("mail");
            if(entry.GetAttributeSet().ContainsKey("mobile")) user.Mobile = entry.GetStringValueOrDefault("mobile");

            var memberships = GetAttributeStringValues(entry, "memberOf");
            foreach (var groupDn in memberships)
            {
                user.MemberOf.Add(CreateGroupFromDn(groupDn));
            }



            return user;
        }

        private static bool LooksLikeDistinguishedName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Contains("=");
        }

        private static List<string> GetAttributeStringValues(LdapEntry entry, string attributeName)
        {
            if (entry == null || string.IsNullOrWhiteSpace(attributeName))
            {
                return new List<string>();
            }

            var values = new List<string>();
            foreach (LdapAttribute attribute in entry.GetAttributeSet())
            {
                var key = attribute.Name;
                if (attribute == null || string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!string.Equals(key, attributeName, StringComparison.OrdinalIgnoreCase)
                    && !TryParseRangeAttributeName(key, attributeName, out _, out _, out _))
                {
                    continue;
                }

                if (attribute.StringValueArray != null && attribute.StringValueArray.Length > 0)
                {
                    values.AddRange(attribute.StringValueArray.Where(v => !string.IsNullOrWhiteSpace(v)));
                }
                else if (!string.IsNullOrWhiteSpace(attribute.StringValue))
                {
                    values.Add(attribute.StringValue);
                }
            }

            return values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Group CreateGroupFromDn(string groupDn)
        {
            var group = new Group
            {
                DN = groupDn
            };

            var cnPart = groupDn?.Split(',').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(cnPart) && cnPart.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                group.Name = cnPart.Substring(3);
            }

            return group;
        }

        private async Task<List<string>> GetCompleteMemberOfDnsAsync(string userDn, LdapEntry seedEntry = null)
        {
            var memberships = new List<string>();
            if (!string.IsNullOrWhiteSpace(userDn) && seedEntry != null)
            {
                memberships.AddRange(GetAttributeStringValues(seedEntry, "memberOf"));
            }

            if (string.IsNullOrWhiteSpace(userDn))
            {
                return memberships
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            LdapEntry currentEntry = seedEntry;
            var nextRangeStart = GetNextRangeStart(currentEntry, "memberOf");
            if (nextRangeStart < 0)
            {
                return memberships
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var sMgmt = LdapQueryManager.Instance;
            while (nextRangeStart >= 0)
            {
                var requestedRange = $"memberOf;range={nextRangeStart}-{nextRangeStart + MemberOfRangeWindow - 1}";
                try
                {
                    currentEntry = await sMgmt.GetRegister(userDn, new[] { requestedRange });
                }
                catch
                {
                    break;
                }

                if (currentEntry == null)
                {
                    break;
                }

                memberships.AddRange(GetAttributeStringValues(currentEntry, "memberOf"));

                if (HasCompletedRangedAttribute(currentEntry, "memberOf"))
                {
                    break;
                }

                var newNextRangeStart = GetNextRangeStart(currentEntry, "memberOf");
                if (newNextRangeStart <= nextRangeStart)
                {
                    break;
                }

                nextRangeStart = newNextRangeStart;
            }

            return memberships
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool HasCompletedRangedAttribute(LdapEntry entry, string attributeName)
        {
            if (entry == null)
            {
                return false;
            }

            foreach (LdapAttribute attribute in entry.GetAttributeSet())
            {
                if (TryParseRangeAttributeName(attribute.Name, attributeName, out _, out _, out var terminal) && terminal)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetNextRangeStart(LdapEntry entry, string attributeName)
        {
            if (entry == null)
            {
                return -1;
            }

            var highestEnd = -1;
            var hasRange = false;

            foreach (LdapAttribute attribute in entry.GetAttributeSet())
            {
                if (!TryParseRangeAttributeName(attribute.Name, attributeName, out _, out var end, out var terminal))
                {
                    continue;
                }

                hasRange = true;
                if (terminal)
                {
                    return -1;
                }

                if (end > highestEnd)
                {
                    highestEnd = end;
                }
            }

            return hasRange ? highestEnd + 1 : -1;
        }

        private static bool TryParseRangeAttributeName(string attributeKey, string baseAttributeName, out int start, out int end, out bool terminal)
        {
            start = -1;
            end = -1;
            terminal = false;

            if (string.IsNullOrWhiteSpace(attributeKey) || string.IsNullOrWhiteSpace(baseAttributeName))
            {
                return false;
            }

            var prefix = $"{baseAttributeName};range=";
            if (!attributeKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rangePart = attributeKey.Substring(prefix.Length);
            var separatorIndex = rangePart.IndexOf('-');
            if (separatorIndex <= 0 || separatorIndex >= rangePart.Length - 1)
            {
                return false;
            }

            var startPart = rangePart.Substring(0, separatorIndex);
            var endPart = rangePart.Substring(separatorIndex + 1);

            if (!int.TryParse(startPart, out start))
            {
                return false;
            }

            if (endPart == "*")
            {
                terminal = true;
                end = int.MaxValue;
                return true;
            }

            if (!int.TryParse(endPart, out end))
            {
                return false;
            }

            return true;
        }

        private async Task<LdapEntry> ResolveUserEntryAsync(string userID)
        {
            if (LooksLikeDistinguishedName(userID))
            {
                var byDn = await TryGetUserEntryByDnAsync(userID);
                if (byDn != null)
                {
                    return byDn;
                }
            }

            var bySamAccountName = await FindUserEntryByAttributeAsync(userID, "sAMAccountName");
            if (bySamAccountName != null)
            {
                return bySamAccountName;
            }

            return await FindUserEntryByAttributeAsync(userID, "userPrincipalName");
        }

        private async Task<LdapEntry> FindUserEntryByAttributeAsync(string value, string attribute)
        {
            var sMgmt = LdapQueryManager.Instance;
            var filter = $"(&(objectClass=user)(objectCategory=person)({LdapInjectionControll.EscapeForSearchFilter(attribute)}={LdapInjectionControll.EscapeForSearchFilter(value)}))";
            var results = await sMgmt.ExecutePagedSearchAsync("", filter);
            var entry = results.Entries.FirstOrDefault();
            if (entry == null)
            {
                return null;
            }

            return await TryGetUserEntryByDnAsync(entry.Dn) ?? entry;
        }

        private async Task<LdapEntry> TryGetUserEntryByDnAsync(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn))
            {
                return null;
            }

            var sMgmt = LdapQueryManager.Instance;
            try
            {
                return await sMgmt.GetRegister(dn, userAttrs);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes the user.
        /// </summary>
        /// <returns>0 for success -1 for error.</returns>
        /// <param name="user">User.</param>
        public async Task<int> DeleteUser(User user)
        {
        

            var qMgmt = LdapQueryManager.Instance;

            try
            {
                await qMgmt.DeleteEntry(user.DN);
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
