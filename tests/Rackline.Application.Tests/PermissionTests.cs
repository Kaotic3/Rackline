using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Application.Organisations;
using Rackline.Application.Rankings;
using Rackline.Application.Tournaments;
using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Organisations;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tests;

/// <summary>
/// Who may do what. A small hierarchy: a nation with two regions, each with a club. Commands run as
/// <see cref="_user"/>, whose appointments each test sets up.
/// </summary>
public class PermissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 19, 0, 0, TimeSpan.Zero);

    private readonly Organisation _nation = new("Nation Pool Association", OrganisationLevel.National);
    private readonly Organisation _north;
    private readonly Organisation _south;
    private readonly Organisation _northClub;
    private readonly Organisation _southClub;

    private readonly FakeUser _user = new() { IsAdmin = false };
    private readonly FakeDirectory _directory = new();
    private readonly InMemoryTournaments _tournaments = new();
    private readonly InMemoryRankings _rankings = new();
    private readonly InMemoryOrganisations _organisations = new();
    private readonly InMemoryPlayers _players = new();
    private readonly FakeUsers _users = new();
    private readonly FakeReferences _references = new();
    private readonly RecordingNotifier _notifier = new();

    public PermissionTests()
    {
        _north = new Organisation("North Region", OrganisationLevel.Regional, _nation);
        _south = new Organisation("South Region", OrganisationLevel.Regional, _nation);
        _northClub = new Organisation("Northgate Club", OrganisationLevel.Club, _north);
        _southClub = new Organisation("Southgate Club", OrganisationLevel.Club, _south);

        foreach (var organisation in new[] { _nation, _north, _south, _northClub, _southClub })
        {
            _directory.Parents[organisation.Id] = organisation.ParentId;
            _references.Organisations.Add(organisation.Id);
            _organisations.Organisations.Add(organisation);
        }
    }

    private OrganisationAccess Access => new(_user, _directory);

    private TournamentLifecycle Lifecycle => new(_tournaments, _references, _rankings, Access, _notifier);

    private void Appoint(Organisation organisation, StaffRole role) => _directory.Appointments.Add(new Appointment(organisation.Id, _user.UserId!.Value, role));

    private static TournamentSettings Settings(Guid? rankingId = null) =>
        new("Permission Open", Discipline.NineBall, TournamentFormat.SingleElimination, MatchLengthKind.RaceTo, 3, RankingId: rankingId);

    /// <summary>A tournament at the organisation, made by an administrator.</summary>
    private async Task<Guid> TournamentAtAsync(Organisation organisation, int players = 0)
    {
        var admin = new OrganisationAccess(new FakeUser(), _directory);
        var lifecycle = new TournamentLifecycle(_tournaments, _references, _rankings, admin, _notifier);
        var id = await lifecycle.CreateAsync(organisation.Id, Settings());
        if (players > 0)
        {
            await lifecycle.OpenRegistrationAsync(id);
            var entries = new TournamentEntries(_tournaments, _references, admin, _notifier);
            for (var i = 0; i < players; i++)
            {
                var player = Guid.CreateVersion7();
                _references.Players.Add(player);
                await entries.AddAsync(id, player, null, 0);
            }

            await new TournamentDraws(_tournaments, admin, _notifier).MakeAsync(id, 1);
            await lifecycle.StartAsync(id);
        }

        return id;
    }

    [Fact]
    public async Task An_organiser_runs_events_at_their_organisation_and_everything_below_it()
    {
        Appoint(_north, StaffRole.Organiser);

        var atRegion = await Lifecycle.CreateAsync(_north.Id, Settings());
        var atClub = await Lifecycle.CreateAsync(_northClub.Id, Settings());
        await Lifecycle.OpenRegistrationAsync(atClub);

        _tournaments.Saved.Select(t => t.Id).Should().BeEquivalentTo([atRegion, atClub]);
    }

    [Fact]
    public async Task An_organiser_cannot_reach_upwards_or_sideways()
    {
        Appoint(_north, StaffRole.Organiser);
        var southEvent = await TournamentAtAsync(_southClub);

        await FluentActions.Awaiting(() => Lifecycle.CreateAsync(_nation.Id, Settings())).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => Lifecycle.CreateAsync(_southClub.Id, Settings())).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => Lifecycle.OpenRegistrationAsync(southEvent)).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => Lifecycle.DeleteAsync(southEvent)).Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_referee_scores_matches_but_cannot_run_the_tournament()
    {
        var id = await TournamentAtAsync(_northClub, players: 2);
        Appoint(_north, StaffRole.Referee);
        var match = _tournaments.Saved.Single().Matches.Single();
        var results = new MatchResults(_tournaments, _references, new InMemoryRatings(), Access, _notifier, new FixedTime(Now));

        await results.RecordResultAsync(id, match.Number, 3, 1);
        match.State.Should().Be(MatchState.Completed);

        await FluentActions.Awaiting(() => Lifecycle.CompleteAsync(id)).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => new TournamentDraws(_tournaments, Access, _notifier).UndoAsync(id)).Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Someone_with_no_appointment_or_not_signed_in_can_do_nothing()
    {
        var id = await TournamentAtAsync(_northClub, players: 2);
        var number = _tournaments.Saved.Single().Matches.Single().Number;
        var results = new MatchResults(_tournaments, _references, new InMemoryRatings(), Access, _notifier, new FixedTime(Now));

        await FluentActions.Awaiting(() => results.StartAsync(id, number, null)).Should().ThrowAsync<ForbiddenException>();
        _user.UserId = null;
        await FluentActions.Awaiting(() => results.StartAsync(id, number, null)).Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Unknown_things_are_not_found_before_permission_is_checked()
    {
        await FluentActions.Awaiting(() => Lifecycle.StartAsync(Guid.CreateVersion7())).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Lifecycle.CreateAsync(Guid.CreateVersion7(), Settings())).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task A_club_event_joins_a_national_ranking_only_with_the_national_body_s_say_so()
    {
        var admin = new RankingSetup(_rankings, _references, new OrganisationAccess(new FakeUser(), _directory));
        var national = await admin.CreateAsync(_nation.Id, "National 9-ball rankings", Discipline.NineBall, "2026", [50, 30], 0);

        // A club organiser cannot put their own event into the national ranking...
        Appoint(_northClub, StaffRole.Organiser);
        await FluentActions.Awaiting(() => Lifecycle.CreateAsync(_northClub.Id, Settings(national))).Should().ThrowAsync<ForbiddenException>();
        var id = await Lifecycle.CreateAsync(_northClub.Id, Settings());

        // ...but a national organiser can, and the club organiser can then still edit the event.
        _directory.Appointments.Clear();
        Appoint(_nation, StaffRole.Organiser);
        await Lifecycle.UpdateAsync(id, Settings(national));
        _directory.Appointments.Clear();
        Appoint(_northClub, StaffRole.Organiser);
        await Lifecycle.UpdateAsync(id, Settings(national) with { Name = "Renamed by the club" });

        _tournaments.Saved.Single().Should().BeEquivalentTo(new { RankingId = (Guid?)national, Name = "Renamed by the club" }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task Rankings_and_leagues_need_an_organiser_of_their_organisation()
    {
        Appoint(_north, StaffRole.Referee);
        var rankings = new RankingSetup(_rankings, _references, Access);
        var leagues = new Leagues.LeagueSetup(new InMemoryLeagues(), _references, Access);

        await FluentActions.Awaiting(() => rankings.CreateAsync(_north.Id, "Regional", Discipline.NineBall, "2026", [10], 0)).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => leagues.CreateLeagueAsync(_north.Id, "Regional League", Discipline.NineBall)).Should().ThrowAsync<ForbiddenException>();

        // Appointments are read once per request, so the next request sees the new one.
        Appoint(_north, StaffRole.Organiser);
        await new RankingSetup(_rankings, _references, Access).CreateAsync(_northClub.Id, "Club", Discipline.NineBall, "2026", [10], 0);
        await new Leagues.LeagueSetup(new InMemoryLeagues(), _references, Access).CreateLeagueAsync(_northClub.Id, "Club League", Discipline.NineBall);
    }

    [Fact]
    public async Task Only_an_admin_adds_a_top_level_organisation_and_organisers_add_below_their_own()
    {
        var setup = new OrganisationSetup(_organisations, Access);
        await FluentActions.Awaiting(() => setup.CreateAsync("Another Nation", OrganisationLevel.National, null)).Should().ThrowAsync<ForbiddenException>();

        Appoint(_north, StaffRole.Organiser);
        var club = await setup.CreateAsync("Brand New Club", OrganisationLevel.Club, _north.Id);
        await FluentActions.Awaiting(() => setup.CreateAsync("Not Mine", OrganisationLevel.Club, _south.Id)).Should().ThrowAsync<ForbiddenException>();

        var venue = await setup.AddVenueAsync(_northClub.Id, "North Hall", null);
        await setup.AddTableAsync(venue, "Table 1");
        await setup.RenameAsync(_northClub.Id, "Northgate Cue Club");

        _organisations.Organisations.Single(o => o.Id == club).ParentId.Should().Be(_north.Id);
        _organisations.Venues.Single().Tables.Should().ContainSingle(t => t.Label == "Table 1");
        _northClub.Name.Should().Be("Northgate Cue Club");

        var admin = new OrganisationSetup(_organisations, new OrganisationAccess(new FakeUser(), _directory));
        await admin.CreateAsync("Another Nation", OrganisationLevel.National, null);
    }

    [Fact]
    public async Task Organisers_appoint_change_and_remove_staff_at_or_below_their_organisation()
    {
        Appoint(_north, StaffRole.Organiser);
        var colleague = Guid.CreateVersion7();
        _users.ByEmail["colleague@example.test"] = colleague;
        var staff = new StaffAppointments(_organisations, _users, Access);

        await staff.AppointAsync(_northClub.Id, "Colleague@Example.test", StaffRole.Referee);
        await staff.AppointAsync(_northClub.Id, "colleague@example.test", StaffRole.Organiser);
        _organisations.Appointments.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { OrganisationId = _northClub.Id, UserId = colleague, Role = StaffRole.Organiser }, o => o.ExcludingMissingMembers());

        await FluentActions.Awaiting(() => staff.AppointAsync(_southClub.Id, "colleague@example.test", StaffRole.Referee)).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => staff.AppointAsync(_northClub.Id, "nobody@example.test", StaffRole.Referee)).Should().ThrowAsync<NotFoundException>();

        await staff.RemoveAsync(_northClub.Id, colleague);
        _organisations.Appointments.Should().BeEmpty();
        await FluentActions.Awaiting(() => staff.RemoveAsync(_northClub.Id, colleague)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Organisers_add_players_and_manage_memberships_of_their_organisations()
    {
        Appoint(_northClub, StaffRole.Organiser);
        var registry = new PlayerRegistry(_players, Access);

        var id = await registry.CreateAsync(_northClub.Id, "Rowan", "Example", "gb", "LIC-77");
        var player = _players.Players.Single();
        player.OrganisationIds.Should().Equal(_northClub.Id);
        player.Nationality.Should().Be("GB");

        await FluentActions.Awaiting(() => registry.AddMemberAsync(_southClub.Id, id)).Should().ThrowAsync<ForbiddenException>();
        await registry.RemoveMemberAsync(_northClub.Id, id);
        player.OrganisationIds.Should().BeEmpty();
        await FluentActions.Awaiting(() => registry.AddMemberAsync(_northClub.Id, Guid.CreateVersion7())).Should().ThrowAsync<NotFoundException>();
    }
}
