using Microsoft.AspNetCore.Mvc.Testing;
using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Hosting;

internal static class PosterSignIn
{
    public static Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string userName) =>
        SignInAsync(factory, userName, allowAutoRedirect: true);

    public static Task<HttpClient> SignInAsync(
        WebApplicationFactory<Program> factory,
        string userName,
        bool allowAutoRedirect)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = true
        });
        Apply(client, userName);
        return Task.FromResult(client);
    }

    public static void Apply(HttpClient client, string userName)
    {
        var identity = Resolve(userName);
        client.DefaultRequestHeaders.Add(InstitutionalTestHandler.AccountHeader, identity.Account);
        client.DefaultRequestHeaders.Add(InstitutionalTestHandler.SidHeader, identity.Sid);
        if (!string.IsNullOrEmpty(identity.Groups))
        {
            client.DefaultRequestHeaders.Add(InstitutionalTestHandler.GroupsHeader, identity.Groups);
        }
    }

    private static (string Account, string Sid, string Groups) Resolve(string userName) =>
        userName switch
        {
            "usera" => (InstitutionalTestIdentities.RequesterAccount, InstitutionalTestIdentities.RequesterSid, InstitutionalGroups.Requesters),
            "usero" => (InstitutionalTestIdentities.TechnicianAccount, InstitutionalTestIdentities.TechnicianSid, InstitutionalGroups.Technicians),
            "both" => (
                InstitutionalTestIdentities.BothAccount,
                InstitutionalTestIdentities.BothSid,
                InstitutionalGroups.Requesters + "," + InstitutionalGroups.Technicians),
            "neither" => (InstitutionalTestIdentities.NeitherAccount, InstitutionalTestIdentities.NeitherSid, ""),
            _ => throw new ArgumentException("Unknown test identity " + userName, nameof(userName))
        };
}
