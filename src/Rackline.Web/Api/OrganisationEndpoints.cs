using System.Security.Claims;
using Rackline.Application.Common;
using Rackline.Application.Organisations;
using Rackline.Application.Players;
using Rackline.Domain.Organisations;
using Rackline.Web.Security;

namespace Rackline.Web.Api;

internal static class OrganisationEndpoints
{
    public static RouteGroupBuilder MapOrganisationEndpoints(this RouteGroupBuilder api)
    {
        var organisations = api.MapGroup("/organisations").WithTags("Organisations");

        organisations.MapGet("/", (IOrganisationQueries queries, CancellationToken ct) => queries.ListAsync(ct));

        organisations.MapGet("/{id:guid}", (Guid id, IOrganisationQueries queries, CancellationToken ct) => DetailAsync(id, queries, ct));

        var manage = organisations.MapGroup("").RequireAuthorization(Policies.ManageOrganisations);

        manage.MapPost("/", async (CreateOrganisationRequest request, OrganisationSetup setup, IOrganisationQueries queries, CancellationToken ct) =>
        {
            var id = await setup.CreateAsync(request.Name, request.Level!.Value, request.ParentId, ct);
            return Results.Created($"/api/organisations/{id}", await queries.GetAsync(id, ct));
        });

        manage.MapPut("/{id:guid}", async (Guid id, RenameOrganisationRequest request, OrganisationSetup setup, IOrganisationQueries queries, CancellationToken ct) =>
        {
            await setup.RenameAsync(id, request.Name, ct);
            return await DetailAsync(id, queries, ct);
        });

        manage.MapPost("/{id:guid}/venues", async (Guid id, AddVenueRequest request, OrganisationSetup setup, CancellationToken ct) =>
        {
            var venueId = await setup.AddVenueAsync(id, request.Name, request.Address, ct);
            return Results.Created($"/api/organisations/{id}", new CreatedResponse(venueId));
        });

        manage.MapPost("/venues/{venueId:guid}/tables", async (Guid venueId, AddTableRequest request, OrganisationSetup setup, CancellationToken ct) =>
            Results.Ok(new CreatedResponse(await setup.AddTableAsync(venueId, request.Label, ct))));

        // Staff lists include email addresses, so only the organisation's organisers may read them.
        manage.MapGet("/{id:guid}/staff", (Guid id, StaffAppointments staff, CancellationToken ct) => staff.ListAsync(id, ct));

        manage.MapPut("/{id:guid}/staff", async (Guid id, AppointStaffRequest request, StaffAppointments staff, CancellationToken ct) =>
        {
            await staff.AppointAsync(id, request.Email, request.Role!.Value, ct);
            return await staff.ListAsync(id, ct);
        });

        manage.MapDelete("/{id:guid}/staff/{userId:guid}", async (Guid id, Guid userId, StaffAppointments staff, CancellationToken ct) =>
        {
            await staff.RemoveAsync(id, userId, ct);
            return await staff.ListAsync(id, ct);
        });

        manage.MapPut("/{id:guid}/players/{playerId:guid}", async (Guid id, Guid playerId, PlayerRegistry registry, CancellationToken ct) =>
        {
            await registry.AddMemberAsync(id, playerId, ct);
            return Results.NoContent();
        });

        manage.MapDelete("/{id:guid}/players/{playerId:guid}", async (Guid id, Guid playerId, PlayerRegistry registry, CancellationToken ct) =>
        {
            await registry.RemoveMemberAsync(id, playerId, ct);
            return Results.NoContent();
        });

        api.MapPost("/players", async (CreatePlayerRequest request, PlayerRegistry registry, IPlayerQueries players, CancellationToken ct) =>
        {
            var id = await registry.CreateAsync(request.OrganisationId!.Value, request.FirstName, request.LastName, request.Nationality, request.LicenceNumber, ct);
            return Results.Created($"/api/players/{id}", await players.GetProfileAsync(id, ct));
        })
        .WithTags("Players")
        .RequireAuthorization(Policies.ManageOrganisations);

        api.MapGet("/users/me/appointments", (ClaimsPrincipal user, IOrganisationQueries queries, CancellationToken ct) =>
            queries.AppointmentsOfAsync(user.UserId()!.Value, ct))
            .WithTags("Users")
            .RequireAuthorization();

        return api;
    }

    private static async Task<OrganisationDetailDto> DetailAsync(Guid id, IOrganisationQueries queries, CancellationToken ct) =>
        await queries.GetAsync(id, ct) ?? throw new NotFoundException("Organisation", id);
}
