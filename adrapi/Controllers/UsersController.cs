using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace adrapi.Controllers
{
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController: BaseController
    {

        private readonly ILogger _logger;


        public UsersController(ILogger<ValuesController> logger)
        {

            //string api_key = this.HttpContext.Request.Headers["api-key"];
            //requesterID = api_key.Split(':')[0];

            _logger = logger;
        }

        // GET api/users
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {

            this.ProcessRequest();

            _logger.LogInformation(GetItem, "{1} getting all users", requesterID);
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
