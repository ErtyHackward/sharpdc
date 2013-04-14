using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpDc;
using SharpDc.Managers;

namespace UnitTests
{
    [TestClass]
    public class DownloadItemSerialization
    {
        [TestMethod]
        public void DownloadItemSerializationSaveTest()
        {
            var engine = new DcEngine();
            var manager = new DownloadManager(engine);

            manager.Save("test.xml");



        }
    }
}
