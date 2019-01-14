using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.AspNetCore.Authorization;

namespace adrapi.Controllers
{
    //[Authorize]
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [ApiController]
    public class InfosController : ControllerBase
    {

        private readonly ILogger _logger;

        public InfosController(ILogger<InfosController> logger)
        {
            _logger = logger;
        }

        // GET api/values
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            //_logger.LogInformation(GetItem, "Getting item test");
            return new string[] { "version", "about" };
        }

        // GET api/infos/version
        [HttpGet("{info}")]
        public ActionResult<string> Get(string info)
        {
            string resp = "";
            switch (info)
            {
                case "version":
                    resp = "0.2.0";
                    return Ok(resp);
                case "about":
                    resp = "This is the Active Directory Read API or just ADRAPI.";
                    return Ok(resp);
            }

            return NotFound();
        }

  
    }
}
