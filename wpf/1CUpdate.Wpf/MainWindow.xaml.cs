using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OneCUpdate.Common;
using OneCUpdate.ViewModels;
using OneCUpdate.Views;

namespace OneCUpdate;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _scrollTimer;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        WindowChromeHelper.EnableDarkTitleBar(this);

        PasswordBox.PasswordChanged += (s, e) => _vm.Password = PasswordBox.Password;

        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _scrollTimer.Tick += (s, e) =>
        {
            _scrollTimer.Stop();
            if (LogList.Items.Count == 0) return;
            try { LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]); } catch { }
        };

        ((INotifyCollectionChanged)_vm.LogEntries).CollectionChanged += OnLogChanged;

        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        _scrollTimer.Stop();
        _scrollTimer.Start();
    }

    private void OnLoadFromAd(object sender, RoutedEventArgs e)
    {
        // в рабочей группе UserDomainName возвращает имя ПК — подставлять его как домен нельзя
        string domain = Environment.UserDomainName;
        if (string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) domain = "";

        var dlg = new AdDialog(domain) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _vm.AddComputers(dlg.Selected);
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_vm.IsRunning)
        {
            var result = MessageBox.Show(this, "Обновление ещё выполняется. Остановить и выйти?",
                "1C Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        _vm.SaveSettings();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            if (_vm.RunCommand.CanExecute(null)) _vm.RunCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F6)
        {
            if (_vm.StopCommand.CanExecute(null)) _vm.StopCommand.Execute(null);
            e.Handled = true;
        }
    }
}
