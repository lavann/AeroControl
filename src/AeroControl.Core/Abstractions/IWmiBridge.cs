using AeroControl.Core.Wmi;

namespace AeroControl.Core.Abstractions;

public interface IWmiBridge
{
    IReadOnlySet<string> GetMethodNames(string namespacePath, string className);

    WmiCallResult Invoke(
        string namespacePath,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?>? arguments = null);

    IReadOnlyDictionary<string, object?> QueryFirst(string namespacePath, string query);
}
