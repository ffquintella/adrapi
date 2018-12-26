using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using adrapi.Ldap;
using Newtonsoft.Json;
using adrapi.Web;


namespace adrapi.Controllers
{
    //[Produces("application/json")]
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController: BaseController
    {
    
        private IConfiguration configuration;

        public UsersController(ILogger<UsersController> logger, IConfiguration iConfig)
        {
      
            _logger = logger;

            configuration = iConfig;
        }

        // GET api/users
        [HttpGet]
        public ActionResult<IEnumerable<String>> Get([FromQuery]int _start, [FromQuery]int _end)
        {

            this.ProcessRequest();

            _logger.LogInformation(GetItem, "{1} listing all users", requesterID);

            var uManager = UserManager.Instance;

            if(_start == 0 && _end != 0)
            {
                return Conflict();
            }

            if (_start == 0 && _end == 0) return uManager.GetList();
            else return uManager.GetList(_start, _end);

            //return users;

        }


        // GET api/users 
        [HttpGet]
        public ActionResult<IEnumerable<domain.User>> Get([RequiredFromQuery]bool _full, [FromQuery]int _start, [FromQuery]int _end)
        {
            if (_full)
            {
                this.ProcessRequest();

                _logger.LogInformation(GetItem, "{1} getting all users objects", requesterID);

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


        // GET api/users/CN=Administrador,CN=Users,DC=labesi,DC=fgv,DC=br
        [HttpGet("{DN}")]
        public ActionResult<domain.User> Get(string DN)
        {
            var uManager = UserManager.Instance;

            var user = uManager.GetUser(DN);

            return user;
        }


        //[ProducesResponseType(200, Type = typeof(Product))]
        //[ProducesResponseType(404)]

        // GET api/users/CN=Administrador,CN=Users,DC=labesi,DC=fgv,DC=br/exists
        [HttpGet("{DN}/exists")]
        public IActionResult GetExists(string DN)
        {
            var uManager = UserManager.Instance;

            try
            {
                _logger.LogDebug("User DN={dn} found");
                var user = uManager.GetUser(DN);

                return Ok();

            }
            catch(Exception ex)
            {
                _logger.LogDebug("User DN={dn} not found.");
                return NotFound();
            }

        }

        // GET api/users/CN=Administrador,CN=Users,DC=labesi,DC=fgv,DC=br/member-of/Administrators
        [HttpGet("{DN}/member-of/{group}")]
        public IActionResult IsMemberOf(string DN, string group)
        {
            var uManager = UserManager.Instance;

            try
            {
                _logger.LogDebug("User DN={dn} found");
                var user = uManager.GetUser(DN);


                foreach(domain.Group grp in user.MemberOf)
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
                _logger.LogDebug("User DN={dn} not found.");
                return NotFound();
            }

        }


    }
}
