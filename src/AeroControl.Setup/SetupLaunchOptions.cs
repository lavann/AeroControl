namespace AeroControl.Setup;

internal enum SetupAction
{
    Interactive,
    Install,
    Uninstall
}

internal sealed record SetupLaunchOptions(
    string? CapturePath,
    SetupAction Action,
    bool StartWithWindows)
{
    public static SetupLaunchOptions Parse(IEnumerable<string> arguments)
    {
        var source = arguments.ToArray();
        string? capturePath = null;
        var action = SetupAction.Interactive;
        var startWithWindows = false;
        for (var index = 0; index < source.Length; index++)
        {
            var argument = source[index];
            if (string.Equals(argument, "--capture", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= source.Length || source[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("--capture requires a value.");
                }

                capturePath = source[++index];
                continue;
            }

            if (argument.StartsWith("--capture=", StringComparison.OrdinalIgnoreCase))
            {
                capturePath = argument[10..];
                if (string.IsNullOrWhiteSpace(capturePath))
                {
                    throw new ArgumentException("--capture requires a value.");
                }

                continue;
            }

            if (string.Equals(argument, "--install", StringComparison.OrdinalIgnoreCase))
            {
                SetAction(ref action, SetupAction.Install);
                continue;
            }

            if (string.Equals(argument, "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                SetAction(ref action, SetupAction.Uninstall);
                continue;
            }

            if (string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase))
            {
                startWithWindows = true;
                continue;
            }

            throw new ArgumentException($"Unknown setup option: {argument}");
        }

        if (capturePath is not null && action != SetupAction.Interactive)
        {
            throw new ArgumentException("--capture cannot be combined with --install or --uninstall.");
        }

        if (startWithWindows && action != SetupAction.Install)
        {
            throw new ArgumentException("--startup requires --install.");
        }

        return new SetupLaunchOptions(capturePath, action, startWithWindows);
    }

    private static void SetAction(ref SetupAction current, SetupAction requested)
    {
        if (current != SetupAction.Interactive)
        {
            throw new ArgumentException("Choose only one setup action.");
        }

        current = requested;
    }
}
