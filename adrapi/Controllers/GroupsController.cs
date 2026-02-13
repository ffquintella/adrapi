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
using System.Threading.Tasks;
using adrapi.Tools;
using System.Linq;

namespace adrapi.Controllers
{
    //[Produces("application/json")]
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0",  Deprecated = true)]
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
        public async Task<ActionResult<IEnumerable<String>>> Get([FromQuery]int _start, [FromQuery]int _end)
        {

            this.ProcessRequest();

            logger.LogDebug(GetItem, "{0} listing all groups", requesterID);

            var gManager = GroupManager.Instance;

            if (_start == 0 && _end != 0)
            {
                return Conflict();
            }

            if (_start == 0 && _end == 0) 
            return await gManager.GetListAsync();
            else return await gManager.GetListAsync(_start, _end);


        }

        // GET api/groups 
        [HttpGet]
        public async Task<ActionResult<IEnumerable<domain.Group>>> Get([RequiredFromQuery]bool _full, [FromQuery]int _start, [FromQuery]int _end)
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
                groups = await gManager.GetGroupsAsync();
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
        public async Task<ActionResult<domain.Group>> Get(string DN)
        {
            this.ProcessRequest();

            if (!IsValidGroupDn(DN))
            {
                return Conflict();
            }

            var gManager = GroupManager.Instance;
            try
            {
                var group = await gManager.GetGroupAsync(DN);
                if (group == null)
                {
                    return NotFound();
                }
                logger.LogDebug(GetItem, "Getting OU={0}", group.Name);
                return group;
            }
            catch(Exception ex)
            {
                logger.LogError(GetItem, "Error getting group ex:{0}", ex.Message);
                return this.StatusCode(500);
            }



        }

        // GET api/groups/:group/exists
        [HttpGet("{DN}/exists")]
        public async Task<IActionResult> GetExists(string DN)
        {
            this.ProcessRequest();

            if (!IsValidGroupDn(DN))
            {
                return Conflict();
            }

            var gManager = GroupManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "Group DN={dn} found", DN);
                var group = await gManager.GetGroupAsync(DN);

                if (group == null)
                {
                    logger.LogDebug(ItemExists, "Group DN={dn} not found.", DN);
                    return NotFound();
                }
                
                return Ok();

            }
            catch (Exception ex)
            {
                logger.LogError(ItemExists, "Error checking group DN={dn}. err:" + ex.Message, DN);
                return this.StatusCode(500);
            }

        }



        // GET api/groups/:group/members
        [HttpGet("{DN}/members")]
        public async Task<ActionResult<List<String>>> GetMembers(string DN, [FromQuery]Boolean _listCN = false)
        {
            this.ProcessRequest();
            if (!IsValidGroupDn(DN))
            {
                return Conflict();
            }

            var gManager = GroupManager.Instance;

            try
            {
                logger.LogDebug(ListItems, "Group DN={dn} found", DN);
                var group = await gManager.GetGroupAsync(DN, _listCN);
                if (group == null)
                {
                    return NotFound();
                }

                return group.Member;

            }
            catch (Exception ex)
            {
                logger.LogError(ListItems, "Error listing members for group DN={dn}. err:" + ex.Message, DN);
                return this.StatusCode(500);
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
        /// <param name="_listCN">If the members are in CN format</param>
        [Authorize(Policy = "Writting")]
        [HttpPut("{DN}")]
        public async Task<ActionResult> Put(string DN, [FromBody] domain.Group group, [FromQuery] Boolean _listCN = false)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to create group:{0}", DN);

            if (ModelState.IsValid && group != null)
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

                var gName= match.Groups["gname"].Value;
                if (!string.Equals(gName, group.Name, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError(PutItem, "Group name and DN CN mismatch name={name} DN={DN}", group.Name, DN);
                    return Conflict();
                }

                var gManager = GroupManager.Instance;
                var uManager = UserManager.Instance;

                var adgroup = await gManager.GetGroupAsync(DN);

                if (_listCN)
                {
                    var members = @group.Member.Clone(); 
                        //group.Member;
                    
                    group.Member.Clear();

                    foreach (String member in members)
                    {
                        string dname = "";

                        var grp = await gManager.GetGroupAsync(member, true, true);
                        if (grp != null) dname = grp.DN;
                        else
                        {
                            var user = await uManager.GetUserAsync(member, "samaccountname");
                            if (user != null) dname = user.DN;
                            else
                            {
                                logger.LogError(InternalError, "Could not find member {member} for group {DN}", member,
                                    DN);
                                return this.StatusCode(422);
                            }
                        }


                        group.Member.Add(dname);
                    }
                }


                if (adgroup == null)
                {
                    // New Group
                    logger.LogInformation(InsertItem, "Creating group DN={DN}", DN);

                    group.DN = DN;

                    LogAudit("group.v1.create.request", DN, $"membersCount={group.Member.Count}");
                    var result = await gManager.CreateGroupAsync(group);
                    if (result == 0)
                    {
                        LogAudit("group.v1.create.success", DN, $"membersCount={group.Member.Count}");
                    }

                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }
                else
                {
                    // Update 
                    logger.LogInformation(UpdateItem, "Updating group DN={DN}", DN);

                    group.DN = DN;

                    LogAudit("group.v1.update.request", DN, $"membersCount={group.Member.Count}");
                    var result = await gManager.SaveGroupAsync(group);
                    if (result == 0)
                    {
                        LogAudit("group.v1.update.success", DN, $"membersCount={group.Member.Count}");
                    }
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



        /// <summary>
        /// Update the member of a single group
        /// </summary>
        /// <param name="DN">The DN of the group</param>
        /// <param name="members">The list of members or in DN or CN format</param>
        /// <param name="_listCN">If the list is in CN format</param>
        /// <returns></returns>
        // PUT api/groups/:group/members
        [Authorize(Policy = "Writting")]
        [HttpPut("{DN}/members")]
        public async Task<ActionResult> PutMembers(string DN, [FromBody] String[] members, [FromQuery] Boolean _listCN = false)
        {
            this.ProcessRequest();
            var gManager = GroupManager.Instance;
            var uManager = UserManager.Instance;

            try
            {
                if (!IsValidGroupDn(DN))
                {
                    return Conflict();
                }

                logger.LogDebug(ListItems, "Group DN={dn} found", DN);
                var group = await gManager.GetGroupAsync(DN);
                if (group == null)
                {
                    return NotFound();
                }

                var resolvedMembers = new List<string>();

                foreach(String member in members)
                {
                    string dname = member;
                    if (_listCN)
                    {
                        var grp = await gManager.GetGroupAsync(member, true,true);
                        if(grp != null) dname = grp.DN;
                        else
                        {
                            var user = await uManager.GetUserAsync(member, "samaccountname");
                            if (user != null) dname = user.DN;
                            else
                            {
                                logger.LogError(InternalError, "Could not find member {member} for group {DN}", member, DN);
                                return this.StatusCode(422);
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(dname))
                    {
                        logger.LogError(InternalError, "Could not find member {member} for group {DN}", member, DN);
                        return this.StatusCode(422);
                    }

                    if (!resolvedMembers.Any(existing => string.Equals(existing, dname, StringComparison.OrdinalIgnoreCase)))
                    {
                        resolvedMembers.Add(dname);
                    }
                }

                group.Member = resolvedMembers;

                try
                {
                    logger.LogInformation(PutItem, "Saving group members for group:{DN}", DN);
                    LogAudit("group.v1.members.replace.request", DN, $"membersCount={group.Member.Count}");
                    await gManager.SaveGroupAsync(group);
                    LogAudit("group.v1.members.replace.success", DN, $"membersCount={group.Member.Count}");
                    return Ok();
                }catch(Exception ex)
                {
                    logger.LogError(InternalError, "Error saving DN={dn} EX: {message}", DN, ex.Message);
                    return this.StatusCode(500);
                }

                //return group.Member;

            }
            catch (Exception ex)
            {
                logger.LogError(ListItems, "Error updating members for group DN={dn}. err:{message} ", DN, ex.Message);
                return this.StatusCode(500);
            }

        }

        #endregion

        #region DELETE

        /// <summary>
        /// Delete the specified DN.
        /// </summary>
        /// <response code="200">Deleted Ok</response>
        /// <response code="404">User not found</response>
        /// <response code="500">Internal Server error</response>
        [Authorize(Policy = "Writting")]
        [HttpDelete("{DN}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> Delete(string DN)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to delete group:{0}", DN);

            Regex regex = new Regex(@"\Acn=(?<login>[^,]+?),", RegexOptions.IgnoreCase);

            Match match = regex.Match(DN);

            if (!match.Success)
            {
                logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                return Conflict();
            }

            var gManager = GroupManager.Instance;

            var dgroup = await gManager.GetGroupAsync(DN);

            if (dgroup == null)
            {
                // No User
                logger.LogError(DeleteItem, "Tring to delete unexistent group DN={DN}", DN);

                return NotFound();

            }
            else
            {
                // Delete 
                logger.LogInformation(DeleteItem, "Deleting group DN={DN}", DN);

                LogAudit("group.v1.delete.request", DN, "delete");
                var result = await gManager.DeleteGroup(dgroup);
                if (result == 0)
                {
                    LogAudit("group.v1.delete.success", DN, "delete");
                }
                if (result == 0) return Ok();
                else return this.StatusCode(500);

            }


        }

        #endregion

        private static bool IsValidGroupDn(string dn)
        {
            var regex = new Regex(@"\Acn=(?<gname>[^,]+?),", RegexOptions.IgnoreCase);
            return regex.IsMatch(dn);
        }
    }
}
