using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using adrapi.Ldap;
using adrapi.Web;
using adrapi.domain;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using adrapi.Models;

namespace adrapi.Controllers
{
    //[Produces("application/json")]
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0",  Deprecated = true)]
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : BaseController
    {


        public UsersController(ILogger<UsersController> logger, IConfiguration iConfig)
        {

            base.logger = logger;

            configuration = iConfig;
        }

        #region GET
        // GET api/users
        [HttpGet]
        public async Task<ActionResult<UserListResponse>> Get([FromQuery]int _start, [FromQuery]int _end, [FromQuery] string _attribute = "", [FromQuery] string _filter = "")
        {

            this.ProcessRequest();

            logger.LogInformation(ListItems, "{0} listing all users", requesterID);


            var uManager = UserManager.Instance;

            if (_start < 0 || _end < 0)
            {
                return Conflict();
            }

            if (_end < _start)
            {
                return Conflict();
            }
            
            if (_attribute == "")
            {
                if (_filter == "")
                {
                    if (_start == 0 && _end == 0) return await uManager.GetListAsync();
                    var normalizedStart = _start == 0 ? 1 : _start;
                    return await uManager.GetListAsync(normalizedStart, _end);
                }

                if (_start == 0 && _end == 0) return await uManager.GetListAsync("", _filter);
                return await uManager.GetListAsync(_start == 0 ? 1 : _start, _end, "", _filter);
                
                
            }

            if (_filter == "")
            {
                if (_start == 0 && _end == 0) return await uManager.GetListAsync(_attribute);
                return await uManager.GetListAsync(_start == 0 ? 1 : _start, _end, _attribute);
            }
            
            if (_start == 0 && _end == 0) return await uManager.GetListAsync(_attribute, _filter);
            return await uManager.GetListAsync(_start == 0 ? 1 : _start, _end, _attribute, _filter);
            

        }

        
        // GET api/users 
        [HttpGet]
        public async Task<ActionResult<IEnumerable<domain.User>>> Get([RequiredFromQuery]bool _full, [FromQuery]int _start, [FromQuery]int _end)
        {
            if (_full)
            {
                this.ProcessRequest();

                logger.LogInformation(ListItems, "{0} getting all users objects", requesterID);

                if (_start == 0 && _end != 0)
                {
                    return Conflict();
                }

                var uManager = UserManager.Instance;
                List<domain.User> users;

                if (_start == 0 && _end == 0) users = (await uManager.GetUsersAsync()).Users;
                else users = (await uManager.GetUsers(_start, _end)).Users;

                return users;
            }
            else
            {
                return new List<domain.User>();
            }
        }

        // GET api/users/:user
        [HttpGet("{user}")]
        public async Task<ActionResult<domain.User>> Get(string user, [FromQuery]string _attribute = "")
        {
            this.ProcessRequest();
            var uManager = UserManager.Instance;

            User usr;

            usr = _attribute != "" ? await uManager.GetUserAsync(user, _attribute) : await uManager.GetUserAsync(user);

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
        public async Task<IActionResult> GetExists(string user, [FromQuery]string _attribute = "")
        {
            this.ProcessRequest();

            if (string.IsNullOrWhiteSpace(user))
            {
                return BadRequest();
            }

            var uManager = UserManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "User DN={user} found with attribute={_attribute}",user,_attribute);
                if (_attribute != "")
                {
                    var resp = await uManager.GetUserAsync(user, _attribute);
                    if (resp == null) return NotFound();
                    
                }
                else
                {
                    var resp = await uManager.GetUserAsync(user);
                    if (resp == null) return NotFound();
                }

                return Ok();

            }
            catch (Exception ex)
            {
                logger.LogError(ItemExists, "Error checking user locator={user} attribute={attribute}. err:{message}", user, _attribute, ex.Message);
                return this.StatusCode(500);
            }

        }

        // GET api/users/:user/member-of/:group
        [HttpGet("{DN}/member-of/{group}")]
        public async Task<IActionResult> IsMemberOf(string DN, string group)
        {
            this.ProcessRequest();

            if (string.IsNullOrWhiteSpace(DN) || string.IsNullOrWhiteSpace(group))
            {
                return BadRequest();
            }

            var uManager = UserManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "User DN={dn} found", DN);
                var user = await uManager.GetUserAsync(DN);
                if (user == null)
                {
                    return NotFound();
                }


                foreach (domain.Group grp in user.MemberOf)
                {
                    if (grp.DN == group)
                    {
                        return Ok();
                    }
                }

                // Rerturns 460 code telling that the user exists but it's not a member 
                return StatusCode(250);


            }
            catch (Exception ex)
            {
                logger.LogError(ItemExists, "Error checking membership for DN={dn} group={group}. err:{message}", DN, group, ex.Message);
                return this.StatusCode(500);
            }

        }
        #endregion

        #region Authentication

        // GET api/users/:user/authenticate
        [HttpPost("{userId}/authenticate")]
        public async Task<ActionResult> Authenticate(string userId, [FromBody] AuthenticationRequest req, [FromQuery] Boolean _useAccount = false)
        {
            if (req == null)
            {
                return BadRequest();
            }

            var uManager = UserManager.Instance;

            User aduser;
            
            if (_useAccount)
            {
                aduser = await uManager.GetUserAsync(userId, "samaccountname");
            }
            else
            {
                aduser = await uManager.GetUserAsync(userId);  
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

                var success = await uManager.ValidateAuthenticationAsync(login, req.Password);

                if (success) return Ok();
                return StatusCode(401);
            }

        }

        // GET api/users/authenticate
        [HttpPost("authenticate")]
        public async Task<ActionResult> AuthenticateDirect([FromBody] AuthenticationRequest req)
        {
            if (req == null)
            {
                return BadRequest();
            }

            var uManager = UserManager.Instance;

            string login;

            if (req.Login == null)
            {
                logger.LogDebug(AuthenticationItem, "Invalid Authentication request without login");
                return BadRequest();
            }
            else login = req.Login;

            var success = await uManager.ValidateAuthenticationAsync(login, req.Password);

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
        public async Task<ActionResult> Put(string DN, [FromBody] User user)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to create user:{0}", DN);

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

                var aduser = await uManager.GetUserAsync(DN);


                if (aduser == null)
                {
                    // New User
                    logger.LogInformation(InsertItem, "Creating user DN={DN}", DN);

                    user.DN = DN;

                    var result = await uManager.CreateUserAsync(user);
                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }
                else
                {
                    // Update 
                    logger.LogInformation(UpdateItem, "Updating user DN={DN}", DN);

                    user.DN = DN;

                    var result = await uManager.SaveUserAsync(user);
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
        public async Task<ActionResult> Delete(string userID, [FromQuery] string _attribute = "")
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to delete user:{0}", userID);


            User duser = null;
            var uManager = UserManager.Instance;
            
            if (_attribute != "")
            {
                duser = await uManager.GetUserAsync(userID, _attribute);
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

                duser = await uManager.GetUserAsync(userID);
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

                var result = await uManager.DeleteUser(duser);
                if (result == 0) return Ok();
                else return this.StatusCode(500);

            }


        }

        #endregion
    }

}
