namespace WingetTui.Core;

public record PackageInfo(
    string Id, 
    string Name, 
    string Version, 
    string Publisher = "", 
    string Description = "",
    string Match = "",
    string Source = "");