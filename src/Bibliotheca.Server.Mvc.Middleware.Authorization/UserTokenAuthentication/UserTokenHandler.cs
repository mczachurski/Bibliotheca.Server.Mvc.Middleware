using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Bibliotheca.Server.Mvc.Middleware.Authorization.UserTokenAuthentication
{
    public class UserTokenHandler : AuthenticationHandler<UserTokenOptions>
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string authorization = Request.Headers["Authorization"];
            string token = null;

            if (string.IsNullOrWhiteSpace(authorization))
            {
                return await Task.FromResult(AuthenticateResult.Skip());
            }

            if (authorization.StartsWith($"{Options.AuthenticationScheme} ", StringComparison.OrdinalIgnoreCase))
            {
                token = authorization.Substring($"{Options.AuthenticationScheme} ".Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return AuthenticateResult.Skip();
            }

            var contextOptions = Context.RequestServices.GetService<IUserTokenConfiguration>();
            var authorizationUrl = contextOptions.GetAuthorizationUrl();
            if(string.IsNullOrWhiteSpace(authorizationUrl))
            {
                return AuthenticateResult.Fail($"{Options.AuthenticationScheme} authentication failed. Authorization server was not specified.");
            }

            var user = await GetUserByTokenAsync(token, authorizationUrl);
            if (user != null)
            {
                var identity = new ClaimsIdentity(Options.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
                identity.AddClaim(new Claim(ClaimTypes.GivenName, user.Name));
                identity.AddClaim(new Claim(ClaimTypes.Name, user.Id));
                identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));

                ClaimsPrincipal principal = new ClaimsPrincipal(identity);

                var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), Options.AuthenticationScheme);

                return await Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return AuthenticateResult.Fail($"{Options.AuthenticationScheme} authentication failed. Credentials are invalid.");
        }

        protected override async Task<bool> HandleUnauthorizedAsync(ChallengeContext context)
        {
            var authResult = await HandleAuthenticateOnceSafeAsync();

            if (!authResult.Skipped)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
            }

            Response.Headers.Append(HeaderNames.WWWAuthenticate, $"{Options.AuthenticationScheme} realm=\"{Options.Realm}\"");
            return false;
        }

        private async Task<UserDto> GetUserByTokenAsync(string token, string authorizationUrl)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("UserToken", token);

            var address = Path.Combine(authorizationUrl, "accessToken");
            var response = await client.GetAsync(address);

            if(response.StatusCode == HttpStatusCode.OK)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                var user = JsonConvert.DeserializeObject<UserDto>(responseString);
                return user;
            }

            return null;
        }
    }
}