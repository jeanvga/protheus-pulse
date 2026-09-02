using ProtheusPulse.Infrastructure.Monitoring;

namespace ProtheusPulse.UnitTests;

/// <summary>
/// Leitura do <c>appserver.ini</c>. Os trechos reproduzem o formato real com dados
/// sintéticos: nenhum host, ambiente ou nome de cliente verdadeiro entra aqui.
/// </summary>
public sealed class ProtheusAppServerIniTests
{
    private const string AppServerIni = """
        [AMBIENTETESTE]
        SourcePath=D:\Protheus\Protheus\APO
        RootPath=D:\Protheus\Protheus_data
        StartPath=\system\
        RpoVersion=120
        TopDataBase=MSSQL
        TopServer=10.0.0.9
        TopAlias=AmbienteTeste
        Environment=AmbienteTeste

        [DRIVERS]
        Active=TCP
        MultiProtocolPort=1
        multiprotocolportsecure=1

        [TCP]
        TYPE=TCPIP
        Port=3331

        [WEBAPP]
        PORT=8091

        [HTTPREST]
        Port=8070

        [GENERAL]
        InstallPath=D:\Protheus\Protheus
        Consolelog=1
        ConsoleMaxSize=10485760
        App_Environment=AmbienteTeste

        [LICENSECLIENT]
        server=10.0.0.8
        port=5555

        [ONSTART]
        JOBS=JOB_TESTE_01, HTTPJOB
        """;

    [Fact]
    public void EachDeclaredTargetIsRecognizedByItsSection()
    {
        var summary = ProtheusAppServerIni.Parse(AppServerIni);

        Assert.Equal("AmbienteTeste", summary.EnvironmentName);
        Assert.Equal("MSSQL", summary.DatabaseKind);
        Assert.Equal(@"D:\Protheus\Protheus_data", summary.RootPath);
        Assert.True(summary.ConsoleLogEnabled);
        Assert.Equal(10_485_760, summary.ConsoleMaxSizeBytes);
        Assert.Equal(["JOB_TESTE_01", "HTTPJOB"], summary.Jobs);

        Assert.Collection(
            summary.Targets,
            target => Assert.Equal(("AppServer (TCP)", "127.0.0.1", 3331, true), (target.Label, target.Host, target.Port, target.IsRequired)),
            target => Assert.Equal(("Portal web", "127.0.0.1", 8091, false), (target.Label, target.Host, target.Port, target.IsRequired)),
            target => Assert.Equal(("REST (HTTPREST)", "127.0.0.1", 8070, false), (target.Label, target.Host, target.Port, target.IsRequired)),
            target => Assert.Equal(("License Server", "10.0.0.8", 5555, true), (target.Label, target.Host, target.Port, target.IsRequired)),
            target => Assert.Equal(("DBAccess", "10.0.0.9", ProtheusAppServerIni.DefaultDbAccessPort, true), (target.Label, target.Host, target.Port, target.IsRequired)));
    }

    [Fact]
    public void MultiProtocolPortIsAToggleAndNeverBecomesAPort()
    {
        var summary = ProtheusAppServerIni.Parse(AppServerIni);

        Assert.DoesNotContain(summary.Targets, target => target.Port == 1);
    }

    [Fact]
    public void BrokerIniYieldsTheBalancerPortItsBackendsAndTheWindowsService()
    {
        const string broker = """
            [GENERAL]
            ConsoleLog=1
            ConsoleFile=D:\Protheus\Protheus\bin\appserver\Console.log

            [BALANCE_HTTP]
            LOCAL_SERVER_PORT=9088
            REMOTE_SERVER_01=10.0.0.8 3331
            REMOTE_SERVER_02=10.0.0.8 3332
            SERVICE_NAME=.TOTVS_BROKER
            """;

        var summary = ProtheusAppServerIni.Parse(broker);

        Assert.Equal(".TOTVS_BROKER", summary.WindowsServiceName);
        Assert.Equal(@"D:\Protheus\Protheus\bin\appserver\Console.log", summary.ConsoleFile);
        Assert.Equal(3, summary.Targets.Count);
        Assert.Contains(summary.Targets, target => target.Label == "Broker (balanceador)" && target.Port == 9088);
        Assert.Equal(2, summary.Targets.Count(target => target.Label == "AppServer balanceado"));
    }

    [Fact]
    public void AByteOrderMarkDoesNotHideTheFirstSection()
    {
        var content = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(System.Text.Encoding.UTF8.GetBytes("[GENERAL]\nConsoleLog=1\n\n[BALANCE_HTTP]\nLOCAL_SERVER_PORT=9088\n"))
            .ToArray();

        var summary = ProtheusAppServerIni.ParseBytes(content);

        Assert.True(summary.ConsoleLogEnabled);
        Assert.Contains(summary.Targets, target => target.Port == 9088);
    }

    [Fact]
    public void ACp1252IniIsReadWithoutMangledAccents()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var content = System.Text.Encoding.GetEncoding(1252)
            .GetBytes("[GENERAL]\nApp_Environment=Produção\n\n[TCP]\nPort=3331\n");

        var summary = ProtheusAppServerIni.ParseBytes(content);

        Assert.Equal("Produção", summary.EnvironmentName);
    }

    [Fact]
    public void AFileThatIsNotAnAppServerIniDeclaresNothing()
    {
        var summary = ProtheusAppServerIni.Parse("[Config]\nTheme=dark\nLanguage=pt-BR\n");

        Assert.Null(summary.EnvironmentName);
        Assert.Null(summary.WindowsServiceName);
        Assert.Empty(summary.Targets);
    }
}
