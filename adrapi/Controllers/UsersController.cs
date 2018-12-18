using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using adrapi.Ldap;

namespace adrapi.Controllers
{
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController: BaseController
    {

        private readonly ILogger _logger;
        private IConfiguration configuration;

        public UsersController(ILogger<ValuesController> logger, IConfiguration iConfig)
        {
      
            _logger = logger;

            configuration = iConfig;
        }

        // GET api/users
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {

            this.ProcessRequest();

            _logger.LogInformation(GetItem, "{1} listing all users", requesterID);

            var ldapConf = new Ldap.LdapConfig(configuration);

            var lcm = LdapConnectionManager.Instance;

            var con = lcm.GetConnection(ldapConf);

            return new string[] { "value1", "value2" };
        }

        // GET api/users/5
        [HttpGet("{id}")]
        public ActionResult<string> Get(int id)
        {
            return "value";
        }


    }
}
