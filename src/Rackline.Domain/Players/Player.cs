using Rackline.Domain.Common;

namespace Rackline.Domain.Players;

public sealed class Player : Entity
{
    private readonly HashSet<Guid> _organisationIds = [];

    public Player(string firstName, string lastName, string nationality, string? licenceNumber = null)
    {
        FirstName = Guard.NotBlank(firstName, "First name");
        LastName = Guard.NotBlank(lastName, "Last name");
        Nationality = NormaliseNationality(nationality);
        LicenceNumber = string.IsNullOrWhiteSpace(licenceNumber) ? null : licenceNumber.Trim();
    }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string DisplayName => $"{FirstName} {LastName}";

    /// <summary>ISO 3166-1 alpha-2 country code, upper case.</summary>
    public string Nationality { get; private set; }

    public string? LicenceNumber { get; private set; }

    public IReadOnlyCollection<Guid> OrganisationIds => _organisationIds;

    public void JoinOrganisation(Guid organisationId) =>
        _organisationIds.Add(Guard.NotEmpty(organisationId, "Organisation"));

    public void LeaveOrganisation(Guid organisationId) => _organisationIds.Remove(organisationId);

    private static string NormaliseNationality(string code)
    {
        code = Guard.NotBlank(code, "Nationality").ToUpperInvariant();
        Guard.That(code.Length == 2 && code.All(char.IsAsciiLetterUpper), "Nationality must be a two-letter ISO country code.");
        return code;
    }
}
