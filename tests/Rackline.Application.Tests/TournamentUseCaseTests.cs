using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Application.Tournaments;
using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tests;

public class TournamentUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 19, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTournaments _tournaments = new();
    private readonly FakeReferences _references = new();
    private readonly FixedTime _time = new(Now);
    private readonly RecordingNotifier _notifier = new();
    private readonly Guid _organisationId = Guid.CreateVersion7();

    private readonly TournamentLifecycle _lifecycle;
    private readonly TournamentEntries _entries;
    private readonly TournamentDraws _draws;
    private readonly MatchResults _results;

    public TournamentUseCaseTests()
    {
        _references.Organisations.Add(_organisationId);
        _lifecycle = new TournamentLifecycle(_tournaments, _references, _notifier);
        _entries = new TournamentEntries(_tournaments, _references, _notifier);
        _draws = new TournamentDraws(_tournaments, _notifier);
        _results = new MatchResults(_tournaments, _references, _notifier, _time);
    }

    private static TournamentSettings Settings(TournamentFormat format = TournamentFormat.SingleElimination) =>
        new("Midweek Nine-Ball", Discipline.NineBall, format, MatchLengthKind.RaceTo, 5);

    private Tournament Saved(Guid id) => _tournaments.Saved.Single(t => t.Id == id);

    private async Task<Guid> TournamentWithPlayersAsync(int players)
    {
        var id = await _lifecycle.CreateAsync(_organisationId, Settings());
        await _lifecycle.OpenRegistrationAsync(id);
        for (var i = 0; i < players; i++)
        {
            var player = Guid.CreateVersion7();
            _references.Players.Add(player);
            await _entries.AddAsync(id, player, seed: null, handicap: 0);
        }

        return id;
    }

    [Fact]
    public async Task Creating_saves_a_draft_tournament_with_the_chosen_settings()
    {
        var id = await _lifecycle.CreateAsync(_organisationId,
            new TournamentSettings("Spring Snooker", Discipline.Snooker, TournamentFormat.DoubleElimination, MatchLengthKind.BestOf, 7, GrandFinalReset: false));

        Saved(id).Should().BeEquivalentTo(new
        {
            Name = "Spring Snooker",
            Status = TournamentStatus.Draft,
            OrganisationId = _organisationId,
            MatchLength = MatchLength.BestOf(7),
            Options = new FormatOptions(GrandFinalReset: false),
        }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task Creating_for_an_unknown_organisation_is_not_found()
    {
        var act = () => _lifecycle.CreateAsync(Guid.CreateVersion7(), Settings());

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Organisation");
        _tournaments.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_tournaments_are_not_found_by_every_command()
    {
        var missing = Guid.CreateVersion7();

        Func<Task>[] commands =
        [
            () => _lifecycle.UpdateAsync(missing, Settings()),
            () => _lifecycle.DeleteAsync(missing),
            () => _lifecycle.OpenRegistrationAsync(missing),
            () => _lifecycle.StartAsync(missing),
            () => _lifecycle.CompleteAsync(missing),
            () => _lifecycle.CancelAsync(missing),
            () => _entries.AddAsync(missing, Guid.CreateVersion7(), null, 0),
            () => _draws.MakeAsync(missing, 1),
            () => _draws.UndoAsync(missing),
            () => _results.StartAsync(missing, 1, null),
            () => _results.RecordResultAsync(missing, 1, 5, 0),
            () => _results.AwardWalkoverAsync(missing, 1, Guid.CreateVersion7()),
        ];

        foreach (var command in commands)
        {
            (await command.Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Tournament");
        }
    }

    [Fact]
    public async Task Updating_renames_and_reconfigures()
    {
        var id = await _lifecycle.CreateAsync(_organisationId, Settings());

        await _lifecycle.UpdateAsync(id, Settings(TournamentFormat.RoundRobin) with { Name = "Renamed", GroupCount = 2 });

        Saved(id).Should().BeEquivalentTo(
            new { Name = "Renamed", Format = TournamentFormat.RoundRobin, Options = new FormatOptions(GroupCount: 2) },
            o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task Entries_need_a_known_player_and_entry()
    {
        var id = await TournamentWithPlayersAsync(1);
        var entryId = Saved(id).Entries[0].Id;

        (await FluentActions.Awaiting(() => _entries.AddAsync(id, Guid.CreateVersion7(), null, 0))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Player");
        (await FluentActions.Awaiting(() => _entries.UpdateAsync(id, Guid.CreateVersion7(), 1, 0))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Entry");
        (await FluentActions.Awaiting(() => _entries.WithdrawAsync(id, Guid.CreateVersion7()))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Entry");

        await _entries.UpdateAsync(id, entryId, seed: 1, handicap: 2);
        Saved(id).Entries[0].Should().BeEquivalentTo(new { Seed = 1, Handicap = 2 });

        await _entries.WithdrawAsync(id, entryId);
        Saved(id).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task The_draw_reports_its_seed_and_can_be_reproduced()
    {
        var id = await TournamentWithPlayersAsync(9);

        var chosen = await _draws.MakeAsync(id, randomSeed: null);
        var first = Saved(id).Matches.Select(m => $"{m.Number}:{m.Slot1}:{m.Slot2}").ToList();
        await _draws.UndoAsync(id);
        Saved(id).Status.Should().Be(TournamentStatus.Registration);

        (await _draws.MakeAsync(id, chosen)).Should().Be(chosen);
        Saved(id).Matches.Select(m => $"{m.Number}:{m.Slot1}:{m.Slot2}").Should().Equal(first);
    }

    [Fact]
    public async Task Match_results_use_the_clock_and_advance_the_bracket()
    {
        var id = await TournamentWithPlayersAsync(4);
        await _draws.MakeAsync(id, 5);
        await _lifecycle.StartAsync(id);
        var table = Guid.CreateVersion7();
        _references.Tables.Add(table);

        await _results.StartAsync(id, 1, table);
        _time.Now = Now.AddMinutes(45);
        await _results.RecordResultAsync(id, 1, 5, 3);
        await _results.AwardWalkoverAsync(id, 2, Saved(id).Matches.Single(m => m.Number == 2).Slot2.EntryId!.Value);

        var matches = Saved(id).Matches.ToDictionary(m => m.Number);
        matches[1].Should().BeEquivalentTo(new { StartedAt = Now, CompletedAt = Now.AddMinutes(45), TableId = table, State = MatchState.Completed });
        matches[2].State.Should().Be(MatchState.Walkover);
        matches[3].State.Should().Be(MatchState.Ready);

        await _results.RecordResultAsync(id, 3, 2, 5);
        await _lifecycle.CompleteAsync(id);
        Saved(id).Status.Should().Be(TournamentStatus.Completed);
    }

    [Fact]
    public async Task Unknown_matches_and_tables_are_not_found()
    {
        var id = await TournamentWithPlayersAsync(2);
        await _draws.MakeAsync(id, 1);
        await _lifecycle.StartAsync(id);

        (await FluentActions.Awaiting(() => _results.RecordResultAsync(id, 99, 5, 0))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Match");
        (await FluentActions.Awaiting(() => _results.StartAsync(id, 1, Guid.CreateVersion7()))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Table");
    }

    [Fact]
    public async Task Domain_rules_surface_as_domain_exceptions()
    {
        var id = await TournamentWithPlayersAsync(2);

        await FluentActions.Awaiting(() => _lifecycle.StartAsync(id)).Should().ThrowAsync<DomainException>("there is no draw yet");
        await FluentActions.Awaiting(() => _lifecycle.CompleteAsync(id)).Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Only_tournaments_without_a_live_draw_can_be_deleted()
    {
        var id = await TournamentWithPlayersAsync(2);
        await _draws.MakeAsync(id, 1);

        await FluentActions.Awaiting(() => _lifecycle.DeleteAsync(id)).Should().ThrowAsync<DomainException>();

        await _lifecycle.CancelAsync(id);
        await _lifecycle.DeleteAsync(id);
        _tournaments.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Conflicts_from_saving_are_passed_on()
    {
        var id = await _lifecycle.CreateAsync(_organisationId, Settings());
        _tournaments.FailNextSave = true;

        await FluentActions.Awaiting(() => _lifecycle.OpenRegistrationAsync(id)).Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Status_draw_and_entry_changes_are_announced_after_saving()
    {
        var id = await TournamentWithPlayersAsync(2);
        await _draws.MakeAsync(id, 1);
        await _lifecycle.StartAsync(id);

        _notifier.Statuses.Should().Equal(
            TournamentStatus.Registration,
            TournamentStatus.Registration,
            TournamentStatus.Registration,
            TournamentStatus.Drawn,
            TournamentStatus.InProgress);
    }

    [Fact]
    public async Task Nothing_is_announced_when_saving_fails()
    {
        var id = await _lifecycle.CreateAsync(_organisationId, Settings());
        _tournaments.FailNextSave = true;

        await FluentActions.Awaiting(() => _lifecycle.OpenRegistrationAsync(id)).Should().ThrowAsync<ConflictException>();
        _notifier.Statuses.Should().BeEmpty();
    }

    [Fact]
    public async Task A_result_announces_the_match_and_the_match_it_fed()
    {
        var id = await TournamentWithPlayersAsync(4);
        await _draws.MakeAsync(id, 5);
        await _lifecycle.StartAsync(id);

        await _results.RecordResultAsync(id, 1, 5, 0);

        _notifier.MatchBatches.Should().ContainSingle()
            .Which.Select(m => m.Number).Should().Equal(1, 3);
    }
}
