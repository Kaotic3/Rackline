using FluentAssertions;
using Rackline.Web.Components.Shared;

namespace Rackline.Web.Tests;

public class LabelsTests
{
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(102, "102nd")]
    [InlineData(111, "111th")]
    public void Ordinals_use_the_right_suffix(int number, string expected) => Labels.Ordinal(number).Should().Be(expected);

    [Theory]
    [InlineData(15.6, "+16")]
    [InlineData(-7.5, "−8")]
    [InlineData(0.4, "0")]
    [InlineData(-0.4, "0")]
    public void Rating_changes_show_their_sign(double change, string expected) => Labels.RatingChange(change).Should().Be(expected);
}
