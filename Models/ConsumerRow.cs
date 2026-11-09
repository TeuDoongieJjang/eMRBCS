using CommunityToolkit.Mvvm.ComponentModel;

namespace eMRBCS.Models;

/// Lightweight list item - NOT a view. The CollectionView virtualizes it.
public sealed partial class ConsumerRow : ObservableObject
{
    public Consumers Consumer { get; }

    public ConsumerRow(Consumers c) { Consumer = c; status = c.status; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tag))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    private int status;

    public string AccountNo => Consumer.accountNo ?? "";
    public string MeterNo => Consumer.meterno ?? "";
    public string Name => Consumer.consumer_name ?? "";
    public string Address => Consumer.address ?? "";
    public string Tag => Status == 1 ? "B" : "U";
    public Color StatusColor => Status == 1 ? Colors.Blue : Colors.Red;

    public void Refresh() => Status = Consumer.status;
}