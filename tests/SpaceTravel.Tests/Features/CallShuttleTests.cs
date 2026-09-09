using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceTravel.Tests.Features;

public sealed class CallShuttleTests : IClassFixture<SpaceTravelApiFactory>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private readonly SpaceTravelApiFactory _factory;
    private readonly HttpClient _client;

    public CallShuttleTests(SpaceTravelApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static object Call(int origin, int destination, params (string Species, decimal WeightKg)[] party)
        => new
        {
            originPlanetId = origin,
            destinationPlanetId = destination,
            lifeForms = party.Select(p => new { species = p.Species, weightKg = p.WeightKg })
        };

    // ---- Happy path ----------------------------------------------------------

    [Fact]
    public async Task A_valid_call_is_created_and_assigned_a_shuttle()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/travel-requests", Call(1, 5, ("Vulcan", 68m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CallResult>(Json);

        Assert.NotNull(body);
        Assert.Equal("Assigned", body.Outcome);
        Assert.Equal("Assigned", body.Status);
        Assert.NotNull(body.ShuttleId);
        Assert.Equal(1, body.LifeFormCount);
        Assert.Equal(68m, body.TotalWeightKg);
        Assert.Equal($"/api/travel-requests/{body.TravelRequestId}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task The_created_call_can_be_read_back()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/travel-requests", Call(2, 4, ("Gorn", 210.5m), ("Vulcan", 68m)));
        var body = await created.Content.ReadFromJsonAsync<CallResult>(Json);

        var fetched = await _client.GetAsync($"/api/travel-requests/{body!.TravelRequestId}");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var detail = await fetched.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Boreth", detail.GetProperty("origin").GetString());
        Assert.Equal("Blue Horizon", detail.GetProperty("destination").GetString());
        Assert.Equal(2, detail.GetProperty("lifeFormCount").GetInt32());
        Assert.Equal(278.5m, detail.GetProperty("totalWeightKg").GetDecimal());
        Assert.Equal(2, detail.GetProperty("lifeForms").GetArrayLength());
    }

    // ---- Validation (validator rejects: 400) ---------------------------------

    [Fact]
    public async Task A_call_to_the_planet_you_are_already_on_is_rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/travel-requests", Call(1, 1, ("Vulcan", 68m)));

        await AssertValidationError(response, "DestinationPlanetId");
    }

    [Fact]
    public async Task An_unknown_planet_is_rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/travel-requests", Call(1, 99, ("Vulcan", 68m)));

        await AssertValidationError(response, "DestinationPlanetId");
    }

    [Fact]
    public async Task An_unknown_origin_is_rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/travel-requests", Call(99, 1, ("Vulcan", 68m)));

        await AssertValidationError(response, "OriginPlanetId");
    }

    [Fact]
    public async Task A_call_with_no_life_forms_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/travel-requests", Call(1, 2));

        await AssertValidationError(response, "LifeForms");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_life_form_must_weigh_more_than_nothing(decimal weightKg)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/travel-requests", Call(1, 2, ("Gorn", weightKg)));

        await AssertValidationError(response, "LifeForms[0].WeightKg");
    }

    [Fact]
    public async Task A_life_form_must_have_a_species()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/travel-requests", Call(1, 2, ("", 80m)));

        await AssertValidationError(response, "LifeForms[0].Species");
    }

    // ---- Business rejection (dispatcher rejects: 201 + Rejected) -------------

    [Fact]
    public async Task A_party_too_large_for_any_shuttle_is_recorded_as_rejected_not_refused()
    {
        var party = Enumerable.Range(0, 21)
            .Select(i => ($"Tribble{i}", 2m))
            .ToArray();

        var response = await _client.PostAsJsonAsync("/api/travel-requests", Call(1, 3, party));

        // The call is a real record with a business outcome, not a malformed request.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CallResult>(Json);
        Assert.Equal("Rejected", body!.Outcome);
        Assert.Contains("20 life forms", body.RejectionReason);
    }

    [Fact]
    public async Task A_life_form_heavier_than_a_shuttle_is_rejected_with_a_reason()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/travel-requests", Call(1, 4, ("Horta", 5000m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CallResult>(Json);
        Assert.Equal("Rejected", body!.Outcome);
        Assert.Contains("4000", body.RejectionReason);
    }

    // ---- Other slices --------------------------------------------------------

    [Fact]
    public async Task An_unknown_travel_request_is_not_found()
    {
        var response = await _client.GetAsync("/api/travel-requests/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_five_docks_are_listed_nearest_first()
    {
        var planets = await _client.GetFromJsonAsync<List<JsonElement>>("/api/planets", Json);

        Assert.NotNull(planets);
        Assert.Equal(5, planets.Count);
        Assert.Equal(
            ["Angel 1", "Boreth", "Aurelia", "Blue Horizon", "Argus X"],
            planets.Select(p => p.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task The_fleet_reports_four_shuttles_with_their_spare_capacity()
    {
        var shuttles = await _client.GetFromJsonAsync<List<JsonElement>>("/api/shuttles", Json);

        Assert.NotNull(shuttles);
        Assert.Equal(4, shuttles.Count);
        Assert.All(shuttles, s =>
        {
            var seats = s.GetProperty("remainingLifeForms").GetInt32();
            var kg = s.GetProperty("remainingWeightKg").GetDecimal();
            Assert.InRange(seats, 0, 20);
            Assert.InRange(kg, 0m, 4000m);
        });
    }

    [Fact]
    public async Task Page_size_is_capped()
    {
        var response = await _client.GetAsync("/api/travel-history?page=1&pageSize=5000");
        await AssertValidationError(response, "PageSize");
    }

    private static async Task AssertValidationError(HttpResponseMessage response, string expectedKey)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var errors = problem.GetProperty("errors");

        Assert.True(
            errors.TryGetProperty(expectedKey, out _),
            $"Expected a validation error on '{expectedKey}'. Got: {errors}");
    }

    private sealed record CallResult(
        int TravelRequestId,
        string Status,
        string Outcome,
        int LifeFormCount,
        decimal TotalWeightKg,
        int? ShuttleId,
        string? ShuttleName,
        int? QueuePosition,
        [property: JsonPropertyName("rejectionReason")] string? RejectionReason);
}
