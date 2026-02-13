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
using adrapi.Models;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace adrapi.Controllers
{

    [Authorize(Policy = "Reading")]
    [ApiVersion( "2.0" )]
    [ApiVersion("1.0",  Deprecated = true)]
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
        public async Task<ActionResult<IEnumerable<String>>> Get()
        {

            this.ProcessRequest();

            logger.LogInformation(GetItem, "{1} listing all ous", requesterID);

            var oManager = OUManager.Instance;


            //if (_start == 0 && _end == 0)
            return await oManager.GetListAsync();
            //else return gManager.GetList(_start, _end);


        }

        // GET: api/ous/:ou
        [HttpGet("{DN}")]
        public async Task<ActionResult<OU>> Get(string DN)
        {
            this.ProcessRequest();

            if (!TryExtractOuName(DN, out _))
            {
                return Conflict();
            }

            if (!IsDnUnderSearchBase(DN))
            {
                return Conflict();
            }

            var oManager = OUManager.Instance;
            try
            {
                var ou = await oManager.GetOUAsync(DN);
                if (ou == null)
                {
                    return NotFound();
                }

                logger.LogDebug(GetItem, "Getting OU={0}", ou.Name);
                return ou;
            }
            catch (Exception ex)
            {
                logger.LogError(GetItem, "Error getting ou ex:{0}", ex.Message);
                return this.StatusCode(500);
            }



        }

        // GET api/ous/:ou/exists
        [HttpGet("{DN}/exists")]
        public async Task<IActionResult> GetExists(string DN)
        {
            this.ProcessRequest();

            if (!TryExtractOuName(DN, out _))
            {
                return Conflict();
            }

            if (!IsDnUnderSearchBase(DN))
            {
                return Conflict();
            }

            var oManager = OUManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "OU DN={dn} found", DN);
                var ou = await oManager.GetOUAsync(DN);
                if (ou == null)
                {
                    return NotFound();
                }

                return Ok();

            }
            catch (Exception ex)
            {
                logger.LogError(ItemExists, "Error checking OU DN={dn}. err:" + ex.Message, DN );
                return this.StatusCode(500);
            }

        }
        #endregion

        #region POST

        // POST api/ous
        [Authorize(Policy = "Writting")]
        [HttpPost]
        [MapToApiVersion("2.0")]
        public async Task<ActionResult> Post([FromBody] OUCreateRequest request)
        {
            ProcessRequest();

            if (!ModelState.IsValid || request == null || string.IsNullOrWhiteSpace(request.DN))
            {
                return BadRequest();
            }

            if (!TryExtractOuName(request.DN, out var ouNameFromDn))
            {
                logger.LogError(PutItem, "DN is not correcly formated DN={0}", request.DN);
                return Conflict();
            }

            if (!string.Equals(ouNameFromDn, request.Name, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError(PutItem, "OU name and DN OU mismatch name={name} DN={DN}", request.Name, request.DN);
                return Conflict();
            }

            if (!IsDnUnderSearchBase(request.DN))
            {
                logger.LogError(PutItem, "OU DN out of configured search base DN={DN}", request.DN);
                return Conflict();
            }

            if (IsProtectedOuDn(request.DN))
            {
                logger.LogError(PutItem, "Cannot create protected/system OU DN={DN}", request.DN);
                return Conflict();
            }

            var oManager = OUManager.Instance;
            var adou = await oManager.GetOUAsync(request.DN);
            if (adou != null)
            {
                return Conflict();
            }

            var ou = new OU
            {
                DN = request.DN,
                Name = request.Name,
                Description = request.Description
            };

            LogAudit("ou.create.request", request.DN, $"name={request.Name}");
            var result = await oManager.CreateOUAsync(ou);
            if (result == 0)
            {
                LogAudit("ou.create.success", request.DN, $"name={request.Name}");
            }
            return result == 0 ? Ok() : this.StatusCode(500);
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
        public async Task<ActionResult> Put(string DN, [FromBody] OU ou)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to create OU:{0}", DN);

            if (ModelState.IsValid && ou != null)
            {
                if (ou.DN != null && ou.DN != DN)
                {
                    logger.LogError(PutItem, "Ou DN different of the URL DN in put request ou.DN={0} DN={1}", ou.DN, DN);
                    return Conflict();
                }

                if (!TryExtractOuName(DN, out var ouNameFromDn))
                {
                    logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                    return Conflict();
                }

                if (!string.Equals(ouNameFromDn, ou.Name, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError(PutItem, "OU name and DN OU mismatch name={name} DN={DN}", ou.Name, DN);
                    return Conflict();
                }

                if (!IsDnUnderSearchBase(DN))
                {
                    logger.LogError(PutItem, "OU DN out of configured search base DN={DN}", DN);
                    return Conflict();
                }

                if (IsProtectedOuDn(DN))
                {
                    logger.LogError(PutItem, "Cannot update protected/system OU DN={DN}", DN);
                    return Conflict();
                }

                var oManager = OUManager.Instance;

                var adou = await oManager.GetOUAsync(DN);


                if (adou == null)
                {
                    // New Group
                    logger.LogInformation(InsertItem, "Creating OU DN={DN}", DN);

                    ou.DN = DN;

                    LogAudit("ou.create.request", DN, $"name={ou.Name}");
                    var result = await oManager.CreateOUAsync(ou);
                    if (result == 0)
                    {
                        LogAudit("ou.create.success", DN, $"name={ou.Name}");
                    }

                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }
                else
                {
                    // Update 
                    logger.LogInformation(UpdateItem, "Updating OU DN={DN}", DN);

                    ou.DN = DN;

                    LogAudit("ou.update.request", DN, $"name={ou.Name}");
                    var result = await oManager.SaveOUAsync(ou);
                    if (result == 0)
                    {
                        LogAudit("ou.update.success", DN, $"name={ou.Name}");
                    }
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
        public async Task<ActionResult> Delete(string DN)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to delete OU:{0}", DN);

            if (!TryExtractOuName(DN, out _))
            {
                logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                return Conflict();
            }

            if (!IsDnUnderSearchBase(DN))
            {
                logger.LogError(PutItem, "OU DN out of configured search base DN={DN}", DN);
                return Conflict();
            }

            if (IsProtectedOuDn(DN))
            {
                logger.LogError(DeleteItem, "Cannot delete protected/system OU DN={DN}", DN);
                return Conflict();
            }

            var oManager = OUManager.Instance;

            var dou = await oManager.GetOUAsync(DN);

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

                LogAudit("ou.delete.request", DN, "delete");
                var result = await oManager.DeleteOUAsync(dou);
                if (result == 0)
                {
                    LogAudit("ou.delete.success", DN, "delete");
                }
                if (result == 0) return Ok();
                else return this.StatusCode(500);

            }


        }

        #endregion

        private static bool TryExtractOuName(string dn, out string ouName)
        {
            ouName = null;
            if (string.IsNullOrWhiteSpace(dn))
            {
                return false;
            }

            var regex = new Regex(@"\Aou=(?<oname>[^,]+?),", RegexOptions.IgnoreCase);
            var match = regex.Match(dn);
            if (!match.Success)
            {
                return false;
            }

            ouName = match.Groups["oname"].Value;
            return !string.IsNullOrWhiteSpace(ouName);
        }

        private string GetSearchBase()
        {
            return configuration["ldap:searchBase"] ?? string.Empty;
        }

        private bool IsDnUnderSearchBase(string dn)
        {
            var searchBase = GetSearchBase();
            if (string.IsNullOrWhiteSpace(searchBase))
            {
                return true;
            }

            return dn.EndsWith(searchBase, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsProtectedOuDn(string dn)
        {
            var searchBase = GetSearchBase();
            if (string.IsNullOrWhiteSpace(dn))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(searchBase) && string.Equals(dn, searchBase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var protectedOus = configuration.GetSection("ldap:protectedOUs").Get<string[]>() ?? Array.Empty<string>();
            foreach (var protectedOu in protectedOus)
            {
                if (string.IsNullOrWhiteSpace(protectedOu))
                {
                    continue;
                }

                if (string.Equals(dn, protectedOu, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var defaultProtectedPrefixes = new[]
            {
                "OU=Domain Controllers,",
                "OU=System,",
                "OU=Microsoft Exchange System Objects,"
            };

            foreach (var prefix in defaultProtectedPrefixes)
            {
                if (dn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
