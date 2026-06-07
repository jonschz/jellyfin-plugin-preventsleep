using Jellyfin.Plugin.PreventSleep.Interface;

namespace Jellyfin.Plugin.PreventSleep.Infrastructure;

internal sealed class MacosPowerManagement : IPowerManagement
{
    private readonly MacosCfStringRef _kIopmAssertionTypePreventUserIdleSystemSleep;
    private readonly MacosCfStringRef _assertionName;
    private readonly MacosCfStringRef _assertionDetails;
    private MacosIopmAssertion? _sleepAssertion;

    internal MacosPowerManagement()
    {
        _kIopmAssertionTypePreventUserIdleSystemSleep = "PreventUserIdleSystemSleep";
        /*
         * According to https://developer.apple.com/library/archive/qa/qa1340/_index.html
         * and https://github.com/apple-oss-distributions/PowerManagement/blob/main/caffeinate/caffeinate.c#L169
         * these strings' length are capped to 128 characters.
         */
        _assertionName = new MacosCfStringRef("Prevent Sleep Plugin for Jellyfin", 128);
        _assertionDetails =
            new MacosCfStringRef("Serving files/waiting for the configured amount of time for further requests", 128);
    }

    public void BlockSleep()
    {
        if (_sleepAssertion is not null)
        {
            return;
        }

        _sleepAssertion = MacosIopmAssertion.Create(_kIopmAssertionTypePreventUserIdleSystemSleep, _assertionName, _assertionDetails);
    }

    public void UnblockSleep()
    {
        _sleepAssertion?.Close();
        _sleepAssertion = null;
    }

    public void Dispose()
    {
        _sleepAssertion?.Dispose();
        _assertionDetails.Dispose();
        _assertionName.Dispose();
        _kIopmAssertionTypePreventUserIdleSystemSleep.Dispose();
    }
}
