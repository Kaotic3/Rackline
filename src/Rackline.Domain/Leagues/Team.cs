using Rackline.Domain.Common;

namespace Rackline.Domain.Leagues;

public sealed class Team : Entity
{
    private readonly List<Guid> _playerIds = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Team()
    {
        Name = null!;
    }

    internal Team(Guid seasonId, string name, Guid? homeVenueId)
    {
        SeasonId = seasonId;
        Name = name;
        HomeVenueId = homeVenueId;
    }

    public Guid SeasonId { get; private set; }

    public string Name { get; private set; }

    public Guid? HomeVenueId { get; private set; }

    /// <summary>The team's registered players. Change them through the season, which checks other teams.</summary>
    public IReadOnlyCollection<Guid> PlayerIds => _playerIds;

    public bool HasPlayer(Guid playerId) => _playerIds.Contains(playerId);

    internal void AddPlayer(Guid playerId)
    {
        if (!_playerIds.Contains(playerId))
        {
            _playerIds.Add(playerId);
        }
    }

    internal void RemovePlayer(Guid playerId) => _playerIds.Remove(playerId);
}
