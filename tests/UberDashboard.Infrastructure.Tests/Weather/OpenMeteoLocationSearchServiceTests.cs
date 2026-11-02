using System.Net;
using UberDashboard.Infrastructure.Tests.TestSupport;
using UberDashboard.Infrastructure.Weather;

namespace UberDashboard.Infrastructure.Tests.Weather;

public sealed class OpenMeteoLocationSearchServiceTests
{
    [Fact]
    public async Task Returns_locations_from_search_response()
    {
        using var client = new HttpClient(new TestHttpMessageHandler(static (request, _) =>
        {
            Assert.Contains("/v1/search", request.RequestUri!.PathAndQuery, StringComparison.Ordinal);
            Assert.Contains("name=Krakow", request.RequestUri.PathAndQuery, StringComparison.Ordinal);
            return TestHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """
                {
                  "results": [
                    {
                      "name": "Kraków",
                      "admin1": "Małopolskie",
                      "country": "Polska",
                      "latitude": 50.0614,
                      "longitude": 19.9366,
                      "timezone": "Europe/Warsaw"
                    }
                  ]
                }
                """);
        }))
        {
            BaseAddress = new Uri("https://geocoding-api.open-meteo.com"),
        };

        var service = new OpenMeteoLocationSearchService(client);

        var locations = await service.SearchAsync("Krakow", 8, CancellationToken.None);

        var location = Assert.Single(locations);
        Assert.Equal("Kraków", location.Name);
        Assert.Equal("Małopolskie", location.Region);
        Assert.Equal("Polska", location.Country);
        Assert.Equal("Europe/Warsaw", location.TimeZone);
    }
}
