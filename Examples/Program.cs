//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Linq;
using System.Threading;
using SharpDc;
using SharpDc.Connections;
using SharpDc.Managers;
using SharpDc.Structs;

namespace Examples
{
    class Program
    {
        public static DcEngine Engine;

        static void Main(string[] args)
        {
            // DcEngine is the main thing we will work with
            // use EngineSettings structure to set various settings of the engine
            Engine = new DcEngine();

            // we need to have at least one hub connection to work with
            var hubSettings = new HubSettings {
                HubAddress = "_write_your_hub_address_here_",
                HubName = "My hub",
                Nickname = "sharpdc"
            };
            
            var hubConnection = new HubConnection(hubSettings);

            // we want to see the hub connection status changes for debug puropses
            hubConnection.ConnectionStatusChanged += (sender, e) => Console.WriteLine("Hub " + e.Status);
            
            // add this connection to the engine
            Engine.Hubs.Add(hubConnection);

            // this event will be called when at least one hub will be connected and logged in
            Engine.ActiveStatusChanged += delegate {

                // to download a file we need to have a magnet-link
                var magnet = new Magnet("magnet:?xt=urn:tree:tiger:3UOKTPAQUGWGKWFIL75ZDMTTQLWF5AM2BAXBVEA&xl=63636810&dn=TiX_1_zvukovoy_barrier.avi");

                Console.WriteLine("Downloading the " + magnet.FileName);
                
                // the file will be saved in the current folder by default
                // you can provide custom file name:
                // engine.DownloadFile(magnet, "C:\\Temp\\my_name.avi");
                
                Engine.DownloadFile(magnet);
            };

            // we want to know when we have download complete
            Engine.DownloadManager.DownloadCompleted += (sender, e) => Console.WriteLine("Download is complete! Press enter to exit.");

            Console.WriteLine("Welcome to the sharpdc example");
            Console.WriteLine("We try to download a file");
            Console.WriteLine("Press enter to exit");

            // the engine to work needs Update() method to be called periodically
            // we can call it manually or use built-in method StartAsync()
            // that will use System.Threading.Timer to call Update() with 200 msec interval
            Engine.StartAsync();
            
            // would be cool to see download progress if any
            // so we call DisplayDownloadInfo method in other thread
            new ThreadStart(DisplayDownloadInfo).BeginInvoke(null, null);
            
            Console.ReadLine();
        }

        static void DisplayDownloadInfo()
        {
            while (true)
            {
                Thread.Sleep(1000);

                var downloadItem = Engine.DownloadManager.Items().FirstOrDefault();

                if (downloadItem == null)
                    // we have no download item, it is finished, exit...
                    return;

                Console.Write(string.Format("\r{0} {1}% Speed: {2}/s          ", 
                    downloadItem.Magnet.FileName, Math.Round(100 * ((float)downloadItem.DoneSegmentsCount / downloadItem.TotalSegmentsCount)), Utils.FormatBytes(Engine.TransferManager.Transfers().Downloads().DownloadSpeed())));
            }
        }
    }
}
