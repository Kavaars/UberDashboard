using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed record MicrosoftTodoList(string Id, string DisplayName);
public sealed record MicrosoftTodoTask(string Id, string Title, string Status);

/// <summary>Reads Microsoft To Do with delegated access and follows Graph pagination.</summary>
public sealed class MicrosoftTodoReader(HttpClient client)
{
    private static readonly Uri Graph = new("https://graph.microsoft.com/v1.0/me/todo/");

    public Task<IReadOnlyList<MicrosoftTodoList>> ReadListsAsync(string token, CancellationToken cancellationToken) =>
        ReadAsync<MicrosoftTodoList>(new Uri(Graph, "lists"), token, cancellationToken);

    public Task<IReadOnlyList<MicrosoftTodoTask>> ReadTasksAsync(string listId, string token, CancellationToken cancellationToken) =>
        ReadAsync<MicrosoftTodoTask>(new Uri(Graph, $"lists/{Uri.EscapeDataString(listId)}/tasks?$select=id,title,status"),
            token, cancellationToken);

    private async Task<IReadOnlyList<T>> ReadAsync<T>(Uri uri, string token, CancellationToken cancellationToken)
    {
        var result = new List<T>();
        var seen = new HashSet<Uri>();
        while (true)
        {
            if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Host != Graph.Host ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                !uri.AbsolutePath.StartsWith(Graph.AbsolutePath, StringComparison.Ordinal) || !seen.Add(uri) || seen.Count > 1_000)
                throw new InvalidDataException("Microsoft Graph zwrócił nieprawidłowy adres kolejnej strony.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<Page<T>>(body, JsonSerializerOptions.Web, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidDataException("Microsoft Graph zwrócił pustą odpowiedź.");
            result.AddRange(page.Value ?? throw new InvalidDataException("Brakuje danych na stronie Microsoft Graph."));
            if (result.Count > 10_000)
                throw new InvalidDataException("Przekroczono limit 10 000 elementów na import.");
            if (string.IsNullOrEmpty(page.NextLink)) return result;
            if (!Uri.TryCreate(page.NextLink, UriKind.Absolute, out var next))
                throw new InvalidDataException("Microsoft Graph zwrócił nieprawidłowy adres kolejnej strony.");
            uri = next;
        }
    }

    private sealed record Page<T>(T[]? Value, [property: JsonPropertyName("@odata.nextLink")] string? NextLink);
}
