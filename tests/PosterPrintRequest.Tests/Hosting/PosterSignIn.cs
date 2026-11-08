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
        client.DefaultRequestHeaders.Add(InstitutionalTestHandler.OidHeader, identity.Oid.ToString());
        client.DefaultRequestHeaders.Add(InstitutionalTestHandler.UsernameHeader, identity.Account);
        client.DefaultRequestHeaders.Add(InstitutionalTestHandler.SidHeader, identity.Sid);
        if (!string.IsNullOrEmpty(identity.Roles))
        {
            client.DefaultRequestHeaders.Add(InstitutionalTestHandler.RolesHeader, identity.Roles);
        }
    }

    private static (Guid Oid, string Account, string Sid, string Roles) Resolve(string userName) =>
        userName switch
        {
            "usera" => (
                InstitutionalTestIdentities.RequesterOid,
                InstitutionalTestIdentities.RequesterAccount,
                InstitutionalTestIdentities.RequesterSid,
                PosterRoles.Requester),
            "usero" => (
                InstitutionalTestIdentities.TechnicianOid,
                InstitutionalTestIdentities.TechnicianAccount,
                InstitutionalTestIdentities.TechnicianSid,
                PosterRoles.Technician),
            "both" => (
                InstitutionalTestIdentities.BothOid,
                InstitutionalTestIdentities.BothAccount,
                InstitutionalTestIdentities.BothSid,
                PosterRoles.Requester + "," + PosterRoles.Technician),
            "neither" => (
                InstitutionalTestIdentities.NeitherOid,
                InstitutionalTestIdentities.NeitherAccount,
                InstitutionalTestIdentities.NeitherSid,
                ""),
            _ => throw new ArgumentException("Unknown test identity " + userName, nameof(userName))
        };
}
