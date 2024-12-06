using Azure.Search.Documents.Models;
using Azure.Search.Documents;
using Azure;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Text.Json;
using AdaptiveCards.Rendering;
using Microsoft.Extensions.Configuration;
using Azure.Search.Documents.Indexes;

namespace ITSupportBot.Services
{
    public class AzureSearchService
    {
        private readonly IConfiguration _configuration;

        public AzureSearchService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<SearchResult> GetTopSearchResultAsync(string query)
        {
            query = query.Replace("\"", "");
            // Initialize Azure Cognitive Search clients
            var searchCredential = new AzureKeyCredential(_configuration["AzureSearchKey"]);
            var indexClient = new SearchIndexClient(new Uri(_configuration["AzureSearchServiceEndpoint"]), searchCredential);
            var searchClient = indexClient.GetSearchClient(_configuration["IndexName"]);

            var searchOptions = new SearchOptions
            {
                Size = 1, // Retrieve only the top result
                IncludeTotalCount = false, // We only need the top result
                QueryType = SearchQueryType.Semantic,
                SemanticSearch = new SemanticSearchOptions
                {
                    QueryCaption = new(QueryCaptionType.Extractive),
                    QueryAnswer = new(QueryAnswerType.Extractive),
                    SemanticConfigurationName = "default",
                }

            };

            var response = await searchClient.SearchAsync<SearchDocument>(query, searchOptions);

            if (response.Value.GetResults().Any())
            {
                var firstResult = response.Value.GetResults().First();

                return new SearchResult
                {
                    Content = firstResult.Document.TryGetValue("content", out var content) ? content.ToString() : null,
                    metadata_storage_name = firstResult.Document.TryGetValue("metadata_storage_name", out var metadata_storage_name) ? metadata_storage_name.ToString() : null,
                };
            }


            return null; // No result found
        }
    }

    public class SearchResult
    {
        public string Content { get; set; }
        public string metadata_storage_name { get; set; }

    }
}