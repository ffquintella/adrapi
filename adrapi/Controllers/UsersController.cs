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

        private readonly ILogger _logger;
        private IConfiguration configuration;

        public UsersController(ILogger<UsersController> logger, IConfiguration iConfig)
        {
      
            _logger = logger;

            configuration = iConfig;
        }

        // GET api/users
        [HttpGet]
        public ActionResult<IEnumerable<String>> Get()
        {

            this.ProcessRequest();

            _logger.LogInformation(GetItem, "{1} listing all users", requesterID);

            var uManager = UserManager.Instance;
            var users = uManager.GetList();

            return users;

        }


        // GET api/users 
        [HttpGet]
        public ActionResult<IEnumerable<domain.User>> Get([RequiredFromQuery]bool _full)
        {
            if (_full)
            {
                this.ProcessRequest();

                _logger.LogInformation(GetItem, "{1} getting all users objects", requesterID);

                var uManager = UserManager.Instance;
                var users = uManager.GetUsers();

                return users;
            }
            else
            {
                return new List<domain.User>();
            }
        }


        // GET api/users/5
        //[HttpGet("{DN}")]
        //public ActionResult<IEnumerable<domain.User>> Get(string DN)


    }
}
