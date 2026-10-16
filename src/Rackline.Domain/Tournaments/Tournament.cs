using Rackline.Domain.Common;
using Rackline.Domain.Draws;
using Rackline.Domain.Matches;

namespace Rackline.Domain.Tournaments;

public sealed class Tournament : Entity
{
    private readonly List<Entry> _entries = [];
    private readonly List<Match> _matches = [];

    public Tournament(
        Guid organisationId,
        string name,
        Discipline discipline,
        TournamentFormat format,
        MatchLength matchLength,
        FormatOptions? options = null)
    {
        OrganisationId = Guard.NotEmpty(organisationId, "Organisation");
        Name = Guard.NotBlank(name, "Tournament name");
        Discipline = discipline;
        Format = format;
        MatchLength = matchLength;
        Options = options ?? FormatOptions.Default;
    }

    public Guid OrganisationId { get; private set; }

    public string Name { get; private set; }

    public Discipline Discipline { get; private set; }

    public TournamentFormat Format { get; private set; }

    public MatchLength MatchLength { get; private set; }

    public FormatOptions Options { get; private set; }

    public TournamentStatus Status { get; private set; } = TournamentStatus.Draft;

    public IReadOnlyList<Entry> Entries => _entries;

    public IReadOnlyList<Match> Matches => _matches;

    public Bracket Bracket => new(_matches);

    public void Rename(string name) => Name = Guard.NotBlank(name, "Tournament name");

    public void OpenRegistration()
    {
        RequireStatus(TournamentStatus.Draft);
        Status = TournamentStatus.Registration;
    }

    public Entry AddEntry(Guid playerId, int? seed = null, int handicap = 0)
    {
        RequireStatus(TournamentStatus.Draft, TournamentStatus.Registration);
        Guard.That(_entries.All(e => e.PlayerId != playerId), "That player has already entered.");
        ValidateSeed(seed, exceptEntryId: null);

        var entry = new Entry(Id, playerId, seed, handicap);
        _entries.Add(entry);
        return entry;
    }

    public void WithdrawEntry(Guid entryId)
    {
        RequireStatus(TournamentStatus.Draft, TournamentStatus.Registration);
        _entries.Remove(FindEntry(entryId));
    }

    public void SetSeed(Guid entryId, int? seed)
    {
        RequireStatus(TournamentStatus.Draft, TournamentStatus.Registration);
        var entry = FindEntry(entryId);
        ValidateSeed(seed, entryId);
        entry.SetSeed(seed);
    }

    public void SetHandicap(Guid entryId, int handicap)
    {
        RequireStatus(TournamentStatus.Draft, TournamentStatus.Registration);
        FindEntry(entryId).SetHandicap(handicap);
    }

    /// <summary>Generates every match for the tournament. The same random seed gives the same draw.</summary>
    public void MakeDraw(IRandom random)
    {
        RequireStatus(TournamentStatus.Registration);
        Guard.That(_entries.Count >= 2, "At least two entries are needed for a draw.");
        Guard.That(
            _entries.All(e => e.Seed is null || e.Seed <= _entries.Count),
            "No seed may be higher than the number of entries.");

        var order = SeedingOrder.Arrange(_entries, random);
        _matches.AddRange(Options.CreateEngine(Format).Generate(Id, order, MatchLength));
        Status = TournamentStatus.Drawn;
    }

    public void UndoDraw()
    {
        RequireStatus(TournamentStatus.Drawn);
        _matches.Clear();
        Status = TournamentStatus.Registration;
    }

    public void Start()
    {
        RequireStatus(TournamentStatus.Drawn);
        Status = TournamentStatus.InProgress;
    }

    public void StartMatch(int matchNumber, DateTimeOffset at, Guid? tableId = null)
    {
        RequireStatus(TournamentStatus.InProgress);
        Bracket[matchNumber].Start(at, tableId);
    }

    public void RecordResult(int matchNumber, int score1, int score2, DateTimeOffset at)
    {
        RequireStatus(TournamentStatus.InProgress);
        Bracket.RecordResult(matchNumber, score1, score2, at);
    }

    public void AwardWalkover(int matchNumber, Guid winnerEntryId, DateTimeOffset at)
    {
        RequireStatus(TournamentStatus.InProgress);
        Bracket.AwardWalkover(matchNumber, winnerEntryId, at);
    }

    public void Complete()
    {
        RequireStatus(TournamentStatus.InProgress);
        Guard.That(Bracket.IsFinished, "Every match must be finished before the tournament is completed.");
        Status = TournamentStatus.Completed;
    }

    public void Cancel()
    {
        Guard.That(
            Status is not (TournamentStatus.Completed or TournamentStatus.Cancelled),
            $"A {Status.ToString().ToLowerInvariant()} tournament cannot be cancelled.");
        Status = TournamentStatus.Cancelled;
    }

    private Entry FindEntry(Guid entryId) =>
        _entries.SingleOrDefault(e => e.Id == entryId) ?? throw new DomainException("Entry not found.");

    private void ValidateSeed(int? seed, Guid? exceptEntryId)
    {
        if (seed is null)
        {
            return;
        }

        Guard.Positive(seed.Value, "Seed");
        Guard.That(
            _entries.All(e => e.Id == exceptEntryId || e.Seed != seed),
            $"Seed {seed} is already taken.");
    }

    private void RequireStatus(params TournamentStatus[] allowed) =>
        Guard.That(allowed.Contains(Status), $"Not allowed while the tournament is {Status}.");
}
