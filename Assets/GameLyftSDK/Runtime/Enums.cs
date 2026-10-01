namespace GameLyft.Sdk
{
    public enum FTUEState
    {
        ftue_start,
        ftue_complete
    }

    public enum LevelState
    {
        level_start,
        level_complete,
        level_fail,
        level_skip,
        level_restart,
        level_pause,
        level_resume
    }

    public enum GLAdFormat
    {
        Banner,
        Mrec,
        Interstitial,
        Rewarded,
        AppOpen
    }

    public enum GLAdResult
    {
        Available,
        NotAvailable
    }

    /// <summary>Platforms the SDK delivers events to. See GameLyftAnalytics.MarkReady.</summary>
    public enum GLDestination
    {
        Firebase,
        AppsFlyer,
        Adjust,
        SolarEngine,
        Singular,
        Airbridge
    }
}
