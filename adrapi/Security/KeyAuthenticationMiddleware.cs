using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Text;

namespace adrapi.Security
{
    public class KeyAuthenticationMiddleware
    {

        private readonly RequestDelegate _next;

        public KeyAuthenticationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            string api_key = context.Request.Headers["api-key"];

            if (api_key != null)
            {
                string[] vals = api_key.Split(':');


                var key = ApiKeyManager.Find(vals[0]);

                if (key != null && key.secretKey == vals[1] && key.authorizedIP == context.Request.HttpContext.Connection.RemoteIpAddress.ToString())
                {
                    await _next.Invoke(context);
                }
                else
                {
                    context.Response.StatusCode = 401; //Unauthorized
                    return;
                }
            }
            else
            {
                // no authorization header
                context.Response.StatusCode = 401; //Unauthorized
                return;
            }
        }
    }
}
