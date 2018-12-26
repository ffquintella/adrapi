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

            this.logger = logger;

            configuration = iConfig;
        }

        // GET: api/ous
        [HttpGet]
        public ActionResult<IEnumerable<String>> Get()
        {

            this.ProcessRequest();

            logger.LogInformation(GetItem, "{1} listing all ous", requesterID);

            var oManager = OUManager.Instance;


            //if (_start == 0 && _end == 0)
                return oManager.GetList();
            //else return gManager.GetList(_start, _end);


        }

        // GET: api/ous/:ou
        [HttpGet("{DN}")]
        public ActionResult<domain.OU> Get(string DN)
        {
            this.ProcessRequest();

            var oManager = OUManager.Instance;
            try
            {
                var ou = oManager.GetOU(DN);
                logger.LogDebug(GetItem, "Getting OU={0}", ou.Name);
                return ou;
            }
            catch (Exception ex)
            {
                logger.LogError(GetItem, "Error getting ou ex:{0}", ex.Message);
                return null;
            }



        }

        // GET api/ous/:ou/exists
        [HttpGet("{DN}/exists")]
        public IActionResult GetExists(string DN)
        {
            this.ProcessRequest();

            var oManager = OUManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "OU DN={dn} found");
                var ou = oManager.GetOU(DN);

                return Ok();

            }
            catch (Exception ex)
            {
                logger.LogDebug(ItemExists, "OU DN={dn} not found.");
                return NotFound();
            }

        }
    }
}
