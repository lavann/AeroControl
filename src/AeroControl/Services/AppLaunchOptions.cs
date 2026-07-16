namespace AeroControl.Services;

internal enum AppView
{
    Cooling,
    Battery,
    Monitor,
    Diagnostics,
    Profiles,
    Settings
}

internal sealed record AppLaunchOptions(
    bool IsDemo,
    string? CapturePath,
    AppView InitialView,
    bool HasExplicitView)
{
    public static AppLaunchOptions Parse(IEnumerable<string> arguments)
    {
        var source = arguments.ToArray();
        var isDemo = false;
        string? capturePath = null;
        var initialView = AppView.Cooling;
        var hasExplicitView = false;

        for (var index = 0; index < source.Length; index++)
        {
            var argument = source[index];
            if (string.Equals(argument, "--demo", StringComparison.OrdinalIgnoreCase))
            {
                isDemo = true;
                continue;
            }

            if (string.Equals(argument, "--capture", StringComparison.OrdinalIgnoreCase))
            {
                capturePath = ReadValue(source, ref index, "--capture");
                continue;
            }

            if (argument.StartsWith("--capture=", StringComparison.OrdinalIgnoreCase))
            {
                capturePath = ReadInlineValue(argument, "--capture");
                continue;
            }

            if (string.Equals(argument, "--view", StringComparison.OrdinalIgnoreCase))
            {
                initialView = ParseView(ReadValue(source, ref index, "--view"));
                hasExplicitView = true;
                continue;
            }

            if (argument.StartsWith("--view=", StringComparison.OrdinalIgnoreCase))
            {
                initialView = ParseView(ReadInlineValue(argument, "--view"));
                hasExplicitView = true;
                continue;
            }

            throw new ArgumentException($"Unknown AeroControl option: {argument}");
        }

        return new AppLaunchOptions(isDemo, capturePath, initialView, hasExplicitView);
    }

    private static string ReadValue(string[] source, ref int index, string option)
    {
        if (index + 1 >= source.Length || source[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return source[index];
    }

    private static string ReadInlineValue(string argument, string option)
    {
        var value = argument[(option.Length + 1)..];
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{option} requires a value.")
            : value;
    }

    private static AppView ParseView(string value) => value.ToLowerInvariant() switch
    {
        "cooling" => AppView.Cooling,
        "battery" => AppView.Battery,
        "monitor" => AppView.Monitor,
        "diagnostics" => AppView.Diagnostics,
        "profiles" => AppView.Profiles,
        "settings" => AppView.Settings,
        _ => throw new ArgumentException(
            $"Unknown AeroControl view '{value}'. Expected cooling, battery, monitor, diagnostics, profiles, or settings.")
    };
}
