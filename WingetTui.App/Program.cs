using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using WingetTui.Core;

IPackageService packages = new WingetCliService();
IReadOnlyList<PackageInfo> current = [];
var updatesMode = false;

using var app = Application.Create();
app.Init();
using Window window = new();
window.Title = "winget TUI (F2: Updates, F5: Install, F6: Upgrade, Esc: Quit)";

var searchLabel = new Label
{
    Text = "Search:", 
    X = 1, 
    Y = 0
};
var search = new TextField
{
    X = Pos.Right(searchLabel) + 1, 
    Y = 0, 
    Width = Dim.Fill(1)
};

var status = new Label
{
    Text = "Enter a package name and press Enter.",
    X = 1,
    Y = 1,
    Width = Dim.Fill()
};

var list = new ListView 
{
    Title = "Packages",
    BorderStyle = LineStyle.Single,
    X = 0, Y = 3, 
    Width = Dim.Percent(40), 
    Height = Dim.Fill()
};

var details = new Label
{
    Title = "Details",
    BorderStyle = LineStyle.Single,
    X = Pos.Right(list), 
    Y = 3,
    Width = Dim.Fill(),
    Height = Dim.Fill()
};

window.Add(searchLabel, search, status, list, details);

void ShowDetails(int index)
{
    if (index < 0 || index >= current.Count)
    {
        details.Text = ""; 
        return;
    }
    var p = current[index];
    
    details.Text = updatesMode
        ? $"{p.Name}\n{p.Id}\n\n" +
          $"Installed version: {p.Version}\n" +
          $"Available version: {p.AvailableVersion}\n" +
          $"Source: {p.Source}"
        : $"{p.Name}\n{p.Id}\n\n" +
          $"Version: {p.Version}\n" +
          $"Match:   {p.Match}\n" +
          $"Source:  {p.Source}";
}

async Task RefreshAsync()
{
    var query = search.Text;

    if (string.IsNullOrWhiteSpace(query))
    {
        status.Text = "Enter a package name and press Enter.";
        return;
    }

    search.Enabled = false;
    list.Enabled = false;
    status.Text = "Searching winget sources...";

    try
    {
        var results = await Task.Run(() => packages.Search(query));

        app.Invoke(() =>
        {
            current = results;
            list.SetSource(
                [.. current.Select(p => $"{p.Name}  {p.Version}")]
            );
            ShowDetails(current.Count > 0 ? 0 : -1);

            status.Text = current.Count == 0
                ? "No packages found."
                : $"Found {current.Count} packages.";

            search.Enabled = true;
            list.Enabled = true;
        });
    }
    catch (Exception ex)
    {
        app.Invoke(() =>
        {
            status.Text = $"Search failed: {ex.Message}";
            search.Enabled = true;
            list.Enabled = true;
        });
    }
}

async Task RefreshAvailableUpdatesAsync()
{
    search.Enabled = false;
    list.Enabled = false;
    status.Text = "Checking for available updates...";

    try
    {
        var updates = await Task.Run(() => packages.GetAvailableUpdates());

        app.Invoke(() =>
        {
            current = updates;
            list.SetSource(
            [
                .. current.Select(p => $"{p.Name}  {p.Version} -> {p.AvailableVersion}")
            ]);

            ShowDetails(current.Count > 0 ? 0 : -1);

            status.Text = current.Count == 0 ? "No available updates found." : $"{current.Count} available updates.";
            search.Enabled = true;
            list.Enabled = true;
        });
    }
    catch (Exception ex)
    {
        app.Invoke(() =>
        {
            status.Text = $"Failed to load updates: {ex.Message}";
            search.Enabled = true;
            list.Enabled = true;
        });
    }
}

async Task InstallSelectedAsync()
{
    var index = list.SelectedItem ?? -1;

    if (index < 0 || index >= current.Count)
    {
        status.Text = "Select a package first.";
        return;
    }
    
    var package = current[index];

    var confirm = MessageBox.Query(
        app,
        "Confirm installation",
        $"Install {package.Name} ({package.Id})?",
        "Install",
        "Cancel");

    if (confirm != 0) return;

    search.Enabled = false;
    list.Enabled = false;
    status.Text = $"Installing {package.Name}...";

    try
    {
        await Task.Run(() => packages.Install(package, acceptPackageAgreements: false));

        app.Invoke(() =>
        {
            status.Text = $"Successfully installed {package.Name}.";
            search.Enabled = true;
            list.Enabled = true;
        });
    }
    catch (Exception ex)
    {
        app.Invoke(() =>
        {
            status.Text = $"Installation failed: {ex.Message}";
            search.Enabled = true;
            list.Enabled = true;
        });
    }
}

async Task UpgradeSelectedAsync()
{
    var index = list.SelectedItem ?? -1;

    if (index < 0 || index >= current.Count)
    {
        status.Text = "Select a package first.";
        return;
    }

    var package = current[index];

    var confirm = MessageBox.Query(
        app,
        "Confirm upgrade",
        $"Attempt to upgrade {package.Name} ({package.Id})?",
        "Upgrade",
        "Cancel");

    if (confirm != 0)
        return;

    search.Enabled = false;
    list.Enabled = false;
    status.Text = $"Upgrading {package.Name}...";

    try
    {
        await Task.Run(() => packages.Upgrade(
            package,
            acceptPackageAgreements: false));

        app.Invoke(() =>
        {
            status.Text = $"Successfully upgraded {package.Name}.";
            search.Enabled = !updatesMode;
            list.Enabled = true;
        });
    }
    catch (Exception ex)
    {
        app.Invoke(() =>
        {
            status.Text = $"Upgrade failed: {ex.Message}";
            search.Enabled = !updatesMode;
            list.Enabled = true;
        });
    }
}

search.Accepting += (_, e) =>
{
    e.Handled = true;
    _ = RefreshAsync();
};
list.ValueChanged += (_, _) => ShowDetails(list.SelectedItem ?? -1);
window.KeyDown += (_, e) =>
{
    if (e == Key.F2)
    {
        e.Handled = true;

        if (!list.Enabled)
            return;

        if (updatesMode)
        {
            updatesMode = false;
            list.Title = "Packages";

            current = [];
            list.SetSource<object>([]);
            ShowDetails(-1);

            search.Enabled = true;
            status.Text = string.IsNullOrWhiteSpace(search.Text)
                ? "Enter a package name and press Enter."
                : "Search mode. Press Enter to search again.";
        }
        else
        {
            updatesMode = true;
            list.Title = "Available Updates";

            current = [];
            list.SetSource<object>([]);
            ShowDetails(-1);

            _ = RefreshAvailableUpdatesAsync();
        }
    }
    else if (e == Key.F5)
    {
        e.Handled = true;

        if (!list.Enabled)
            return;

        if (updatesMode)
        {
            status.Text = "Use F6 to upgrade a selected package.";
            return;
        }

        _ = InstallSelectedAsync();
    }
    else if (e == Key.F6)
    {
        e.Handled = true;

        if (!list.Enabled)
            return;

        _ = UpgradeSelectedAsync();
    }
};
app.Run(window);
