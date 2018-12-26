﻿using System;
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
    }
}
