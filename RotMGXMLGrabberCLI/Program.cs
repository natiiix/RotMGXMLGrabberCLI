using System;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using SwfDotNet.IO;
using SwfDotNet.IO.Tags;
using System.Net;
using System.Threading.Tasks;
using System.Reflection;

namespace RotMGXMLGrabberCLI
{
    public static class Program
    {
        private const string CLIENT_URL = "https://realmofthemadgodhrd.appspot.com/client";
        private const string OBJECTS_URL = "https://static.drips.pw/rotmg/production/current/xmlc/Objects.xml";
        private const string TILES_URL = "https://static.drips.pw/rotmg/production/current/xmlc/GroundTypes.xml";

        private const string PACKETS_FILE = "Packets.xml";
        private const string OBJECTS_FILE = "Objects.xml";
        private const string TILES_FILE = "Tiles.xml";

        private const string CLIENT_FILE = "client.swf";
        private const string RABCDASM_FILE = "RABCDAsm.exe";
        private const string ABCDATA_FILE = "abcdata.abc";
        private const string ABCDATA_DIR = "abcdata";
        private const string GAME_SERVER_CONNECTION_FILE = @"kabam\rotmg\messaging\impl\GameServerConnection.class.asasm";

        private static readonly string ASSEMBLY_DIR = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static readonly string PACKETS_PATH = Path.Combine(ASSEMBLY_DIR, PACKETS_FILE);
        private static readonly string OBJECTS_PATH = Path.Combine(ASSEMBLY_DIR, OBJECTS_FILE);
        private static readonly string TILES_PATH = Path.Combine(ASSEMBLY_DIR, TILES_FILE);

        private static readonly string CLIENT_PATH = Path.Combine(ASSEMBLY_DIR, CLIENT_FILE);
        private static readonly string RABCDASM_PATH = Path.Combine(ASSEMBLY_DIR, RABCDASM_FILE);
        private static readonly string ABCDATA_FILE_PATH = Path.Combine(ASSEMBLY_DIR, ABCDATA_FILE);
        private static readonly string ABCDATA_DIR_PATH = Path.Combine(ASSEMBLY_DIR, ABCDATA_DIR);
        private static readonly string GAME_SERVER_CONNECTION_PATH = Path.Combine(ABCDATA_DIR_PATH, GAME_SERVER_CONNECTION_FILE);

        private static void Main(string[] args)
        {
            Task.WaitAll(new Task[]
            {
                GetProd().ContinueWith(task => GetPackets()),
                GetObjects(),
                GetTiles()
            });
        }

        private static Task GetProd() =>
            DownloadFileAsync(CLIENT_FILE, CLIENT_URL, CLIENT_PATH);

        private static Task GetObjects() =>
            DownloadFileAsync(OBJECTS_FILE, OBJECTS_URL, OBJECTS_PATH);

        private static Task GetTiles() =>
            DownloadFileAsync(TILES_FILE, TILES_URL, TILES_PATH);

        private static void GetPackets()
        {
            if (!File.Exists(CLIENT_PATH))
            {
                Console.WriteLine($"Cannot find SWF file \"{CLIENT_FILE}\"");
                return;
            }

            Console.WriteLine($"Reading SWF file \"{CLIENT_FILE}\"...");

            SwfReader swfReader = new SwfReader(CLIENT_PATH);
            Swf swf = swfReader.ReadSwf();
            swfReader.Close();

            Console.WriteLine($"Done reading SWF file: \"{CLIENT_FILE}\"");

            WriteRABCDAsmToDisk();

            foreach (BaseTag tag in swf.Tags)
            {
                if (tag.name != null && tag.TagCode == (int)TagCodeEnum.DoABC2)
                {
                    WriteABCDataToDisk(tag);
                    RunRABCDAsm();

                    if (File.Exists(GAME_SERVER_CONNECTION_PATH))
                    {
                        ExtractPackets();
                    }
                    else
                    {
                        Console.WriteLine($"Cannot find ABC data for \"{PACKETS_FILE}\"");
                    }
                }
            }

            DeleteTempFiles();
        }

        private static void WriteRABCDAsmToDisk()
        {
            Console.WriteLine($"Writing \"{RABCDASM_FILE}\" to disk...");

            using (BinaryWriter bw = new BinaryWriter(File.Open(RABCDASM_PATH, FileMode.Create, FileAccess.Write)))
            {
                bw.Write(Properties.Resources.RABCDAsm);
            }

            Console.WriteLine($"File written to disk: \"{RABCDASM_FILE}\"");
        }

        private static void RunRABCDAsm()
        {
            Console.WriteLine($"Executing subprocess \"{RABCDASM_FILE}\"... (this can take a while)");

            Process processRABCDAsm = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = RABCDASM_PATH,
                    Arguments = $"\"{ABCDATA_FILE_PATH}\"",
                    //RedirectStandardOutput = true,
                    //RedirectStandardError = true,
                    //UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            processRABCDAsm.Start();
            processRABCDAsm.WaitForExit();

            Console.WriteLine($"Subprocess \"{RABCDASM_FILE}\" exited with exit code: {processRABCDAsm.ExitCode}");
        }

        private static void WriteABCDataToDisk(BaseTag tag)
        {
            Console.WriteLine($"Writing \"{ABCDATA_FILE}\" to disk...");

            using (BinaryWriter bw = new BinaryWriter(File.Open(ABCDATA_FILE_PATH, FileMode.Create, FileAccess.Write)))
            {
                bw.Write(((DoABC2Tag)tag).ABC);
            }

            Console.WriteLine($"File written to disk: \"{ABCDATA_FILE}\"");
        }

        private static void ExtractPackets()
        {
            Console.WriteLine($"Writing \"{PACKETS_FILE}\" to disk...");

            using (StreamWriter swPackets = new StreamWriter(PACKETS_PATH))
            {
                swPackets.WriteLine("<Packets>");

                using (StreamReader srGSC = new StreamReader(GAME_SERVER_CONNECTION_PATH))
                {
                    string pattern = "QName\\(PackageNamespace\\(\\\"\\\"\\), \\\"(\\w+)\\\"\\) slotid (?:.+) type QName\\(PackageNamespace\\(\\\"\\\"\\), \\\"int\\\"\\) value Integer\\((\\d+)\\)";

                    Regex rgx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    MatchCollection matches = rgx.Matches(srGSC.ReadToEnd());

                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count == 3)
                        {
                            swPackets.WriteLine("\t<Packet>\n\t\t<PacketName>{0}</PacketName>\n\t\t<PacketID>{1}</PacketID>\n\t</Packet>", match.Groups[1].Value.ToString().Replace("_", ""), match.Groups[2].Value.ToString());
                        }
                    }
                }

                swPackets.WriteLine("</Packets>");
            }

            Console.WriteLine($"File written to disk: \"{PACKETS_FILE}\"");
        }

        private static void DeleteTempFiles()
        {
            Console.WriteLine("Deleting temporary files...");

            File.Delete(CLIENT_PATH);
            File.Delete(RABCDASM_PATH);
            File.Delete(ABCDATA_FILE_PATH);

            //Directory.Delete(ABCDATA_DIR_PATH, true);
            Process.Start(new ProcessStartInfo("cmd", $"/c rmdir /S /Q \"{ABCDATA_DIR_PATH}\"") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }).WaitForExit();

            Console.WriteLine("Temporary files deleted");
        }

        private static Task DownloadFileAsync(string name, string url, string path)
        {
            Console.WriteLine($"Downloading \"{name}\"...");

            using (WebClient wc = new WebClient())
            {
                return wc.DownloadFileTaskAsync(new Uri(url), path)
                    .ContinueWith(_ => Console.WriteLine($"Download completed: \"{name}\""));
            }
        }
    }
}
