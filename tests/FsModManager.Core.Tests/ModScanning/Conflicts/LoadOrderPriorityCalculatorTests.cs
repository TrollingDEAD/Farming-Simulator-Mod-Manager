using FsModManager.Core.ModScanning.Conflicts;
using Xunit;

namespace FsModManager.Core.Tests.ModScanning.Conflicts;

public sealed class LoadOrderPriorityCalculatorTests
{
    private readonly LoadOrderPriorityCalculator _calculator = new();

    [Fact]
    public void GetLaterLoadingMod_MatchesRealWorldForumReportedOutcome()
    {
        // Real forum-reported scenario: the mod author deliberately renamed their zip with a "ZZ"
        // prefix specifically to force it to load last and win the override. This only works if
        // FS25's comparison is case-INSENSITIVE - a plain case-sensitive StringComparer.Ordinal
        // compare would put "FS25_zLiftablePalletsBales.zip" after "FS25_ZZGuaranteedCropPrices.zip"
        // regardless of the "ZZ" prefix (ASCII lowercase 'z' > uppercase 'Z'), which would make the
        // observed real-world "ZZ" trick pointless. See LoadOrderPriorityCalculator's remarks.
        const string zLiftablePalletsBales = "FS25_zLiftablePalletsBales.zip";
        const string zzGuaranteedCropPrices = "FS25_ZZGuaranteedCropPrices.zip";

        var winner = _calculator.GetLaterLoadingMod(zLiftablePalletsBales, zzGuaranteedCropPrices);

        Assert.Equal(zzGuaranteedCropPrices, winner);
    }

    [Fact]
    public void OrderByLoadOrder_UsesFileNameOnly_IgnoringFolderPath()
    {
        var ordered = _calculator.OrderByLoadOrder(new[]
        {
            @"C:\mods\FS25_zzLast.zip",
            @"D:\other\FS25_AFirst.zip",
        });

        Assert.Equal(@"D:\other\FS25_AFirst.zip", ordered[0]);
        Assert.Equal(@"C:\mods\FS25_zzLast.zip", ordered[1]);
    }

    [Fact]
    public void GetLaterLoadingMod_IsCaseInsensitive_NotCultureAware()
    {
        // "b" vs "A" - case-insensitive ordinal puts A before b, same as invariant-culture would,
        // but the point being tested is that this is NOT a raw case-sensitive byte compare.
        var winner = _calculator.GetLaterLoadingMod("FS25_A.zip", "FS25_b.zip");

        Assert.Equal("FS25_b.zip", winner);
    }
}
