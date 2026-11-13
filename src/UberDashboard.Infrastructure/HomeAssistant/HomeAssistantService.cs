using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UberDashboard.Application.HomeAssistant;

namespace UberDashboard.Infrastructure.HomeAssistant;

/// <summary>Klient REST API Home Assistant z tokenem dostępu w nagłówku Bearer.</summary>
public sealed class HomeAssistantService(HttpClient client) : IHomeAssistantService
{
    public async Task<HomeAssistantEntityState> GetStateAsync(HomeAssistantConnection connection, string entityId, CancellationToken cancellationToken)
    {
        var id = HomeAssistantValidation.NormalizeEntityId(entityId);
        using var request = CreateRequest(HttpMethod.Get, connection, $"api/states/{id}");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var attributes = root.TryGetProperty("attributes", out var value) ? value : default;
        return new HomeAssistantEntityState(
            ReadString(root, "state") ?? "",
            ReadString(attributes, "unit_of_measurement"),
            ReadString(attributes, "friendly_name"));
    }

    public async Task CallServiceAsync(HomeAssistantConnection connection, string entityId, CancellationToken cancellationToken)
    {
        var id = HomeAssistantValidation.NormalizeEntityId(entityId);
        var (domain, service) = HomeAssistantValidation.ServiceFor(id);
        using var request = CreateRequest(HttpMethod.Post, connection, $"api/services/{domain}/{service}");
        request.Content = JsonContent.Create(new { entity_id = id });
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, HomeAssistantConnection connection, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(connection.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        return request;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
