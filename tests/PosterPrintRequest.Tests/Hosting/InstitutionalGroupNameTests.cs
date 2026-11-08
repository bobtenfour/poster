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
}
