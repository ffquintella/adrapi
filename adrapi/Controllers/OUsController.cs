using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using adrapi.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.Extensions.Configuration;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace adrapi.Controllers
{

    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [ApiController]
    public class OUsController : BaseController
    {

        public OUsController(ILogger<GroupsController> logger, IConfiguration iConfig)
        {

            this._logger = logger;

            configuration = iConfig;
        }

        // GET: api/values
        [HttpGet]
        public ActionResult<IEnumerable<String>> Get([FromQuery]int _start, [FromQuery]int _end)
        {

            this.ProcessRequest();

            _logger.LogInformation(GetItem, "{1} listing all ous", requesterID);

            var oManager = OUManager.Instance;

            if (_start == 0 && _end != 0)
            {
                return Conflict();
            }

            //if (_start == 0 && _end == 0)
                return oManager.GetList();
            //else return gManager.GetList(_start, _end);


        }


    }
}
