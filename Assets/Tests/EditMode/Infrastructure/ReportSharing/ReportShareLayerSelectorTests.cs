using NUnit.Framework;
using ChessTheBetrayal.Infrastructure.ReportSharing;

namespace ChessTheBetrayal.Tests.EditMode.Infrastructure.ReportSharing
{
    /// <summary>
    /// The Android JNI itself can only be proven on a device, but which layer it starts from is
    /// plain arithmetic on a platform flag and an API level — the one piece of the export path this
    /// project can actually pin down without a phone.
    /// </summary>
    [TestFixture]
    public class ReportShareLayerSelectorTests
    {
        [Test]
        public void Choose_OffAndroid_IsClipboardOnly()
        {
            ReportShareLayer layer = ReportShareLayerSelector.Choose(isAndroidRuntime: false, androidApiLevel: 34);

            Assert.That(layer, Is.EqualTo(ReportShareLayer.ClipboardOnly),
                "Off Android there is no share sheet and no MediaStore to ask, whatever API level is passed in.");
        }

        [Test]
        public void Choose_AtTheMediaStoreDownloadsFloor_PicksMediaStoreDownloads()
        {
            ReportShareLayer layer = ReportShareLayerSelector.Choose(
                isAndroidRuntime: true, androidApiLevel: ReportShareLayerSelector.MinMediaStoreDownloadsApiLevel);

            Assert.That(layer, Is.EqualTo(ReportShareLayer.MediaStoreDownloads));
        }

        [Test]
        public void Choose_OneApiLevelBelowTheFloor_PicksTextIntent()
        {
            ReportShareLayer layer = ReportShareLayerSelector.Choose(
                isAndroidRuntime: true, androidApiLevel: ReportShareLayerSelector.MinMediaStoreDownloadsApiLevel - 1);

            Assert.That(layer, Is.EqualTo(ReportShareLayer.TextIntent),
                "MediaStore.Downloads throws on an API level below its own floor, so anything under it must not be offered.");
        }

        [Test]
        public void Choose_AtTheMinSupportedApiLevel_PicksTextIntent()
        {
            // The app's own AndroidMinSdkVersion floor, pinned in ProjectSettings — the oldest
            // device this project can actually be asked to run on.
            ReportShareLayer layer = ReportShareLayerSelector.Choose(isAndroidRuntime: true, androidApiLevel: 26);

            Assert.That(layer, Is.EqualTo(ReportShareLayer.TextIntent));
        }
    }
}
