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
using adrapi.domain;
using System.Text.RegularExpressions;

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

        #region GET
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
                logger.LogDebug(ItemExists, "OU DN={dn} not found. err:" + ex.Message );
                return NotFound();
            }

        }
        #endregion

        #region PUT

        // PUT api/ous/:ou
        /// <summary>
        /// Creates the specified ou.
        /// </summary>
        /// <returns>The put.</returns>
        /// <param name="OU">ou.</param>
        [Authorize(Policy = "Writting")]
        [HttpPut("{DN}")]
        public ActionResult Put(string DN, [FromBody] OU ou)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to create OU:{0}", DN);

            if (ModelState.IsValid)
            {
                if (ou.DN != null && ou.DN != DN)
                {
                    logger.LogError(PutItem, "Ou DN different of the URL DN in put request ou.DN={0} DN={1}", ou.DN, DN);
                    return Conflict();
                }

                Regex regex = new Regex(@"\Aou=(?<oname>[^,]+?),", RegexOptions.IgnoreCase);

                Match match = regex.Match(DN);

                if (!match.Success)
                {
                    logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                    return Conflict();
                }

                var oName = match.Groups["oname"];

                var oManager = OUManager.Instance;

                var adou = oManager.GetOU(DN);


                if (adou == null)
                {
                    // New Group
                    logger.LogInformation(InsertItem, "Creating OU DN={DN}", DN);

                    ou.DN = DN;

                    var result = oManager.CreateOU(ou);

                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }
                else
                {
                    // Update 
                    logger.LogInformation(UpdateItem, "Updating OU DN={DN}", DN);

                    ou.DN = DN;

                    var result = oManager.SaveOU(ou);
                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                    //return this.StatusCode(500);
                }



            }
            else
            {
                return BadRequest();
            }

            //return Conflict();
        }

        #endregion

        #region DELETE

        /// <summary>
        /// Delete the specified DN.
        /// </summary>
        /// <response code="200">Deleted Ok</response>
        /// <response code="404">OU not found</response>
        /// <response code="500">Internal Server error</response>
        [Authorize(Policy = "Writting")]
        [HttpDelete("{DN}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public ActionResult Delete(string DN)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to delete OU:{0}", DN);

            Regex regex = new Regex(@"\Aou=(?<login>[^,]+?),", RegexOptions.IgnoreCase);

            Match match = regex.Match(DN);

            if (!match.Success)
            {
                logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                return Conflict();
            }



            var oManager = OUManager.Instance;

            var dou = oManager.GetOU(DN);

            if (dou == null)
            {
                // No User
                logger.LogError(DeleteItem, "Tring to delete unexistent OU DN={DN}", DN);

                return NotFound();

            }
            else
            {
                // Delete 
                logger.LogInformation(DeleteItem, "Deleting ou DN={DN}", DN);

                var result = oManager.DeleteOU(dou);
                if (result == 0) return Ok();
                else return this.StatusCode(500);

            }


        }

        #endregion
    }
}
