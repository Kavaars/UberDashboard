using System.Net;
using System.Text;
using System.Text.Json;
using UberDashboard.App.Features.HomeAssistant;
using UberDashboard.Application.HomeAssistant;
using UberDashboard.Core.ModularPanels;
using UberDashboard.Infrastructure.HomeAssistant;

namespace UberDashboard.App.Tests.HomeAssistant;

public sealed class HomeAssistantTests
{
    [Theory]
    [InlineData("http://192.168.1.10:8123", "http://192.168.1.10:8123/")]
    [InlineData(" https://ha.local/ha/ ", "https://ha.local/ha/")]
    [InlineData("http://homeassistant.local:8123", "http://homeassistant.local:8123/")]
    [InlineData("http://homeassistant:8123", "http://homeassistant:8123/")]
    [InlineData("http://172.16.0.5", "http://172.16.0.5/")]
    [InlineData("https://dom.duckdns.org", "https://dom.duckdns.org/")]
    public void Base_url_is_normalized_with_trailing_slash(string input, string expected) =>
        Assert.Equal(expected, HomeAssistantValidation.NormalizeBaseUrl(input).ToString());

    [Theory]
    [InlineData("ftp://ha.local")]
    [InlineData("http://user:pass@ha.local")]
    [InlineData("http://ha.local/?token=1")]
    [InlineData("not a url")]
    [InlineData("http://dom.duckdns.org:8123")]
    [InlineData("http://8.8.8.8:8123")]
    [InlineData("http://172.32.0.1")]
    public void Base_url_rejects_unsafe_or_invalid_values(string input) =>
        Assert.Throws<ArgumentException>(() => HomeAssistantValidation.NormalizeBaseUrl(input));

    [Theory]
    [InlineData("light.salon")]
    [InlineData("sensor.outdoor_temp_2")]
    public void Entity_id_accepts_home_assistant_format(string input) =>
        Assert.Equal(input, HomeAssistantValidation.NormalizeEntityId(input));

    [Theory]
    [InlineData("light")]
    [InlineData("../api/config")]
    [InlineData("light.salon?x=1")]
    public void Entity_id_rejects_paths_and_queries(string input) =>
        Assert.Throws<ArgumentException>(() => HomeAssistantValidation.NormalizeEntityId(input));

    [Theory]
    [InlineData("button.kitchen", "button", "press")]
    [InlineData("script.morning", "script", "turn_on")]
    [InlineData("light.salon", "homeassistant", "toggle")]
    public void Action_maps_to_domain_service(string entityId, string domain, string service) =>
        Assert.Equal((domain, service), HomeAssistantValidation.ServiceFor(entityId));

    [Fact]
    public async Task Get_state_sends_bearer_token_and_parses_unit()
    {
        var handler = new StubHandler(_ => Json("{\"state\":\"21.5\",\"attributes\":{\"unit_of_measurement\":\"°C\",\"friendly_name\":\"Salon\"}}"));
        var service = new HomeAssistantService(new HttpClient(handler));

        var state = await service.GetStateAsync(Connection(), "sensor.temp", CancellationToken.None);

        Assert.Equal("http://ha.local:8123/api/states/sensor.temp", handler.Last!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Last.Headers.Authorization!.Scheme);
        Assert.Equal("secret", handler.Last.Headers.Authorization.Parameter);
        Assert.Equal(new HomeAssistantEntityState("21.5", "°C", "Salon"), state);
    }

    [Fact]
    public async Task Action_posts_entity_to_domain_service()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new HomeAssistantService(new HttpClient(handler));

        await service.CallServiceAsync(Connection(), "script.morning", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Last!.Method);
        Assert.Equal("http://ha.local:8123/api/services/script/turn_on", handler.Last.RequestUri!.ToString());
        Assert.Equal("{\"entity_id\":\"script.morning\"}", handler.Body);
    }

    [Fact]
    public void Token_round_trips_and_rejects_garbage()
    {
        var protectedToken = HomeAssistantTokenProtector.Protect("secret");

        Assert.DoesNotContain("secret", protectedToken, StringComparison.Ordinal);
        Assert.Equal("secret", HomeAssistantTokenProtector.Unprotect(protectedToken));
        Assert.Null(HomeAssistantTokenProtector.Unprotect("dpapi1:not-base64!"));
        Assert.Null(HomeAssistantTokenProtector.Unprotect(null));
    }

    [Fact]
    public void Document_keeps_modules_without_plain_token()
    {
        var module = new HomeAssistantModuleState("m1", "light.salon", "Salon", HomeAssistantModuleKind.Action);
        var document = new HomeAssistantPanelDocument
        {
            BaseUrl = "http://ha.local:8123/",
            ProtectedToken = HomeAssistantTokenProtector.Protect("secret"),
            ModulePositions = [new ModulePosition("m1", 0, 0)],
            Modules = [module],
        };

        var restored = JsonSerializer.Deserialize<HomeAssistantPanelDocument>(JsonSerializer.SerializeToUtf8Bytes(document))!;

        Assert.Equal(module, Assert.Single(restored.Modules));
        Assert.Equal("http://ha.local:8123/", restored.BaseUrl);
        Assert.Equal("secret", HomeAssistantTokenProtector.Unprotect(restored.ProtectedToken));
    }

    private static HomeAssistantConnection Connection() => new(new Uri("http://ha.local:8123/"), "secret");

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        // The client disposes the request after sending, so the body is captured while it is still alive.
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}

internal sealed class FakeHomeAssistantService : IHomeAssistantService
{
    public Task<HomeAssistantEntityState> GetStateAsync(HomeAssistantConnection connection, string entityId, CancellationToken cancellationToken) =>
        Task.FromResult(new HomeAssistantEntityState("", null, null));

    public Task CallServiceAsync(HomeAssistantConnection connection, string entityId, CancellationToken cancellationToken) => Task.CompletedTask;
}
