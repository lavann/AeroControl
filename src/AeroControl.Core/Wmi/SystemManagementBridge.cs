using System.Management;
using System.Runtime.Versioning;
using AeroControl.Core.Abstractions;

namespace AeroControl.Core.Wmi;

[SupportedOSPlatform("windows10.0.17763")]
public sealed class SystemManagementBridge : IWmiBridge
{
    public IReadOnlySet<string> GetMethodNames(string namespacePath, string className)
    {
        var scope = CreateConnectedScope(namespacePath);
        using var managementClass = new ManagementClass(
            scope,
            new ManagementPath(className),
            null);
        managementClass.Get();

        return managementClass.Methods
            .Cast<MethodData>()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public WmiCallResult Invoke(
        string namespacePath,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var scope = CreateConnectedScope(namespacePath);
        using var managementClass = new ManagementClass(
            scope,
            new ManagementPath(className),
            null);
        using var instances = managementClass.GetInstances();

        foreach (ManagementObject instance in instances)
        {
            using (instance)
            using (var input = instance.GetMethodParameters(methodName))
            {
                if (arguments is not null)
                {
                    foreach (var (name, value) in arguments)
                    {
                        input[name] = value;
                    }
                }

                using var output = instance.InvokeMethod(methodName, input, null);
                var values = output?.Properties
                    .Cast<PropertyData>()
                    .ToDictionary(
                        property => property.Name,
                        property => (object?)property.Value,
                        StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                return new WmiCallResult(values);
            }
        }

        throw new InvalidOperationException(
            $"WMI class {className} did not expose an instance in {namespacePath}.");
    }

    public IReadOnlyDictionary<string, object?> QueryFirst(
        string namespacePath,
        string query)
    {
        var scope = CreateConnectedScope(namespacePath);
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery(query));
        using var results = searcher.Get();

        foreach (ManagementObject result in results)
        {
            using (result)
            {
                return result.Properties
                    .Cast<PropertyData>()
                    .Where(property => !property.Name.StartsWith("__", StringComparison.Ordinal))
                    .ToDictionary(
                        property => property.Name,
                        property => (object?)property.Value,
                        StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static ManagementScope CreateConnectedScope(string namespacePath)
    {
        var options = new ConnectionOptions
        {
            EnablePrivileges = true,
            Impersonation = ImpersonationLevel.Impersonate
        };
        var scope = new ManagementScope(namespacePath, options);
        scope.Connect();
        return scope;
    }
}
