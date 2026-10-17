using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Draws;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Tournaments;

public class TournamentTests
{
    private static readonly Guid OrganisationId = Guid.CreateVersion7();

    private static Tournament NewTournament(TournamentFormat format = TournamentFormat.SingleElimination, FormatOptions? options = null) =>
        new(OrganisationId, "Riverside Winter Open", Discipline.NineBall, format, MatchLength.RaceTo(7), options);

    private static Tournament WithEntries(int count, TournamentFormat format = TournamentFormat.SingleElimination)
    {
        var tournament = NewTournament(format);
        tournament.OpenRegistration();
        for (var i = 0; i < count; i++)
        {
            tournament.AddEntry(Guid.CreateVersion7());
        }

        return tournament;
    }

    [Fact]
    public void A_new_tournament_starts_as_a_draft()
    {
        var tournament = NewTournament();

        tournament.Status.Should().Be(TournamentStatus.Draft);
        tournament.Name.Should().Be("Riverside Winter Open");
        tournament.Options.Should().Be(FormatOptions.Default);
    }

    [Fact]
    public void Requires_a_name_and_organisation()
    {
        FluentActions.Invoking(() => new Tournament(OrganisationId, " ", Discipline.NineBall, TournamentFormat.RoundRobin, RaceTo5))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Tournament(Guid.Empty, "Open", Discipline.NineBall, TournamentFormat.RoundRobin, RaceTo5))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Runs_through_its_whole_lifecycle()
    {
        var tournament = WithEntries(5);
        tournament.MakeDraw(new SeededRandom(1));
        tournament.Status.Should().Be(TournamentStatus.Drawn);
        tournament.Matches.Should().HaveCount(7);

        tournament.Start();
        tournament.Status.Should().Be(TournamentStatus.InProgress);

        while (tournament.Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number) is { } match)
        {
            tournament.StartMatch(match.Number, At);
            tournament.RecordResult(match.Number, 7, 3, At);
        }

        tournament.Complete();
        tournament.Status.Should().Be(TournamentStatus.Completed);
        tournament.Bracket.ChampionEntryId.Should().NotBeNull();
    }

    [Fact]
    public void Entries_are_unique_per_player()
    {
        var tournament = NewTournament();
        var player = Guid.CreateVersion7();
        tournament.AddEntry(player);

        tournament.Invoking(t => t.AddEntry(player)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Seeds_must_be_positive_and_unique()
    {
        var tournament = NewTournament();
        var first = tournament.AddEntry(Guid.CreateVersion7(), seed: 1);

        tournament.Invoking(t => t.AddEntry(Guid.CreateVersion7(), seed: 1)).Should().Throw<DomainException>();
        tournament.Invoking(t => t.AddEntry(Guid.CreateVersion7(), seed: 0)).Should().Throw<DomainException>();

        var second = tournament.AddEntry(Guid.CreateVersion7(), seed: 2);
        tournament.Invoking(t => t.SetSeed(second.Id, 1)).Should().Throw<DomainException>();

        tournament.SetSeed(first.Id, 1);
        tournament.SetSeed(first.Id, null);
        tournament.SetSeed(second.Id, 1);
        second.Seed.Should().Be(1);
    }

    [Fact]
    public void Seeds_cannot_exceed_the_field_at_the_draw()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();
        tournament.AddEntry(Guid.CreateVersion7(), seed: 3);
        tournament.AddEntry(Guid.CreateVersion7());

        tournament.Invoking(t => t.MakeDraw(new SeededRandom(1))).Should().Throw<DomainException>();
    }

    [Fact]
    public void Entries_can_be_withdrawn_and_handicapped_before_the_draw_only()
    {
        var tournament = WithEntries(3);
        var entry = tournament.Entries[0];

        tournament.SetHandicap(entry.Id, 2);
        entry.Handicap.Should().Be(2);

        tournament.WithdrawEntry(tournament.Entries[2].Id);
        tournament.Entries.Should().HaveCount(2);

        tournament.MakeDraw(new SeededRandom(1));
        tournament.Invoking(t => t.WithdrawEntry(entry.Id)).Should().Throw<DomainException>();
        tournament.Invoking(t => t.AddEntry(Guid.CreateVersion7())).Should().Throw<DomainException>();
        tournament.Invoking(t => t.SetHandicap(entry.Id, 0)).Should().Throw<DomainException>();
        tournament.Invoking(t => t.WithdrawEntry(Guid.NewGuid())).Should().Throw<DomainException>();
    }

    [Fact]
    public void The_same_random_seed_gives_the_same_draw()
    {
        var tournament = WithEntries(11);

        tournament.MakeDraw(new SeededRandom(42));
        var first = tournament.Matches.Select(Describe).ToList();
        tournament.UndoDraw();
        tournament.Matches.Should().BeEmpty();
        tournament.Status.Should().Be(TournamentStatus.Registration);

        tournament.MakeDraw(new SeededRandom(42));
        tournament.Matches.Select(Describe).Should().Equal(first);
    }

    [Fact]
    public void Seeded_entries_keep_their_places_whatever_the_random_seed()
    {
        var tournament = NewTournament();
        tournament.OpenRegistration();
        var top = tournament.AddEntry(Guid.CreateVersion7(), seed: 1);
        var second = tournament.AddEntry(Guid.CreateVersion7(), seed: 2);
        for (var i = 0; i < 6; i++)
        {
            tournament.AddEntry(Guid.CreateVersion7());
        }

        foreach (var randomSeed in new[] { 1, 2, 3 })
        {
            tournament.MakeDraw(new SeededRandom(randomSeed));
            tournament.Matches.Single(m => m.Round == 1 && m.Position == 1).Slot1.EntryId.Should().Be(top.Id);
            tournament.Matches.Single(m => m.Round == 1 && m.Position == 3).Slot1.EntryId.Should().Be(second.Id);
            tournament.UndoDraw();
        }
    }

    [Theory]
    [InlineData(TournamentFormat.SingleElimination, 7)]
    [InlineData(TournamentFormat.DoubleElimination, 15)]
    [InlineData(TournamentFormat.RoundRobin, 28)]
    public void Each_supported_format_produces_its_own_draw(TournamentFormat format, int expectedMatches)
    {
        var tournament = WithEntries(8, format);

        tournament.MakeDraw(new SeededRandom(7));

        tournament.Matches.Should().HaveCount(expectedMatches);
        tournament.Matches.Should().OnlyContain(m => m.TournamentId == tournament.Id && m.FramesToWin == 7);
    }

    [Fact]
    public void Format_options_are_passed_to_the_engine()
    {
        var tournament = NewTournament(TournamentFormat.RoundRobin, new FormatOptions(GroupCount: 2));
        tournament.OpenRegistration();
        for (var i = 0; i < 8; i++)
        {
            tournament.AddEntry(Guid.CreateVersion7());
        }

        tournament.MakeDraw(new SeededRandom(7));

        tournament.Matches.Select(m => m.Group).Distinct().Should().BeEquivalentTo([1, 2]);
        tournament.Matches.Should().HaveCount(12);
    }

    [Theory]
    [InlineData(TournamentFormat.GroupsThenKnockout)]
    [InlineData(TournamentFormat.Swiss)]
    public void Formats_not_yet_built_are_refused(TournamentFormat format)
    {
        var tournament = WithEntries(4, format);

        tournament.Invoking(t => t.MakeDraw(new SeededRandom(1))).Should().Throw<DomainException>();
        tournament.Status.Should().Be(TournamentStatus.Registration);
    }

    [Fact]
    public void Status_transitions_are_enforced()
    {
        var tournament = NewTournament();

        tournament.Invoking(t => t.MakeDraw(new SeededRandom(1))).Should().Throw<DomainException>();
        tournament.Invoking(t => t.Start()).Should().Throw<DomainException>();

        tournament.OpenRegistration();
        tournament.Invoking(t => t.OpenRegistration()).Should().Throw<DomainException>();
        tournament.Invoking(t => t.MakeDraw(new SeededRandom(1))).Should().Throw<DomainException>("one entry is not enough");

        tournament.AddEntry(Guid.CreateVersion7());
        tournament.AddEntry(Guid.CreateVersion7());
        tournament.MakeDraw(new SeededRandom(1));
        tournament.Invoking(t => t.RecordResult(1, 7, 0, At)).Should().Throw<DomainException>("not started yet");

        tournament.Start();
        tournament.Invoking(t => t.UndoDraw()).Should().Throw<DomainException>();
        tournament.Invoking(t => t.Complete()).Should().Throw<DomainException>("the final is unplayed");
    }

    [Fact]
    public void Walkovers_are_recorded_through_the_tournament()
    {
        var tournament = WithEntries(2);
        tournament.MakeDraw(new SeededRandom(1));
        tournament.Start();
        var final = tournament.Matches.Single();

        tournament.AwardWalkover(final.Number, final.Slot2.EntryId!.Value, At);

        tournament.Bracket.ChampionEntryId.Should().Be(final.Slot2.EntryId);
        tournament.Complete();
    }

    [Theory]
    [InlineData(TournamentStatus.Draft)]
    [InlineData(TournamentStatus.Registration)]
    [InlineData(TournamentStatus.InProgress)]
    public void Can_be_cancelled_until_it_is_finished(TournamentStatus reached)
    {
        var tournament = WithEntries(2);
        if (reached == TournamentStatus.Draft)
        {
            tournament = NewTournament();
        }
        else if (reached == TournamentStatus.InProgress)
        {
            tournament.MakeDraw(new SeededRandom(1));
            tournament.Start();
        }

        tournament.Cancel();

        tournament.Status.Should().Be(TournamentStatus.Cancelled);
        tournament.Invoking(t => t.Cancel()).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_completed_tournament_cannot_be_cancelled()
    {
        var tournament = WithEntries(2);
        tournament.MakeDraw(new SeededRandom(1));
        tournament.Start();
        tournament.RecordResult(1, 7, 6, At);
        tournament.Complete();

        tournament.Invoking(t => t.Cancel()).Should().Throw<DomainException>();
    }

    [Fact]
    public void Can_be_reconfigured_only_before_the_draw()
    {
        var tournament = WithEntries(4);

        tournament.Reconfigure(Discipline.Snooker, TournamentFormat.RoundRobin, MatchLength.BestOf(5), new FormatOptions(GroupCount: 2));

        tournament.Should().BeEquivalentTo(new
        {
            Discipline = Discipline.Snooker,
            Format = TournamentFormat.RoundRobin,
            MatchLength = MatchLength.BestOf(5),
            Options = new FormatOptions(GroupCount: 2),
        }, o => o.ExcludingMissingMembers());

        tournament.Reconfigure(Discipline.NineBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(7));
        tournament.Options.Should().Be(FormatOptions.Default);

        tournament.MakeDraw(new SeededRandom(1));
        tournament.Invoking(t => t.Reconfigure(Discipline.NineBall, TournamentFormat.RoundRobin, RaceTo5))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Can_be_deleted_only_when_no_draw_is_in_play()
    {
        var tournament = WithEntries(2);
        tournament.CanBeDeleted.Should().BeTrue();

        tournament.MakeDraw(new SeededRandom(1));
        tournament.CanBeDeleted.Should().BeFalse();

        tournament.Start();
        tournament.CanBeDeleted.Should().BeFalse();

        tournament.Cancel();
        tournament.CanBeDeleted.Should().BeTrue();
        NewTournament().CanBeDeleted.Should().BeTrue();
    }
}
