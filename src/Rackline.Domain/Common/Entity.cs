namespace Rackline.Domain.Common;

/// <summary>Base type for anything with a persistent identity.</summary>
public abstract class Entity
{
    protected Entity()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }
}
