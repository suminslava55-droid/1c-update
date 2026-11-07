using System.Collections.ObjectModel;
using System.DirectoryServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OneCUpdate.Common;

namespace OneCUpdate.Views;

public sealed class AdComputer : ObservableObject
{
    private bool _isChecked;
    public string Name { get; set; }
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

public partial class AdDialog : Window
{
    private readonly ObservableCollection<AdComputer> _computers = new ObservableCollection<AdComputer>();
    private bool _ready;
    private bool _treeLoading;

    public List<string> Selected { get; } = new List<string>();

    public AdDialog(string initialDomain)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);

        DomainBox.Text = initialDomain ?? "";
        ComputerList.ItemsSource = _computers;
        _ready = true;

        Loaded += (s, e) => LoadTree();
    }

    // ===================== дерево =====================

    private sealed class NodeInfo
    {
        public string Title;
        public string Path;
    }

    private sealed class ChildInfo
    {
        public string Name;
        public string Path;
    }

    private void OnLoadTree(object sender, RoutedEventArgs e) => LoadTree();

    private async void LoadTree()
    {
        if (_treeLoading) return;
        _treeLoading = true;

        Tree.Items.Clear();
        _computers.Clear();
        CountText.Text = "";
        StatusText.Text = "Подключение к домену…";
        Cursor = Cursors.Wait;

        try
        {
            string domain = DomainBox.Text.Trim();

            // LDAP в фоне: контроллер домена может отвечать долго
            NodeInfo info = await Task.Run(() => OpenRootInfo(domain));

            var item = new TreeViewItem { Header = info.Title, Tag = info.Path };
            item.Items.Add(new TreeViewItem { Header = "…" });
            item.Expanded += OnItemExpanded;
            Tree.Items.Add(item);
            item.IsExpanded = true;
            item.IsSelected = true;

            StatusText.Text = "Дерево загружено. Выберите подразделение слева.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка: " + Friendly(ex.Message);
        }
        finally
        {
            Cursor = Cursors.Arrow;
            _treeLoading = false;
        }
    }

    private static NodeInfo OpenRootInfo(string domain)
    {
        string error;
        DirectoryEntry root = OpenRoot(domain, out error);
        if (root == null)
            throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "не удалось подключиться к домену" : error);

        using (root)
        {
            root.RefreshCache();
            return new NodeInfo
            {
                Title = string.IsNullOrEmpty(domain) ? root.Path : domain,
                Path = root.Path
            };
        }
    }

    private void OnItemExpanded(object sender, RoutedEventArgs e)
    {
        var item = sender as TreeViewItem;
        if (item == null) return;
        FillNode(item);
        e.Handled = true;
    }

    private async void FillNode(TreeViewItem item)
    {
        if (item.Tag == null) return;
        if (!(item.Items.Count == 1 && item.Items[0] is TreeViewItem &&
              Convert.ToString(((TreeViewItem)item.Items[0]).Header) == "…")) return;

        item.Items.Clear();
        try
        {
            string path = Convert.ToString(item.Tag);
            List<ChildInfo> children = await Task.Run(() => ReadChildren(path));

            foreach (ChildInfo info in children)
            {
                var child = new TreeViewItem { Header = info.Name, Tag = info.Path };
                child.Items.Add(new TreeViewItem { Header = "…" });
                child.Expanded += OnItemExpanded;
                item.Items.Add(child);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось прочитать структуру: " + Friendly(ex.Message);
        }
    }

    private static List<ChildInfo> ReadChildren(string path)
    {
        var result = new List<ChildInfo>();
        using (var entry = new DirectoryEntry(path))
        using (var searcher = new DirectorySearcher(entry))
        {
            searcher.Filter = "(|(objectClass=organizationalUnit)(objectClass=container)(objectClass=builtinDomain))";
            searcher.SearchScope = SearchScope.OneLevel;
            searcher.PageSize = 200;
            searcher.PropertiesToLoad.Add("ou");
            searcher.PropertiesToLoad.Add("cn");
            foreach (SearchResult res in searcher.FindAll())
            {
                string name = null;
                if (res.Properties["ou"].Count > 0) name = Convert.ToString(res.Properties["ou"][0]);
                else if (res.Properties["cn"].Count > 0) name = Convert.ToString(res.Properties["cn"][0]);
                if (string.IsNullOrEmpty(name)) continue;

                result.Add(new ChildInfo { Name = name, Path = res.Path });
            }
        }
        return result;
    }

    // ===================== список =====================

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!_ready) return;
        LoadComputers();
    }

    private void OnFind(object sender, RoutedEventArgs e) => LoadComputers();

    private void OnRecursiveChanged(object sender, RoutedEventArgs e)
    {
        // событие приходит и во время InitializeComponent, когда Tree ещё не создан
        if (!_ready) return;
        LoadComputers();
    }

    private void OnMaskKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { LoadComputers(); e.Handled = true; }
    }

    private async void LoadComputers()
    {
        if (!_ready || Tree == null || ComputerList == null) return;

        var node = Tree.SelectedItem as TreeViewItem;
        if (node == null || node.Tag == null)
        {
            StatusText.Text = "Сначала выберите подразделение слева.";
            return;
        }

        Cursor = Cursors.Wait;
        _computers.Clear();
        try
        {
            string path = Convert.ToString(node.Tag);
            string mask = (MaskBox.Text ?? "").Trim().Trim('*');
            bool recursive = RecursiveBox.IsChecked == true;
            object header = node.Header;

            List<string> names = await Task.Run(() => ReadComputers(path, mask, recursive));

            foreach (string name in names) _computers.Add(new AdComputer { Name = name });

            StatusText.Text = "Компьютеров в «" + header + "»: " + names.Count +
                              (recursive ? " (с вложенными)" : " (только этот уровень)");
            CountText.Text = names.Count + " найдено";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка: " + Friendly(ex.Message);
        }
        finally { Cursor = Cursors.Arrow; }
    }

    private static List<string> ReadComputers(string path, string mask, bool recursive)
    {
        string like = "*";
        if (!string.IsNullOrEmpty(mask))
        {
            var escaped = new System.Text.StringBuilder();
            foreach (char c in mask)
            {
                if (c == '(' || c == ')' || c == '*' || c == '\\') escaped.Append('\\');
                escaped.Append(c);
            }
            like = "*" + escaped + "*";
        }
        string filter = "(&(objectCategory=computer)(name=" + like + "))";

        var result = new List<string>();
        using (var entry = new DirectoryEntry(path))
        using (var searcher = new DirectorySearcher(entry))
        {
            searcher.Filter = filter;
            searcher.SearchScope = recursive ? SearchScope.Subtree : SearchScope.OneLevel;
            searcher.PropertiesToLoad.Add("name");
            searcher.PageSize = 500;
            searcher.SizeLimit = 3000;
            searcher.Sort = new SortOption("name", SortDirection.Ascending);
            foreach (SearchResult res in searcher.FindAll())
            {
                if (res.Properties["name"].Count == 0) continue;
                result.Add(Convert.ToString(res.Properties["name"][0]));
                if (result.Count >= 3000) break;
            }
        }
        return result;
    }

    // ===================== кнопки =====================

    private void OnCheckAll(object sender, RoutedEventArgs e)
    {
        foreach (AdComputer c in _computers) c.IsChecked = true;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        Selected.Clear();
        foreach (AdComputer c in _computers)
        {
            if (c.IsChecked) Selected.Add(c.Name);
        }

        if (Selected.Count == 0)
        {
            MessageBox.Show(this, "Ничего не отмечено.", "1C Update", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ===================== подключение =====================

    private static DirectoryEntry OpenRoot(string domain, out string error)
    {
        error = null;
        string currentDns = null;
        string currentNetbios = null;
        try
        {
            var d = System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain();
            currentDns = d.Name;
            using (var entry = d.GetDirectoryEntry())
            {
                currentNetbios = Convert.ToString(entry.Properties["dc"].Value);
            }
        }
        catch { }

        bool isCurrent = string.IsNullOrEmpty(domain)
            || SameDomain(domain, currentDns)
            || SameDomain(domain, currentNetbios)
            || SameDomain(domain, Environment.UserDomainName);

        if (isCurrent && currentDns != null)
        {
            try
            {
                var d = System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain();
                return d.GetDirectoryEntry();
            }
            catch (Exception ex) { error = ex.Message; }
        }

        try { return new DirectoryEntry(domain.Length == 0 ? "LDAP://RootDSE" : "LDAP://" + domain); }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    private static bool SameDomain(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        a = a.Trim(); b = b.Trim();
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        int dotA = a.IndexOf('.'), dotB = b.IndexOf('.');
        if (dotA > 0) a = a.Substring(0, dotA);
        if (dotB > 0) b = b.Substring(0, dotB);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string Flatten(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        while (s.IndexOf("  ", StringComparison.Ordinal) >= 0) s = s.Replace("  ", " ");
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    /// <summary>Понятные сообщения вместо COM-ошибок DirectoryServices.</summary>
    private static string Friendly(string message)
    {
        if (string.IsNullOrEmpty(message)) return "не удалось подключиться к домену";

        string m = message.ToLowerInvariant();
        if (m.Contains("не работоспособен") || m.Contains("not operational") || m.Contains("0x8007203a"))
            return "контроллер домена недоступен (компьютер не в домене или нет связи с DC)";
        if (m.Contains("не удается найти") || m.Contains("cannot find") || m.Contains("0x8007054b"))
            return "домен не найден — проверьте имя домена";
        if (m.Contains("отказано в доступе") || m.Contains("access is denied") || m.Contains("0x80070005"))
            return "нет прав на чтение Active Directory";
        if (m.Contains("доверительные отношения") || m.Contains("trust relationship"))
            return "нет доверия к домену — проверьте членство компьютера в домене";

        return Flatten(message, 220);
    }
}
