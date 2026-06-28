using Jellyfin.Plugin.PreventSleep.Interface;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal sealed class MacosPowerManagement : IPowerManagement
{
    private readonly ILogger<MacosPowerManagement> _logger;
    private readonly MacosCfStringRef _kIopmAssertionTypePreventUserIdleSystemSleep;
    private readonly MacosCfStringRef _assertionName;
    private readonly MacosCfStringRef _assertionDetails;
    private MacosIopmAssertion? _sleepAssertion;

    internal MacosPowerManagement(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MacosPowerManagement>();
        _kIopmAssertionTypePreventUserIdleSystemSleep = new MacosCfStringRef("PreventUserIdleSystemSleep");
        /*
         * According to https://developer.apple.com/library/archive/qa/qa1340/_index.html
         * and https://github.com/apple-oss-distributions/PowerManagement/blob/main/caffeinate/caffeinate.c#L169
         * these strings' length are capped to 128 characters.
         */
        _assertionName = new MacosCfStringRef("Prevent Sleep Plugin for Jellyfin");
        _assertionDetails = new MacosCfStringRef("Serving files/waiting for the configured amount of time for further requests");
    }

    public void BlockSleep()
    {
        if (_sleepAssertion is not null)
        {
            return;
        }

        _sleepAssertion = MacosIopmAssertion.Create(_kIopmAssertionTypePreventUserIdleSystemSleep, _assertionName, _assertionDetails);
        _logger.LogDebug("IOPMAssertionCreateWithDescription succeeded: sleep blocked");
    }

    public void UnblockSleep()
    {
        if (_sleepAssertion is not null)
        {
            _sleepAssertion.Close();
            _sleepAssertion = null;
            _logger.LogDebug("Power assertion released: sleep re-enabled");
        }
    }

    public void Dispose()
    {
        UnblockSleep();
        _assertionDetails.Dispose();
        _assertionName.Dispose();
        _kIopmAssertionTypePreventUserIdleSystemSleep.Dispose();
    }
}
