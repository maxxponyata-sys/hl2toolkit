namespace HL2Toolkit;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Консольный режим для проверки без интерфейса: HL2Toolkit.exe --selftest
        if (args.Length > 0 && args[0] == "--selftest") { Environment.Exit(SelfTest.Run(args)); return; }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message + "\n\n" + e.Exception.StackTrace, "HL2 Toolkit — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new UI.MainForm());
    }
}
