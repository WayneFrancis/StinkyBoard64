namespace StinkyFootboard.MacroHost;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Can be run in CI without opening a form or sending keystrokes.
        // Confirms C# interop matches native Windows INPUT on this process.
        if (args is ["--check-input-layout"])
        {
            KeyboardMacroHook.ValidateInputLayout();
            return;
        }

        // Each host has a global low-level keyboard hook; running two
        // instances can suppress each other's triggers and cause confusing
        // tray/taskbar behavior. Limit to one per interactive logon session.
        using var mutex = new Mutex(initiallyOwned: true,
            name: @"Local\StinkyFootboardRedoneMacroHost", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("MacroHost is already running. Check the taskbar " +
                "or notification area for the existing instance. Close it " +
                "before starting a new one.",
                "MacroHost already running", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MacroHostForm());
    }
}
