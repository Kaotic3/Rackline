using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Rackline.Domain.Organisations;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

/// <summary>The public, read-only API: its description, cross-site reads, rate limit and drafts.</summary>
[Trait("Category", "Integration")]
public class PublicApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [PostgresFact]
    public async Task The_OpenAPI_document_describes_only_public_reads()
    {
        var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var info = document.RootElement.GetProperty("info");
        info.GetProperty("title").GetString().Should().Be("Rackline public API");

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().ToList();
        var names = paths.Select(p => p.Name).ToList();
        names.Should().Contain(["/api/tournaments", "/api/tournaments/{id}", "/api/ratings", "/api/challenges/{id}", "/api/organisations/{id}", "/api/seasons/{id}"]);
        names.Should().NotContain(n => n.StartsWith("/api/auth") || n.StartsWith("/api/users") || n.EndsWith("/staff"));
        paths.SelectMany(p => p.Value.EnumerateObject().Select(o => o.Name)).Should().OnlyContain(method => method == "get");
        document.RootElement.GetProperty("paths").GetProperty("/api/tournaments/{id}").GetProperty("get").GetProperty("summary").GetString()
            .Should().Contain("entries");
    }

    [PostgresFact]
    public async Task Other_sites_may_read_but_not_write()
    {
        var client = api.CreateClient();
        var read = new HttpRequestMessage(HttpMethod.Get, "/api/tournaments");
        read.Headers.Add("Origin", "https://club.example");
        var readResponse = await client.SendAsync(read);
        readResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("*");

        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/tournaments");
        preflight.Headers.Add("Origin", "https://club.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        var preflightResponse = await client.SendAsync(preflight);
        preflightResponse.Headers.TryGetValues("Access-Control-Allow-Methods", out var allowed);
        (allowed ?? []).Should().NotContain(m => m.Contains("POST"), "only reads are allowed from other sites");
        preflightResponse.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse("cookies are never sent cross-site");
    }

    [PostgresFact]
    public async Task Draft_tournaments_are_not_public()
    {
        var (club, _, _) = await api.AddReferenceDataAsync(0);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var created = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId = club, name = "Secret Draft Open", discipline = "NineBall", format = "SingleElimination", matchLengthKind = "RaceTo", matchLengthValue = 3,
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var visitor = api.CreateClient();

        (await visitor.GetAsync($"/api/tournaments/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await visitor.GetStringAsync("/api/tournaments")).Should().NotContain("Secret Draft Open");
        (await organiser.GetAsync($"/api/tournaments/{id}")).StatusCode.Should().Be(HttpStatusCode.OK, "organisers still see their drafts");

        await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null);
        (await visitor.GetAsync($"/api/tournaments/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task Anonymous_callers_are_rate_limited_and_told_when_to_retry()
    {
        using var limited = api.WithWebHostBuilder(b => b.UseSetting("PublicApi:PermitsPerMinute", "3"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.GetAsync("/api/organisations")).StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
        var rejected = await client.GetAsync("/api/organisations");
        rejected.Headers.GetValues("Retry-After").Should().Equal("60");
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("3 API requests a minute");

        // Pages are not limited.
        (await client.GetAsync("/developers")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task The_developer_page_explains_the_API()
    {
        var html = WebUtility.HtmlDecode(await api.CreateClient().GetStringAsync("/developers"));

        html.Should().Contain("/openapi/v1.json").And.Contain("/api/tournaments/{id}").And.Contain("100000 requests a minute");
        html.Should().NotContain("\"type\":\"server\"");
    }
}
