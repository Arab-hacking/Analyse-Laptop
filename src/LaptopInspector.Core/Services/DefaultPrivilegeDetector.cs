using System.Security.Principal;

namespace LaptopInspector.Core.Services;

public sealed class DefaultPrivilegeDetector : IPrivilegeDetector
{
    public bool IsAdministrator()
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        return string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase);
    }
}
