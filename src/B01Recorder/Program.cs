namespace B01Recorder;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--verify") || args.Contains("--preview"))
        {
            Verification.Run(args.Contains("--preview"));
            return;
        }
        Application.Run(new MainForm());
    }
}
