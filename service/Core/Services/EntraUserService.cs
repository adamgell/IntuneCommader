using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class EntraUserService(GraphServiceClient graphClient) : IEntraUserService
{
    private readonly GraphServiceClient _graphClient = graphClient;

    private static readonly string[] UserSelect =
    [
        "id", "displayName", "givenName", "surname", "mail", "userPrincipalName", "mailNickname",
        "department", "jobTitle", "officeLocation", "usageLocation", "mobilePhone",
        "businessPhones", "city", "state", "country", "companyName", "employeeId",
        "accountEnabled", "onPremisesSamAccountName", "onPremisesDomainName", "onPremisesImmutableId",
        "onPremisesSyncEnabled", "onPremisesDistinguishedName", "onPremisesUserPrincipalName",
        "onPremisesExtensionAttributes", "assignedLicenses", "assignedPlans", "createdDateTime",
        "lastPasswordChangeDateTime", "preferredLanguage"
    ];

    public async Task<List<User>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<User>();

        var response = await _graphClient.Users.GetAsync(req =>
        {
            req.QueryParameters.Top = 999;
            req.QueryParameters.Select = UserSelect;
        }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.Users
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }
}
