using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface INotificationTemplateService
{
    Task<List<NotificationMessageTemplate>> ListNotificationTemplatesAsync(CancellationToken cancellationToken = default);
    Task<NotificationMessageTemplate?> GetNotificationTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<NotificationMessageTemplate> CreateNotificationTemplateAsync(NotificationMessageTemplate template, CancellationToken cancellationToken = default);
    Task<NotificationMessageTemplate> UpdateNotificationTemplateAsync(NotificationMessageTemplate template, CancellationToken cancellationToken = default);
    Task DeleteNotificationTemplateAsync(string id, CancellationToken cancellationToken = default);
}
