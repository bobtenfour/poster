using System.Security.Claims;
using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Requests;

/// <summary>Authentication Contract section 3.9: the requester's name and email come from the token.</summary>
public sealed class RequesterContactTests
{
    [Fact]
    public void Name_comes_from_the_name_claim_and_email_from_preferred_username()
    {
        var contact = RequesterContact.FromPrincipal(Principal(("name", " Ada Lovelace "), ("preferred_username", "ada@uic.edu")));

        Assert.Equal(new RequesterContact("Ada Lovelace", "ada@uic.edu"), contact);
    }

    [Theory]
    [InlineData(null, "ada@uic.edu")]
    [InlineData("   ", "ada@uic.edu")]
    [InlineData("Ada Lovelace", null)]
    [InlineData("Ada Lovelace", "ada")]
    [InlineData("Ada Lovelace", "ada@uic")]
    [InlineData("Ada Lovelace", "ada lovelace@uic.edu")]
    [InlineData("Ada Lovelace", "ada@@uic.edu")]
    [InlineData("Ada\u0007Lovelace", "ada@uic.edu")]
    public void A_missing_or_unusable_claim_gives_no_contact(string? name, string? email)
    {
        var claims = new List<(string, string)>();
        if (name is not null)
        {
            claims.Add(("name", name));
        }

        if (email is not null)
        {
            claims.Add(("preferred_username", email));
        }

        Assert.Null(RequesterContact.FromPrincipal(Principal(claims.ToArray())));
    }

    [Fact]
    public void An_anonymous_principal_gives_no_contact()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "Ada"), new Claim("preferred_username", "ada@uic.edu")]));

        Assert.Null(RequesterContact.FromPrincipal(anonymous));
    }

    [Fact]
    public void A_value_longer_than_the_limit_gives_no_contact()
    {
        var longName = new string('a', RequesterContact.MaxLength + 1);

        Assert.Null(RequesterContact.FromPrincipal(Principal(("name", longName), ("preferred_username", "ada@uic.edu"))));
    }

    // Interactive pages (Request, ReplacePoster) sit under a static Routes component: the sign-in state reaches
    // their circuits only as a root-level cascading value.
    [Fact]
    public void The_application_provides_the_sign_in_state_to_interactive_pages()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryPaths.WebProjectDirectory(), "Program.cs"));

        Assert.Contains("builder.Services.AddCascadingAuthenticationState();", program, StringComparison.Ordinal);
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "Test", "name", "roles"));
}
