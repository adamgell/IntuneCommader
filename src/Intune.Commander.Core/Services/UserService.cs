using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class UserService(GraphServiceClient graphClient) : IUserService
{
    private readonly GraphServiceClient _graphClient = graphClient;

    private static readonly string[] UserSelect =
        ["id", "displayName", "userPrincipalName", "mail", "jobTitle", "department"];

    public async Task<List<User>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        var result = new List<User>();
        if (string.IsNullOrWhiteSpace(query)) return result;

        var trimmed = query.Trim();

        // If the query looks like a UPN, try a direct lookup first (fast path)
        if (trimmed.Contains('@'))
        {
            try
            {
                var user = await _graphClient.Users[trimmed]
                    .GetAsync(req => req.QueryParameters.Select = UserSelect, cancellationToken);
                if (user != null) result.Add(user);
                return result;
            }
            catch
            {
                // Fall through to search
            }
        }

        // Use $search which works reliably without advanced query constraints.
        // $search on users requires ConsistencyLevel: eventual header.
        var escaped = trimmed.Replace("\"", "\\\"");
        try
        {
            var response = await _graphClient.Users.GetAsync(req =>
            {
                req.QueryParameters.Search = $"\"displayName:{escaped}\"";
                req.QueryParameters.Select = UserSelect;
                req.QueryParameters.Top = 25;
                req.QueryParameters.Orderby = ["displayName"];
                req.Headers.Add("ConsistencyLevel", "eventual");
                req.QueryParameters.Count = true;
            }, cancellationToken);

            if (response?.Value != null)
                result.AddRange(response.Value);
        }
        catch
        {
            // $search may not be available in all tenants; fall back to startsWith $filter
            var escapedFilter = trimmed.Replace("'", "''");
            var fallback = await _graphClient.Users.GetAsync(req =>
            {
                req.QueryParameters.Filter =
                    $"startsWith(displayName,'{escapedFilter}') or startsWith(userPrincipalName,'{escapedFilter}')";
                req.QueryParameters.Select = UserSelect;
                req.QueryParameters.Top = 25;
                req.Headers.Add("ConsistencyLevel", "eventual");
                req.QueryParameters.Count = true;
            }, cancellationToken);

            if (fallback?.Value != null)
                result.AddRange(fallback.Value);
        }

        return result;
    }

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
