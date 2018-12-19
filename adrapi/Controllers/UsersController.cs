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
        public ActionResult<IEnumerable<domain.User>> Get()
        {

            this.ProcessRequest();

            _logger.LogInformation(GetItem, "{1} listing all users", requesterID);

            var uManager = UserManager.Instance;
            var users = uManager.GetList();

            //return JsonConvert.SerializeObject(users);
            return users;

        }

        // GET api/users/5
        [HttpGet("{id}")]
        public ActionResult<string> Get(string name)
        {
            return "value";
        }


    }
}
