using System.Net;
using System.Text;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class MicrosoftTodoReaderTests
{
    [Fact]
    public async Task Reads_all_pages_with_delegated_token()
    {
        using var handler = new GraphHandler(
            """{"value":[{"id":"a","displayName":"Pierwsza"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/todo/lists?$skiptoken=2"}""",
            """{"value":[{"id":"b","displayName":"Druga"}]}""");
        using var http = new HttpClient(handler);

        var lists = await new MicrosoftTodoReader(http).ReadListsAsync("test-token", CancellationToken.None);

        Assert.Equal(["a", "b"], lists.Select(item => item.Id));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Rejects_next_page_that_would_forward_the_token_to_another_host()
    {
        using var handler = new GraphHandler(
            """{"value":[],"@odata.nextLink":"https://example.invalid/v1.0/me/todo/lists"}""");
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MicrosoftTodoReader(http).ReadListsAsync("test-token", CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Reads_tasks_using_the_basic_graph_endpoint()
    {
        using var handler = new GraphHandler("""{"value":[]}""");
        using var http = new HttpClient(handler);
        await new MicrosoftTodoReader(http).ReadTasksAsync("list-id", "test-token", CancellationToken.None);
        Assert.Equal("/v1.0/me/todo/lists/list-id/tasks", handler.LastUri!.PathAndQuery);
    }

    private sealed class GraphHandler(params string[] pages) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("graph.microsoft.com", request.RequestUri!.Host);
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pages[RequestCount++], Encoding.UTF8, "application/json"),
            });
        }
    }
}
