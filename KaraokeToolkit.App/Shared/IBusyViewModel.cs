using System.ComponentModel;

namespace KaraokeToolkit.App.Shared;

internal interface IBusyViewModel : INotifyPropertyChanged
{
    bool IsBusy { get; }
}
