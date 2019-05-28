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

namespace adrapi.Controllers
{
    //[Produces("application/json")]
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0")]
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
        public ActionResult<IEnumerable<String>> Get([FromQuery]int _start, [FromQuery]int _end, [FromQuery] string _attribute = "")
        {

            this.ProcessRequest();

            logger.LogInformation(ListItems, "{0} listing all users", requesterID);


            var uManager = UserManager.Instance;

            if (_start == 0 && _end != 0)
            {
                return Conflict();
            }
            
            if (_attribute == "")
            {
                if (_start == 0 && _end == 0) return uManager.GetList();
                else return uManager.GetList(_start, _end);
            }
            else
            {
                if (_start == 0 && _end == 0) return uManager.GetList(_attribute);
                else return uManager.GetList(_start, _end, _attribute);
            }

            //return users;

        }


        // GET api/users 
        [HttpGet]
        public ActionResult<IEnumerable<domain.User>> Get([RequiredFromQuery]bool _full, [FromQuery]int _start, [FromQuery]int _end)
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

                if (_start == 0 && _end == 0) users = uManager.GetUsers();
                else users = uManager.GetUsers(_start, _end);

                return users;
            }
            else
            {
                return new List<domain.User>();
            }
        }


        // GET api/users/:user
        [HttpGet("{user}")]
        public ActionResult<domain.User> Get(string user, [FromQuery]string _attribute = "")
        {
            this.ProcessRequest();
            var uManager = UserManager.Instance;

            User usr;
            
            usr = _attribute != "" ? uManager.GetUser(user, _attribute) : uManager.GetUser(user);

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
        public IActionResult GetExists(string user, [FromQuery]string _attribute = "")
        {
            this.ProcessRequest();

            var uManager = UserManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "User DN={user} found with attribute={_attribute}",user,_attribute);
                if (_attribute != "")
                {
                    var resp = uManager.GetUser(user, _attribute);
                    if (resp == null) return NotFound();
                    
                }
                else
                {
                    var resp = uManager.GetUser(user);
                    if (resp == null) return NotFound();
                }

                return Ok();

            }
            catch (Exception ex)
            {
                logger.LogDebug(ItemExists, "User DN={dn} not found.");
                return NotFound();
            }

        }

        // GET api/users/:user/member-of/:group
        [HttpGet("{DN}/member-of/{group}")]
        public IActionResult IsMemberOf(string DN, string group)
        {
            this.ProcessRequest();

            var uManager = UserManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "User DN={dn} found");
                var user = uManager.GetUser(DN);


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
                logger.LogDebug(ItemExists, "User DN={dn} not found.");
                return NotFound();
            }

        }
        #endregion

        #region Authentication

        // GET api/users/:user/authenticate
        [HttpPost("{userId}/authenticate")]
        public ActionResult Authenticate(string userId, [FromBody] AuthenticationRequest req, [FromQuery] Boolean _useAccount = false)
        {

            var uManager = UserManager.Instance;

            User aduser;
            
            if (_useAccount)
            {
                aduser = uManager.GetUser(userId, "samaccountname");
            }
            else
            {
                aduser = uManager.GetUser(userId);  
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

                var success = uManager.ValidateAuthentication(login, req.Password);

                if (success) return Ok();
                return StatusCode(401);
            }

        }

        // GET api/users/authenticate
        [HttpPost("authenticate")]
        public ActionResult AuthenticateDirect([FromBody] AuthenticationRequest req)
        {

            var uManager = UserManager.Instance;

            string login;

            if (req.Login == null)
            {
                logger.LogDebug(AuthenticationItem, "Invalid Authentication request without login");
                return BadRequest();
            }
            else login = req.Login;

            var success = uManager.ValidateAuthentication(login, req.Password);

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
        public ActionResult Put(string DN, [FromBody] User user)
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

                var aduser = uManager.GetUser(DN);


                if (aduser == null)
                {
                    // New User
                    logger.LogInformation(InsertItem, "Creating user DN={DN}", DN);

                    user.DN = DN;

                    var result = uManager.CreateUser(user);
                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }
                else
                {
                    // Update 
                    logger.LogInformation(UpdateItem, "Updating user DN={DN}", DN);

                    user.DN = DN;

                    var result = uManager.SaveUser(user);
                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }



            }
            else
            {
                return BadRequest();
            }

            return Conflict();
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
        public ActionResult Delete(string userID, [FromQuery] string _attribute = "")
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to delete user:{0}", userID);


            User duser = null;
            var uManager = UserManager.Instance;
            
            if (_attribute != "")
            {
                duser = uManager.GetUser(userID, _attribute);
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

                duser = uManager.GetUser(userID);
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

                var result = uManager.DeleteUser(duser);
                if (result == 0) return Ok();
                else return this.StatusCode(500);

            }


        }

        #endregion
    }

}
