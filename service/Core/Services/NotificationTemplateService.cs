using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class NotificationTemplateService : INotificationTemplateService
{
    private readonly GraphServiceClient _graphClient;

    public NotificationTemplateService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<NotificationMessageTemplate>> ListNotificationTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<NotificationMessageTemplate>();

        var response = await _graphClient.DeviceManagement.NotificationMessageTemplates
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 999;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.NotificationMessageTemplates
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

    public async Task<NotificationMessageTemplate?> GetNotificationTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.NotificationMessageTemplates[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<NotificationMessageTemplate> CreateNotificationTemplateAsync(NotificationMessageTemplate template, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.NotificationMessageTemplates
            .PostAsync(template, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create notification message template");
    }

    public async Task<NotificationMessageTemplate> UpdateNotificationTemplateAsync(NotificationMessageTemplate template, CancellationToken cancellationToken = default)
    {
        var id = template.Id ?? throw new ArgumentException("Notification message template must have an ID for update");

        var result = await _graphClient.DeviceManagement.NotificationMessageTemplates[id]
            .PatchAsync(template, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetNotificationTemplateAsync(id, cancellationToken), "notification message template");
    }

    public async Task DeleteNotificationTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.NotificationMessageTemplates[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }
}
