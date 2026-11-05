using CredBench.Core.Services.GlobalPlatform;

namespace CredBench.Core.Services.Pkoc;

/// <summary>
/// Orchestrates PKOC applet deployment: SCP02 channel → delete existing → load CAP → install.
/// </summary>
public class PkocCardProgrammer
{
    // Package AID from build.xml: A000000898000002
    private static readonly byte[] PkocPackageAid =
        [0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x02];

    // Applet AID (also used as instance AID): A000000898000001
    private static readonly byte[] PkocAppletAid =
        [0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x01];

    // SELECT PKOC command for state detection
    private static readonly byte[] SelectPkocCommand =
    [
        0x00, 0xA4, 0x04, 0x00,
        0x08,
        0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x01,
        0x00
    ];

    /// <summary>
    /// Default GlobalPlatform keys used by most blank JavaCards.
    /// </summary>
    public static readonly byte[] DefaultGpKey = GpSessionFactory.DefaultGpKey;

    private readonly GlobalPlatformService _gpService = new();
    private readonly GpSessionFactory _sessionFactory;
    private readonly AppletLoader _appletLoader = new();

    public PkocCardProgrammer()
    {
        _sessionFactory = new GpSessionFactory(_gpService);
    }

    /// <summary>
    /// Programs the PKOC applet onto a blank JavaCard.
    /// Establishes SCP02, removes any existing PKOC applet, loads the CAP, and installs.
    /// When no explicit GP key is provided, tries common default keys in sequence.
    /// </summary>
    public void ProgramCard(
        ICardConnection connection,
        byte[] capFileData,
        byte[]? gpKey = null,
        bool deleteExisting = true,
        IProgress<(int Current, int Total)>? progress = null,
        IProgress<string>? statusProgress = null)
    {
        var session = _sessionFactory.Establish(connection, gpKey, statusProgress);

        statusProgress?.Report("Deleting existing applet...");
        if (deleteExisting)
        {
            _gpService.Delete(connection, session, PkocAppletAid);
            _gpService.Delete(connection, session, PkocPackageAid);
        }

        statusProgress?.Report("Loading applet...");
        _appletLoader.LoadAndInstall(
            connection, session, capFileData,
            PkocPackageAid, PkocAppletAid, PkocAppletAid,
            progress);
    }

    /// <summary>
    /// Checks whether the PKOC applet is already installed by attempting SELECT.
    /// Does not require an SCP02 session.
    /// </summary>
    public bool IsAppletInstalled(ICardConnection connection)
    {
        var response = connection.Transmit(SelectPkocCommand);
        return response.Length >= 2 && response[^2] == 0x90 && response[^1] == 0x00;
    }

    /// <summary>
    /// Establishes SCP02 and queries card content (installed applets, packages, free memory).
    /// </summary>
    public CardContent QueryCardContent(
        ICardConnection connection,
        byte[]? gpKey = null,
        IProgress<string>? statusProgress = null)
    {
        var session = _sessionFactory.Establish(connection, gpKey, statusProgress);
        statusProgress?.Report("Reading card content...");
        return _gpService.GetCardContent(connection, session);
    }
}
