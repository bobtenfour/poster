using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class InstitutionalGroupNameTests
{
    [Theory]
    [InlineData("GroupPCAdmins", "GroupPCAdmins", true)]
    [InlineData(@"UNIVERSITY\GroupPCAdmins", "GroupPCAdmins", true)]
    [InlineData(@"UNIVERSITY\grouppcadmins", "GroupPCAdmins", true)]
    [InlineData("DEN-DentistryUsers", "DEN-DentistryUsers", true)]
    [InlineData(@"COLLEGE\DEN-DentistryUsers", "DEN-DentistryUsers", true)]
    [InlineData("GroupPCAdmins", "DEN-DentistryUsers", false)]
    [InlineData(@"UNIVERSITY\DEN-DentistryUsers", "GroupPCAdmins", false)]
    [InlineData("GroupPCAdminsExtra", "GroupPCAdmins", false)]
    [InlineData(@"UNIVERSITY\GroupPCAdmins\nested", "GroupPCAdmins", false)]
    [InlineData("", "GroupPCAdmins", false)]
    [InlineData("GroupPCAdmins", "", false)]
    public void Group_match_uses_the_account_name_or_its_leaf(string translated, string group, bool expected)
    {
        Assert.Equal(expected, InstitutionalGroupNames.Matches(translated, group));
    }

    [Fact]
    public void Actor_requires_a_sid_and_does_not_accept_a_display_name_alone()
    {
        Assert.Null(InstitutionalActor.Create(null, "Ada Lovelace"));
        Assert.Null(InstitutionalActor.Create("Ada Lovelace", "Ada Lovelace"));
        Assert.Null(InstitutionalActor.Create("S-1-5-21-1-2-3-4", " "));
        var actor = InstitutionalActor.Create("S-1-5-21-1-2-3-4", @" UNIVERSITY\ada ");
        Assert.NotNull(actor);
        Assert.Equal("S-1-5-21-1-2-3-4", actor.SecurityIdentifier);
        Assert.Equal(@"UNIVERSITY\ada", actor.AccountName);
        Assert.True(actor.IsValid);
    }
}
