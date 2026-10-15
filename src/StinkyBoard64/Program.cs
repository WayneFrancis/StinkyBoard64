using System.Diagnostics;

namespace StinkyFootboard.Editor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        string warning =
            "STINKYBOARD64 — USE AT YOUR OWN RISK\n\n" +
            "This program is not supported or endorsed by the Stinky Footboard manufacturer.\n\n" +
            "Writes change persistent EEPROM settings. A partial write could leave a board " +
            "unresponsive or with damaged configuration. Every write REPLACES ALL FOUR " +
            "button key/sequence pages; it can ERASE MACROS or alternate-bank settings, " +
            "including bytes this app cannot read. Windows USB attachment numbers and " +
            "Container IDs are not permanent serial numbers.\n\n" +
            "Back up each board's settings to JSON and keep the original utility for " +
            "recovery. A JSON file is not a complete EEPROM backup.\n\n" +
            "Close Stinky.exe first. The software is provided without warranty.\n\n" +
            "Do you understand and ACCEPT THE RISK of using this software?";

        if (MessageBox.Show(warning, "Accept risk — StinkyBoard64",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        if (Process.GetProcessesByName("Stinky").Length != 0)
        {
            MessageBox.Show("Close original Stinky.exe before using this program.",
                "Original software is running", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        Application.Run(new MainForm());
    }
}
