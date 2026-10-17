using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Organisations;

namespace Rackline.Domain.Tests.Organisations;

public class StaffPermissionsTests
{
    private static readonly Organisation World = new("World Cue Body", OrganisationLevel.International);
    private static readonly Organisation Nation = new("Nation Pool Association", OrganisationLevel.National, World);
    private static readonly Organisation North = new("North Region", OrganisationLevel.Regional, Nation);
    private static readonly Organisation South = new("South Region", OrganisationLevel.Regional, Nation);
    private static readonly Organisation NorthClub = new("Northgate Club", OrganisationLevel.Club, North);
    private static readonly Organisation SouthClub = new("Southgate Club", OrganisationLevel.Club, South);

    private static readonly Dictionary<Guid, Guid?> Parents =
        new[] { World, Nation, North, South, NorthClub, SouthClub }.ToDictionary(o => o.Id, o => o.ParentId);

    private static readonly Guid User = Guid.CreateVersion7();

    private static OrganisationLineage LineageOf(Organisation organisation) => new(organisation.Id, Parents);

    [Fact]
    public void A_lineage_runs_from_the_organisation_to_the_top()
    {
        LineageOf(NorthClub).Ids.Should().Equal(NorthClub.Id, North.Id, Nation.Id, World.Id);
        LineageOf(World).Ids.Should().Equal(World.Id);
        LineageOf(NorthClub).Self.Should().Be(NorthClub.Id);
        LineageOf(NorthClub).Includes(South.Id).Should().BeFalse();
    }

    [Fact]
    public void A_lineage_needs_every_organisation_on_the_way_up_and_no_loops()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        FluentActions.Invoking(() => new OrganisationLineage(a, new Dictionary<Guid, Guid?>())).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new OrganisationLineage(a, new Dictionary<Guid, Guid?> { [a] = b })).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new OrganisationLineage(a, new Dictionary<Guid, Guid?> { [a] = b, [b] = a })).Should().Throw<DomainException>();
    }

    [Fact]
    public void An_appointment_covers_its_organisation_and_everything_below_it()
    {
        Appointment[] appointments = [new(North.Id, User, StaffRole.Organiser)];

        StaffPermissions.Allows(appointments, LineageOf(North), StaffRole.Organiser).Should().BeTrue();
        StaffPermissions.Allows(appointments, LineageOf(NorthClub), StaffRole.Organiser).Should().BeTrue();
        StaffPermissions.Allows(appointments, LineageOf(Nation), StaffRole.Organiser).Should().BeFalse("it does not reach upwards");
        StaffPermissions.Allows(appointments, LineageOf(South), StaffRole.Organiser).Should().BeFalse("it does not reach sideways");
        StaffPermissions.Allows(appointments, LineageOf(SouthClub), StaffRole.Referee).Should().BeFalse();
    }

    [Fact]
    public void An_organiser_can_referee_but_a_referee_cannot_organise()
    {
        StaffPermissions.Allows([new Appointment(NorthClub.Id, User, StaffRole.Organiser)], LineageOf(NorthClub), StaffRole.Referee).Should().BeTrue();

        Appointment[] referee = [new(Nation.Id, User, StaffRole.Referee)];
        StaffPermissions.Allows(referee, LineageOf(NorthClub), StaffRole.Referee).Should().BeTrue();
        StaffPermissions.Allows(referee, LineageOf(NorthClub), StaffRole.Organiser).Should().BeFalse();
    }

    [Fact]
    public void Any_one_appointment_is_enough()
    {
        Appointment[] appointments = [new(SouthClub.Id, User, StaffRole.Organiser), new(North.Id, User, StaffRole.Referee)];

        StaffPermissions.Allows(appointments, LineageOf(SouthClub), StaffRole.Organiser).Should().BeTrue();
        StaffPermissions.Allows(appointments, LineageOf(NorthClub), StaffRole.Referee).Should().BeTrue();
        StaffPermissions.Allows(appointments, LineageOf(NorthClub), StaffRole.Organiser).Should().BeFalse();
        StaffPermissions.Allows([], LineageOf(NorthClub), StaffRole.Referee).Should().BeFalse();
    }

    [Fact]
    public void Appointments_need_an_organisation_and_a_user_and_can_change_role()
    {
        FluentActions.Invoking(() => new Appointment(Guid.Empty, User, StaffRole.Referee)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Appointment(North.Id, Guid.Empty, StaffRole.Referee)).Should().Throw<DomainException>();

        var appointment = new Appointment(North.Id, User, StaffRole.Referee);
        appointment.ChangeRole(StaffRole.Organiser);
        appointment.Role.Should().Be(StaffRole.Organiser);
    }

    [Fact]
    public void A_parent_must_sit_above_its_child()
    {
        FluentActions.Invoking(() => new Organisation("Upside Down", OrganisationLevel.National, NorthClub)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Organisation("Sideways", OrganisationLevel.Regional, North)).Should().Throw<DomainException>();
    }
}
