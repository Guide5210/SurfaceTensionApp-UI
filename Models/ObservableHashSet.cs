using System.Collections;
using System.Collections.Specialized;

namespace SurfaceTensionApp.Models;

/// <summary>
/// A set with O(1) Contains that implements INotifyCollectionChanged
/// so the UI refreshes automatically when items are added or removed.
/// Used for ManualOutlierIndices in SpeedGroup.
/// </summary>
public class ObservableHashSet<T> : IReadOnlyCollection<T>, INotifyCollectionChanged
{
    private readonly HashSet<T> _inner = new();

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _inner.Count;

    public bool Add(T item)
    {
        if (!_inner.Add(item)) return false;
        Raise(NotifyCollectionChangedAction.Add, item);
        return true;
    }

    public bool Remove(T item)
    {
        if (!_inner.Remove(item)) return false;
        Raise(NotifyCollectionChangedAction.Remove, item);
        return true;
    }

    public bool Contains(T item) => _inner.Contains(item);

    public void Clear()
    {
        _inner.Clear();
        CollectionChanged?.Invoke(this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _inner.GetEnumerator();

    private void Raise(NotifyCollectionChangedAction action, T item) =>
        CollectionChanged?.Invoke(this,
            new NotifyCollectionChangedEventArgs(action, item));
}
