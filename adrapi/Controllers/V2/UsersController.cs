using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using adrapi.Ldap;
using adrapi.Web;
using adrapi.domain;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using adrapi.Models;
using System.Linq;

namespace adrapi.Controllers.V2
{
    [Produces("application/json")]
    [Authorize(Policy = "Reading")]
    [ApiVersion("2.0")]
    [Route("api/users")]
    [Route("api/{domain}/users")]
    [ApiController]
    public class UsersController : BaseController
    {


        public UsersController(ILogger<Controllers.UsersController> logger, IConfiguration iConfig)
        {

            base.logger = logger;

            configuration = iConfig;
        }

        #region GET
        // GET api/users
        [HttpGet]
        public async Task<ActionResult<UserListResponse>> Get([FromQuery]int _start = -1, [FromQuery]int _end = -1, [FromQuery]string _cookie = "", [FromQuery] string _attribute = "", [FromQuery] string _filter = "", [FromRoute] string domain = null)
        {

            this.ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            logger.LogInformation(ListItems, "{0} listing all users", requesterID);

            if (IsEntraDomain(domain))
            {
                return await RunWithProviderAsync(domain, async provider =>
                {
                    var users = string.IsNullOrWhiteSpace(_filter)
                        ? await provider.GetUsersAsync()
                        : await provider.SearchUsersAsync(_filter);
                    return Ok(BuildUserList(users));
                });
            }

            var uManager = UserManager.Instance;

            /*if (_attribute == "")
            {
                if (_filter == "")
                {
                    return uManager.GetList("", "", _cookie);
                }

                return uManager.GetList("", _filter, _cookie);
  
            }

            if (_filter == "")
            {
                return uManager.GetList(_attribute, "", _cookie);
            }*/

            // Default mode: LDAP paged query using cookie.
            if (_start == -1 && _end == -1)
            {
                var response = await uManager.GetListAsync(_attribute, _filter, _cookie, ldapConfig);

                return response;
            }

            // Range mode requires both values and valid bounds.
            if (_start < 0 || _end < 0)
            {
                return Conflict();
            }

            if (_end < _start)
            {
                return Conflict();
            }

            // VLV is 1-based internally; accept _start=0 from clients as first item.
            var normalizedStart = _start == 0 ? 1 : _start;
            return await uManager.GetListAsync(normalizedStart, _end, _attribute, _filter, ldapConfig);

        }

        
        // GET api/users 
        [HttpGet]
        public async Task<ActionResult<UserListResponse>> Get([RequiredFromQuery]bool _full, [FromQuery]int _start, [FromQuery]int _end, [FromRoute] string domain = null)
        {

            this.ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            logger.LogInformation(ListItems, "{0} getting all users objects", requesterID);

            if (IsEntraDomain(domain))
            {
                return await RunWithProviderAsync(domain, async provider =>
                    Ok(BuildUserList(await provider.GetUsersAsync())));
            }

            if (_start == 0 && _end != 0)
            {
                return Conflict();
            }

            var uManager = UserManager.Instance;
            UserListResponse response;

            if (_start == 0 && _end == 0) response = await uManager.GetUsersAsync(ldapConfig);
            else response = await uManager.GetUsersAsync(ldapConfig);

            return response;

        }

        // GET api/users/:user
        [HttpGet("{user}")]
        public async Task<ActionResult<domain.User>> Get(string user, [FromQuery]string _attribute = "", [FromRoute] string domain = null)
        {
            this.ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            if (IsEntraDomain(domain))
            {
                return await RunWithProviderAsync(domain, async provider =>
                {
                    var found = await provider.GetUserAsync(user);
                    return found == null ? (ActionResult)NotFound() : Ok(found);
                });
            }

            var uManager = UserManager.Instance;

            User usr;

            usr = _attribute != "" ? await uManager.GetUserAsync(user, _attribute, ldapConfig) : await uManager.GetUserAsync(user, "", ldapConfig);

            if (usr == null)
            {
                return NotFound();
            }
            
            logger.LogDebug(GetItem, "User locator={user} found", user);

            return usr;
        }


        //[ProducesResponseType(200, Type = typeof(Product))]
        //[ProducesResponseType(404)]

        // GET api/users/:user/exists
        [HttpGet("{user}/exists")]
        public async Task<IActionResult> GetExists(string user, [FromQuery]string _attribute = "", [FromRoute] string domain = null)
        {
            this.ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            if (IsEntraDomain(domain))
            {
                return await RunWithProviderAsync(domain, async provider =>
                    await provider.UserExistsAsync(user) ? (ActionResult)Ok() : NotFound());
            }

            var uManager = UserManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "User DN={user} found with attribute={_attribute}",user,_attribute);
                if (_attribute != "")
                {
                    var resp = await uManager.GetUserAsync(user, _attribute, ldapConfig);
                    if (resp == null) return NotFound();

                }
                else
                {
                    var resp = await uManager.GetUserAsync(user, "", ldapConfig);
                    if (resp == null) return NotFound();
                }

                return Ok();

            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                logger.LogDebug(ItemExists, "User DN={user} not found.", user);
                return NotFound();
            }

        }

        // GET api/users/:user/attributes
        [HttpGet("{user}/attributes")]
        public async Task<ActionResult<UserAttributeInspectionResponse>> GetAttributes(string user, [FromQuery] string _lookupAttribute = "sAMAccountName", [FromRoute] string domain = null)
        {
            this.ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            if (string.IsNullOrWhiteSpace(user))
            {
                return BadRequest();
            }

            var uManager = UserManager.Instance;
            var inspection = await uManager.InspectUserAttributesAsync(user, _lookupAttribute, ldapConfig);
            if (inspection == null)
            {
                return NotFound();
            }

            return inspection;
        }

        // GET api/users/:user/member-of/:group
        [HttpGet("{user}/member-of/{group}")]
        public async Task<IActionResult> IsMemberOf(string user, string group, [FromRoute] string domain = null)
        {
            this.ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(group))
            {
                return BadRequest();
            }

            var uManager = UserManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "Checking membership for user={user} group={group}", user, group);
                var adUser = await uManager.GetUserAsync(user, "", ldapConfig);
                if (adUser == null)
                {
                    return NotFound();
                }

                if (IsMembershipMatch(adUser, group))
                    return Ok();

                // Rerturns 460 code telling that the user exists but it's not a member 
                return StatusCode(250);


            }
            catch (Exception ex)
            {
                logger.LogDebug(ItemExists, "User DN={dn} not found. err:" + ex.Message);
                return NotFound();
            }

        }

        [HttpGet("{user}/groups")]
        public async Task<ActionResult<UserGroupsResponse>> GetGroups(string user, [FromRoute] string domain = null)
        {
            this.ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            if (string.IsNullOrWhiteSpace(user))
            {
                return BadRequest();
            }

            var uManager = UserManager.Instance;
            var adUser = await uManager.GetUserAsync(user, "", ldapConfig);
            if (adUser == null)
            {
                return NotFound();
            }

            var groupDns = adUser.MemberOf
                .Select(g => g?.DN)
                .Where(dn => !string.IsNullOrWhiteSpace(dn))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var groupCns = groupDns
                .Select(ExtractCnFromDn)
                .Where(cn => !string.IsNullOrWhiteSpace(cn))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new UserGroupsResponse
            {
                LookupValue = user,
                DistinguishedName = adUser.DN,
                MemberOfDns = groupDns,
                MemberOfCns = groupCns
            };
        }
        #endregion

        #region Authentication

        // POST api/users/:user/authenticate
        // Verifies a user's password against AD. Requires a valid api-key (Reading policy)
        // AND is rate-limited per (ip, keyID) and per ip to throttle brute force.
        [HttpPost("{userId}/authenticate")]
        [Authorize(Policy = "Reading")]
        [EnableRateLimiting("AuthEndpoint")]
        public async Task<ActionResult> Authenticate(string userId, [FromBody] AuthenticationRequest req, [FromQuery] Boolean _useAccount = false, [FromRoute] string domain = null)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Password))
            {
                return BadRequest();
            }

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            var uManager = UserManager.Instance;

            User aduser;

            if (_useAccount)
            {
                aduser = await uManager.GetUserAsync(userId, "samaccountname", ldapConfig);
            }
            else
            {
                aduser = await uManager.GetUserAsync(userId, "", ldapConfig);
            }


            if (aduser == null)
            {
                logger.LogDebug(PutItem, "User ID={userID} not found", userId);
                return NotFound();
            }
            else
            {
                string login;

                if (req.Login == null) login = aduser.Account;
                else login = req.Login;

                var success = await uManager.ValidateAuthenticationAsync(login, req.Password, ldapConfig);

                if (success) return Ok();
                return StatusCode(401);
            }

        }

        // POST api/users/authenticate
        // Direct credentials check (no userId lookup). Same auth + rate-limit posture.
        [HttpPost("authenticate")]
        [Authorize(Policy = "Reading")]
        [EnableRateLimiting("AuthEndpoint")]
        public async Task<ActionResult> AuthenticateDirect([FromBody] AuthenticationRequest req, [FromRoute] string domain = null)
        {

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            var uManager = UserManager.Instance;

            string login;

            if (req == null || string.IsNullOrWhiteSpace(req.Login) || string.IsNullOrWhiteSpace(req.Password))
            {
                logger.LogDebug(AuthenticationItem, "Invalid Authentication request without login");
                return BadRequest();
            }
            else login = req.Login;

            var success = await uManager.ValidateAuthenticationAsync(login, req.Password, ldapConfig);

            if (success) return Ok();
            return StatusCode(401);


        }
        #endregion

        #region PUT
        // PUT api/users/:user
        /// <summary>
        /// Creates the specified user.
        /// </summary>
        /// <returns>The put.</returns>
        /// <param name="user">User.</param>
        [Authorize(Policy = "Writting")]
        [HttpPut("{DN}")]
        public async Task<ActionResult> Put(string DN, [FromBody] User user, [FromRoute] string domain = null)
        {
            ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            logger.LogDebug(PutItem, "Tring to create user:{0}", DN);

            if (IsEntraDomain(domain))
            {
                if (!ModelState.IsValid) return BadRequest();
                return await RunWithProviderAsync(domain, async provider =>
                {
                    var existing = await provider.GetUserAsync(DN);
                    if (existing == null)
                    {
                        if (string.IsNullOrWhiteSpace(user.Login)) user.Login = DN; // path segment is the UPN
                        LogAudit("entra.user.create.request", DN, $"account={user.Account}");
                        var created = await provider.CreateUserAsync(user);
                        if (created) LogAudit("entra.user.create.success", user.ID ?? DN, $"account={user.Account}");
                        return created ? (ActionResult)Ok() : StatusCode(500);
                    }

                    if (string.IsNullOrWhiteSpace(user.ID)) user.ID = existing.ID;
                    LogAudit("entra.user.update.request", user.ID ?? DN, $"account={user.Account}");
                    var updated = await provider.UpdateUserAsync(user);
                    if (updated) LogAudit("entra.user.update.success", user.ID ?? DN, $"account={user.Account}");
                    return updated ? (ActionResult)Ok() : StatusCode(500);
                });
            }

            if (ModelState.IsValid)
            {
                if (user.DN != null && user.DN != DN)
                {
                    logger.LogError(PutItem, "User DN different of the URL DN in put request user.DN={0} DN={1}", user.DN, DN);
                    return Conflict();
                }


                //Regex regex = new Regex(@"cn=([^,]+?),", RegexOptions.IgnoreCase);
                Regex regex = new Regex(@"\Acn=(?<login>[^,]+?),", RegexOptions.IgnoreCase);

                Match match = regex.Match(DN);

                if (!match.Success)
                {
                    logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                    return Conflict();
                }

                var uLogin = match.Groups["login"];

                var uManager = UserManager.Instance;

                var aduser = await uManager.GetUserAsync(DN, "", ldapConfig);


                if (aduser == null)
                {
                    // New User
                    logger.LogInformation(InsertItem, "Creating user DN={DN}", DN);

                    user.DN = DN;

                    var result = await uManager.CreateUserAsync(user, ldapConfig);
                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }
                else
                {
                    // Update
                    logger.LogInformation(UpdateItem, "Updating user DN={DN}", DN);

                    user.DN = DN;

                    var result = await uManager.SaveUserAsync(user, ldapConfig);
                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }



            }
            else
            {
                return BadRequest();
            }
            
        }

        #endregion

        #region DELETE

        /// <summary>
        /// Delete the specified DN.
        /// </summary>
        /// <response code="200">Deleted Ok</response>
        /// <response code="404">User not found</response>
        /// <response code="500">Internal Server error</response>
        [Authorize(Policy = "Writting")]
        [HttpDelete("{userID}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> Delete(string userID, [FromQuery] string _attribute = "", [FromRoute] string domain = null)
        {
            ProcessRequest();

            if (!TryResolveDomain(domain, out var ldapConfig, out var domainError)) return domainError;

            logger.LogDebug(PutItem, "Tring to delete user:{0}", userID);

            if (IsEntraDomain(domain))
            {
                return await RunWithProviderAsync(domain, async provider =>
                {
                    var existing = await provider.GetUserAsync(userID);
                    if (existing == null) return (ActionResult)NotFound();
                    LogAudit("entra.user.delete.request", existing.ID ?? userID, "delete");
                    var deleted = await provider.DeleteUserAsync(existing);
                    if (deleted) LogAudit("entra.user.delete.success", existing.ID ?? userID, "delete");
                    return deleted ? (ActionResult)Ok() : StatusCode(500);
                });
            }

            User duser = null;
            var uManager = UserManager.Instance;

            if (_attribute != "")
            {
                duser = await uManager.GetUserAsync(userID, _attribute, ldapConfig);
            }
            else
            {
                Regex regex = new Regex(@"\Acn=(?<login>[^,]+?),", RegexOptions.IgnoreCase);

                Match match = regex.Match(userID);

                if (!match.Success)
                {
                    logger.LogError(PutItem, "DN is not correcly formated  DN={0}", userID);
                    return Conflict();
                }


                //var uLogin = match.Groups["login"];

                duser = await uManager.GetUserAsync(userID, "", ldapConfig);
            }



            if (duser == null)
            {
                // No User
                logger.LogError(DeleteItem, "Tring to delete unexistent user DN={DN}", userID);

                return NotFound();

            }
            else
            {
                // Delete
                logger.LogInformation(DeleteItem, "Deleting user DN={DN}", userID);

                var result = await uManager.DeleteUser(duser, ldapConfig);
                if (result == 0) return Ok();
                else return this.StatusCode(500);

            }


        }

        #endregion

        // Builds a v2 UserListResponse from provider results (backend-agnostic).
        private static UserListResponse BuildUserList(List<User> users)
        {
            users ??= new List<User>();
            return new UserListResponse
            {
                Users = users,
                UserNames = users
                    .Select(u => u.Login ?? u.Account ?? u.ID)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList(),
                SearchType = "User",
                SearchMethod = "Graph",
            };
        }

        private static bool IsMembershipMatch(User user, string group)
        {
            if (user == null || string.IsNullOrWhiteSpace(group))
            {
                return false;
            }

            var normalizedGroup = group.Trim();
            var compareAsDn = normalizedGroup.Contains("=");

            return user.MemberOf.Any(member =>
            {
                if (string.IsNullOrWhiteSpace(member?.DN))
                {
                    return false;
                }

                if (string.Equals(member.DN, normalizedGroup, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (compareAsDn)
                {
                    return false;
                }

                var memberCn = !string.IsNullOrWhiteSpace(member.Name) ? member.Name : ExtractCnFromDn(member.DN);
                return string.Equals(memberCn, normalizedGroup, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string ExtractCnFromDn(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn))
            {
                return null;
            }

            var firstPart = dn.Split(',').FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstPart) || !firstPart.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return firstPart.Substring(3);
        }
    }

}
