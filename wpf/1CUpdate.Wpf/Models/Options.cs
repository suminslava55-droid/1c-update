namespace OneCUpdate.Models;

/// <summary>Параметры установки</summary>
public sealed class Options
{
    public string Psexec { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public string DistribDir { get; set; } = string.Empty;
    public string LogDir { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string InstallMode { get; set; } = "no";
    public string DeleteMode { get; set; } = "no";
    public bool DesignerAllClients { get; set; }
    public bool ThinClient { get; set; } = true;
    public bool ThinClientFile { get; set; }
    public int Threads { get; set; } = 4;
    public int TimeoutMinutes { get; set; } = 30;
    public bool DryRun { get; set; }
    public string Delivery { get; set; } = "exe";
    public string LocalExe { get; set; } = string.Empty;

    public string Iod =>
        $"DESIGNERALLCLIENTS={Tf(DesignerAllClients)} THINCLIENT={Tf(ThinClient)} THINCLIENTFILE={Tf(ThinClientFile)}";

    private static int Tf(bool value) => value ? 1 : 0;

    public string Prefix(string pc)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("\\\\").Append(pc).Append(" -s -accepteula");
        if (!string.IsNullOrEmpty(User))
        {
            sb.Append(" -u \"").Append(User).Append("\"");
            if (!string.IsNullOrEmpty(Password)) sb.Append(" -p \"").Append(Password).Append("\"");
        }
        return sb.ToString();
    }

    public string BuildArgsKill(string pc) => Prefix(pc) + " taskkill /f /t /im 1CUpdate.exe";

    public string BuildArgs(string pc, string progressPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Prefix(pc));

        if (Delivery == "exe")
        {
            sb.Append(" -c -f \"").Append(LocalExe).Append("\"");
            sb.Append(" --remote-worker");
            if (!string.IsNullOrEmpty(progressPath))
                sb.Append(" --progress \"").Append(progressPath).Append("\"");
        }
        else
        {
            sb.Append(" powershell -NoProfile -ExecutionPolicy Bypass -File \"").Append(Script).Append("\"");
        }

        sb.Append(" -dd \"").Append(DistribDir).Append("\"");
        sb.Append(" -dl \"").Append(LogDir).Append("\"");
        sb.Append(" -ip \"").Append(InstallMode).Append("\"");
        sb.Append(" -dp \"").Append(DeleteMode).Append("\"");
        sb.Append(" -iod \"").Append(Iod).Append("\"");
        return sb.ToString();
    }
}
