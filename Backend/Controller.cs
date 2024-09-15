using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Scriban;
using Ehrenmeter.Backend.Services;

namespace Ehrenmeter.Backend
{
    internal class Main(ILogger<Main> logger, IHTMLViewRetrievalService htmlViewRetrievalService)
    {
        private readonly string _websiteURL = Environment.GetEnvironmentVariable("WebsiteURL") ??
            throw new ArgumentNullException(nameof(_websiteURL));

        private readonly string _stripeProductPriceId = Environment.GetEnvironmentVariable("StripeProductPriceId") ??
            throw new ArgumentNullException(nameof(_stripeProductPriceId));

        private class ClientPrincipal
        {
            private Guid _userId;

            public required string IdentityProvider { get; init; }

            public required string UserId
            {
                get
                {
                    return _userId.ToString();
                }
                init
                {
                    _userId = Guid.Parse(value);
                }
            }

            public required string UserDetails { get; init; }
            public required IEnumerable<string> UserRoles { get; init; }
        }

        [Function("index")]
        public async Task<IActionResult> Index([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
        {
            var pageViewData = new
            {
                StripeProductPriceId = _stripeProductPriceId
            };

            return await RenderView("index", pageViewData);
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
