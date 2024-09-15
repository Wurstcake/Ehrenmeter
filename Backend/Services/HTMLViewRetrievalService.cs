using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace Ehrenmeter.Backend.Services
{
    interface IHTMLViewRetrievalService
    {
        Task<string> GetView(string viewName);
    }

    internal class HTMLViewRetrievalService(ILogger<HTMLViewRetrievalService> logger) : IHTMLViewRetrievalService
    {
        string viewsBaseUri = Environment.GetEnvironmentVariable("ViewsSourceURI") ?? throw new ArgumentNullException(nameof(viewsBaseUri));

        public async Task<string> GetView(string viewName)
        {
            try
            {
                var viewUri = $"{viewsBaseUri}/{viewName}.html";
                var client = new BlockBlobClient(new Uri(viewUri));

                var content = await client.DownloadContentAsync();

                return content.Value.Content.ToString();
            }
            catch (Exception ex)
            {
                logger.LogError($"Couldnt get the view {viewName}. Error: {ex.Message}");
                return string.Empty;
            }
        }
    }


    internal class HTMLViewRetrievalServiceDev : IHTMLViewRetrievalService
    {
        string viewsBaseUri = Environment.GetEnvironmentVariable("ViewsSourceURI") ?? throw new ArgumentNullException(nameof(viewsBaseUri));

        public async Task<string> GetView(string viewName)
        {
            var url = $"{viewsBaseUri}/{viewName}.html";

            using HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }
    }
}

