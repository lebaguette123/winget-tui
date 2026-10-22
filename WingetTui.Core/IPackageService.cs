namespace WingetTui.Core;

public interface IPackageService
{
    IReadOnlyList<PackageInfo> Search(string query);

    void Install(PackageInfo package, bool acceptPackageAgreements);
    
    void Upgrade(PackageInfo package, bool acceptPackageAgreements);
    IReadOnlyList<PackageInfo> GetAvailableUpdates();
}