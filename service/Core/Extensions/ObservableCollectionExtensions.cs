using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Intune.Commander.Core.Extensions;

public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Replaces all items in an <see cref="ObservableCollection{T}"/> in-place.
    /// Triggers one <see cref="System.Collections.Specialized.NotifyCollectionChangedAction.Reset"/> CollectionChanged event (from Clear)
    /// followed by N <see cref="System.Collections.Specialized.NotifyCollectionChangedAction.Add"/> CollectionChanged events.
    /// Use this to reuse an already-bound collection instance rather than replacing the reference.
    /// </summary>
    public static void ReplaceAll<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
