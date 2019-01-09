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
using NLog;
using adrapi.domain;
using System.Text.RegularExpressions;

namespace adrapi.Controllers
{
    //[Produces("application/json")]
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [ApiController]
    public class GroupsController: BaseController
    {


        public GroupsController(ILogger<GroupsController> logger, IConfiguration iConfig)
        {
      
            this.logger = logger;

            configuration = iConfig;
        }

        #region GET
        // GET api/groups
        [HttpGet]
        public ActionResult<IEnumerable<String>> Get([FromQuery]int _start, [FromQuery]int _end)
        {

            this.ProcessRequest();

            logger.LogDebug(GetItem, "{0} listing all groups", requesterID);

            var gManager = GroupManager.Instance;

            if (_start == 0 && _end != 0)
            {
                return Conflict();
            }

            if (_start == 0 && _end == 0) 
            return gManager.GetList();
            else return gManager.GetList(_start, _end);


        }

        // GET api/groups 
        [HttpGet]
        public ActionResult<IEnumerable<domain.Group>> Get([RequiredFromQuery]bool _full, [FromQuery]int _start, [FromQuery]int _end)
        {
            if (_full)
            {
                this.ProcessRequest();

                logger.LogDebug(ListItems, "{0} getting all groups objects", requesterID);

                if (_start == 0 && _end != 0)
                {
                    return Conflict();
                }

                var gManager = GroupManager.Instance;
                List<domain.Group> groups;

                //if (_start == 0 && _end == 0) 
                groups = gManager.GetGroups();
                //else groups = gManager.GetGroups(_start, _end);

                return groups;
            }
            else
            {
                return new List<domain.Group>();
            }
        }

        //TODO: BUG There is a bug related to doing a limited search then doing another one here... FIX IT! :-)
        // GET api/groups/:group
        [HttpGet("{DN}")]
        public ActionResult<domain.Group> Get(string DN)
        {
            this.ProcessRequest();

            var gManager = GroupManager.Instance;
            try
            {
                var group = gManager.GetGroup(DN);
                logger.LogDebug(GetItem, "Getting OU={0}", group.Name);
                return group;
            }
            catch(Exception ex)
            {
                logger.LogError(GetItem, "Error getting group ex:{0}", ex.Message);
                return null;
            }



        }

        // GET api/groups/:group/exists
        [HttpGet("{DN}/exists")]
        public IActionResult GetExists(string DN)
        {
            this.ProcessRequest();

            var gManager = GroupManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "Group DN={dn} found", DN);
                var group = gManager.GetGroup(DN);

                return Ok();

            }
            catch (Exception ex)
            {
                logger.LogDebug(ItemExists, "Group DN={dn} not found.");
                return NotFound();
            }

        }



        // GET api/groups/:group/members
        [HttpGet("{DN}/members")]
        public ActionResult<List<String>> GetMembers(string DN)
        {
            this.ProcessRequest();
            var gManager = GroupManager.Instance;

            try
            {
                logger.LogDebug(ListItems, "Group DN={dn} found");
                var group = gManager.GetGroup(DN);

                return group.Member;

            }
            catch (Exception ex)
            {
                logger.LogDebug(ListItems, "Group DN={dn} not found.");
                return NotFound();
            }

        }
        #endregion

        #region PUT
        // PUT api/groups/:group
        /// <summary>
        /// Creates the specified group.
        /// </summary>
        /// <returns>The put.</returns>
        /// <param name="group">Group.</param>
        [Authorize(Policy = "Writting")]
        [HttpPut("{DN}")]
        public ActionResult Put(string DN, [FromBody] domain.Group group)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to create group:{0}", DN);

            if (ModelState.IsValid)
            {
                if (group.DN != null && group.DN != DN)
                {
                    logger.LogError(PutItem, "Group DN different of the URL DN in put request group.DN={0} DN={1}", group.DN, DN);
                    return Conflict();
                }

                Regex regex = new Regex(@"\Acn=(?<gname>[^,]+?),", RegexOptions.IgnoreCase);

                Match match = regex.Match(DN);

                if (!match.Success)
                {
                    logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                    return Conflict();
                }

                var gName= match.Groups["gname"];

                var gManager = GroupManager.Instance;

                var adgroup = gManager.GetGroup(DN);


                if (adgroup == null)
                {
                    // New Group
                    logger.LogInformation(InsertItem, "Creating group DN={DN}", DN);

                    group.DN = DN;

                    var result = gManager.CreateGroup(group);

                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }
                else
                {
                    // Update 
                    logger.LogInformation(UpdateItem, "Updating group DN={DN}", DN);

                    group.DN = DN;

                    var result = gManager.SaveGroup(group);
                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }



            }
            else
            {
                return BadRequest();
            }

            //return Conflict();
        }



        // PUT api/groups/:group/members
        [HttpPut("{DN}/members")]
        public ActionResult PutMembers(string DN, [FromBody] String[] members)
        {
            this.ProcessRequest();
            var gManager = GroupManager.Instance;

            try
            {
                logger.LogDebug(ListItems, "Group DN={dn} found");
                var group = gManager.GetGroup(DN);

                group.Member.Clear();

                foreach(String member in members)
                {
                    group.Member.Add(member);
                }

                try
                {
                    logger.LogInformation(PutItem, "Saving group members for group:{DN}", DN);
                    gManager.SaveGroup(group);
                    return Ok();
                }catch(Exception ex)
                {
                    logger.LogError(InternalError, "Error saving DN={dn} EX:", DN, ex.Message);
                    return this.StatusCode(500);
                }

                //return group.Member;

            }
            catch (Exception ex)
            {
                logger.LogDebug(ListItems, "Group DN={dn} not found.", DN);
                return NotFound();
            }

        }

        #endregion
    }
}
