using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Scriban;
using Ehrenmeter.Backend.Services;

namespace Ehrenmeter.Backend
{
    internal class Main(ILogger<Main> logger, IHTMLViewRetrievalService htmlViewRetrievalService, IDbService dbService,
            IJwtTokenService _jwtTokenService)
    {
        [Function("ehre")]
        public async Task<IActionResult> Ehre([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            var token = req.Headers["Authorization"].ToString().Replace("Bearer ", "");
            if (token is null ||
                !await _jwtTokenService.ValidateToken(token))
            {
                return await RenderView("login");
            }

            var userId = _jwtTokenService.GetUserId(token);

            if (req.Method == "POST"){
                var formData = await req.ReadFormAsync();
        
                var receiverId = int.Parse(formData["receiver-id"].ToString());
                var amount = int.Parse(formData["amount"].ToString());
                string description = formData["description"].ToString();

                await dbService.GiveEhre(userId, receiverId, amount, description);
            }

            var users = await dbService.GetTopUsers();
            var pageViewData = new {
                Users = users
            };

            logger.LogInformation($"Retrieved {users.Count} users for ehre view");
            return await RenderView("ehre", pageViewData);
        }

        [Function("ehre-history")]
        public async Task<IActionResult> EhreHistory([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            var token = req.Headers["Authorization"].ToString().Replace("Bearer ", "");
            if (token is null ||
                !await _jwtTokenService.ValidateToken(token))
            {
                return await RenderView("login");
            }

            var queryData = req.Query;

            var receiverId = int.Parse(queryData["receiver-id"].ToString());
            
            var receiver = await dbService.GetUser(receiverId) ?? throw new BadHttpRequestException("Invalid receiver id");
            var transactions = await dbService.GetUserEhreHistory(receiver);
            var pageViewData = new {
                Username = receiver.Username,
                Transactions = transactions
            };

            logger.LogInformation($"Retrieved {transactions.Count} transactions for ehre history view");
            return await RenderView("ehre-history", pageViewData);
        }

        [Function("login")]
        public async Task<IActionResult> Login([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
        {
            var form = await req.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();

            logger.LogInformation($"Got a login request with username {username}");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)){
                return new UnauthorizedResult();
            }

            var authResult = await dbService.AuthenticateUser(username, password);
            if (!authResult.isAuthenticated){
                return new UnauthorizedResult();
            }

            var token = _jwtTokenService.GenerateToken(authResult.userId);
            return new OkObjectResult(new { token });
        }

        [Function("signup")]
        public async Task<IActionResult> SignUp([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            if (req.Method == "GET"){
                return await RenderView("signup");
            }

            if (!req.HasFormContentType)
            {
                return new BadRequestObjectResult("Invalid form submission");
            }

            var form = await req.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();

            logger.LogInformation($"Got a sign up request with username {username}");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return new BadRequestObjectResult("User registration failed - missing fields");
            }

            var user = await dbService.RegisterUser(username, password);

            if (user is null)
            {
                return new BadRequestObjectResult("User registration failed");
            }

            var token = _jwtTokenService.GenerateToken(user.UserId);
            return new OkObjectResult(new { token });
        }

        private async Task<ContentResult> RenderView(string viewName, object? @object = null)
        {
            string html = await htmlViewRetrievalService.GetView(viewName);
            var template = Template.Parse(html);

            // Render the template with the model
            var htmlContent = template.Render(@object);

            return new ContentResult { Content = htmlContent, ContentType = "text/html" };
        }

    }
}
