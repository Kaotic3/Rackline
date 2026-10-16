using Rackline.Domain.Common;

namespace Rackline.Domain.Organisations;

/// <summary>Ordered from the top of the hierarchy downwards.</summary>
public enum OrganisationLevel
{
    International,
    National,
    Regional,
    Club,
}

public sealed class Organisation : Entity
{
    public Organisation(string name, OrganisationLevel level, Organisation? parent = null)
    {
        Name = Guard.NotBlank(name, "Organisation name");
        Level = level;

        if (parent is not null)
        {
            Guard.That(parent.Level < level, "A parent organisation must sit above its child in the hierarchy.");
            ParentId = parent.Id;
        }
    }

    public string Name { get; private set; }

    public OrganisationLevel Level { get; private set; }

    public Guid? ParentId { get; private set; }

    public void Rename(string name) => Name = Guard.NotBlank(name, "Organisation name");
}
