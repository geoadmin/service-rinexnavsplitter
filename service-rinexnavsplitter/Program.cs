using System;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace RinexNavSplitter
{
    class Program
    {
        private static LogWriter lwSingleton = LogWriter.GetInstance;
        private static DateTime dateTime;
        private static string dateTimePath;
        private static bool isDelete = false;
        private static string dateShopPath = "";
        private static Stopwatch stopwatchStep;
        private static Stopwatch stopwatchTotal;
        private static readonly string shopPath = ConfigurationManager.AppSettings["ShopPath"];
        private static StreamWriter swGPS;
        private static StreamWriter swGLONASS;
        private static StreamWriter swGALILEO;
        private static StreamWriter swBEIDOU;

        static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0)
                {
                    Usage(args);
                }
                else
                {
                    if (!Directory.Exists(ConfigurationManager.AppSettings["LogPath"]))
                    {
                        Directory.CreateDirectory(ConfigurationManager.AppSettings["LogPath"]);
                    }
                    LogWriter.WriteToLog(string.Format("===============================Start=============================="));
                    Process processes = Process.GetCurrentProcess();
                    processes.PriorityClass = ProcessPriorityClass.BelowNormal;
                    stopwatchStep = new Stopwatch();
                    stopwatchTotal = new Stopwatch();
                    Splitting();
                    LogWriter.WriteToLog(string.Format("=================================End=============================="));
                }
            }
            catch (Exception e)
            {
                System.Console.WriteLine(e.Message);
                LogWriter.WriteToLog(e);
                LogWriter.WriteToLog(string.Format("=================================End=============================="));
            }
            finally
            {
                LogWriter.ForceFlush();
            }
        }
        private static void Usage(string[] args)
        {
            if (args[0].Length > 0)
            {
                Console.WriteLine("Splitts the Rinex 3 combinde navigationfile in one navigationfile per satellite system.");
                Console.WriteLine("");
                Console.WriteLine("RinexNavSplitter [/?] [/help]");
                Console.WriteLine("");
                Console.WriteLine("/? /help     Shows this help.");
                Console.WriteLine("");
                Console.WriteLine("No Parameter are needed to start the processing.");
                Console.WriteLine("All configurations are read from the .config file.");
                Console.WriteLine("The configuration for RinexNavSplitter can be found at the install folder (.config files).");
            }
        }

        private static void Splitting()
        {
            stopwatchTotal.Start();
            bool.TryParse(ConfigurationManager.AppSettings["DeleteIfOK"], out isDelete);
            if ((ConfigurationManager.AppSettings["SpecificDateYear"].ToString().Length > 0) &&
                (ConfigurationManager.AppSettings["SpecificDateMonth"].ToString().Length > 0) &&
                (ConfigurationManager.AppSettings["SpecificDateDay"].ToString().Length > 0))
            {
                int year = 0;
                int month = 0;
                int day = 0;
                if (int.TryParse(ConfigurationManager.AppSettings["SpecificDateYear"], out year) &&
                    int.TryParse(ConfigurationManager.AppSettings["SpecificDateMonth"], out month) &&
                    int.TryParse(ConfigurationManager.AppSettings["SpecificDateDay"], out day))
                {
                    dateTime = new DateTime(year, month, day);
                    LogWriter.WriteToLog(string.Format("Specific Day set, setting 'DaysBack' will be ignored!"));
                    SplittingDay();
                }
                else
                {
                    throw new Exception("DateTime Parse error!");
                }
            }
            else
            {
                dateTime = DateTime.Now;
                int numDaysBack = 0;
                int.TryParse(ConfigurationManager.AppSettings["DaysBack"], out numDaysBack);
                for (int days = 0; days <= numDaysBack; days++)
                {
                    dateTime = DateTime.Now.AddDays(-days);
                    SplittingDay();
                }
            }
            LogWriter.WriteToLog(string.Format("Totaltime to process : {0:hh\\:mm\\:ss}", stopwatchTotal.Elapsed));
            stopwatchTotal.Stop();

        }

        private static bool PrepareHeader(string line)
        {
            bool isEndOfHeader = false;
            if (line.Contains("RINEX VERSION / TYPE"))
            {
                swGPS.WriteLine(checkRinex(line.Replace("M: MIXED NAV DATA", "G: GPS           ")));
                swBEIDOU.WriteLine(checkRinex(line.Replace("M: MIXED NAV DATA", "C: BDS           ")));
                swGLONASS.WriteLine(checkRinex(line.Replace("M: MIXED NAV DATA", "R: GLONASS       ")));
                swGALILEO.WriteLine(checkRinex(line.Replace("M: MIXED NAV DATA", "E: GALILEO       ")));
            }
            if (line.Contains("PGM / RUN BY / DATE"))
            {
                swGPS.WriteLine(checkRinex(string.Format("RinexNavSplitter    swisstopo           {0} ", line.Substring(40))));
                swBEIDOU.WriteLine(checkRinex(string.Format("RinexNavSplitter    swisstopo           {0} ", line.Substring(40))));
                swGLONASS.WriteLine(checkRinex(string.Format("RinexNavSplitter    swisstopo           {0} ", line.Substring(40))));
                swGALILEO.WriteLine(checkRinex(string.Format("RinexNavSplitter    swisstopo           {0} ", line.Substring(40))));
            }
            if (line.Contains("GPSA") || line.Contains("GPSB") || line.Contains("GPUT"))
            {
                swGPS.WriteLine(checkRinex(line.Insert(line.Length, "    ")));
            }
            if (line.Contains("GAL") || line.Contains("GAUT"))
            {
                swGALILEO.WriteLine(checkRinex(line.Insert(line.Length, "    ")));
            }
            if (line.Contains("GLUT"))
            {
                swGLONASS.WriteLine(checkRinex(line.Insert(line.Length, "    ")));
            }
            if (line.Contains("BDSA") || line.Contains("BDSB") || line.Contains("BDUT"))
            {
                swBEIDOU.WriteLine(checkRinex(line.Insert(line.Length, "    ")));
            }
            if (line.Contains("LEAP SECONDS"))
            {
                // GPS Week is wrong -> NetR9
                // Because VRS in RefDataShop "Leap second" for GLONASS is uesd
                string line1 = line.Remove(7);
                string line2 = line1.Insert(7, "                                                     LEAP SECONDS");
                swGLONASS.WriteLine(checkRinex(line2.Insert(line2.Length, "        ")));
            }
            if (line.Contains("END OF HEADER"))
            {
                swGPS.WriteLine(checkRinex(line.Insert(line.Length, "       ")));
                swBEIDOU.WriteLine(checkRinex(line.Insert(line.Length, "       ")));
                swGLONASS.WriteLine(checkRinex(line.Insert(line.Length, "       ")));
                swGALILEO.WriteLine(checkRinex(line.Insert(line.Length, "       ")));
                isEndOfHeader = true;
            }
            return isEndOfHeader;
        }

        private static void PrepareNavigation(StreamReader reader)
        {
            string line;
            bool isGPS = false;
            bool isGLONASS = false;
            bool isBEIDOU = false;
            bool isGALILEO = false;
            while ((line = reader.ReadLine()) != null)
            {
                if (isGPS && line.StartsWith(" "))
                {
                    swGPS.WriteLine(line);
                }
                if (isGALILEO && line.StartsWith(" "))
                {
                    swGALILEO.WriteLine(line);
                }
                if (isGLONASS && line.StartsWith(" "))
                {
                    swGLONASS.WriteLine(line);
                }
                if (isBEIDOU && line.StartsWith(" "))
                {
                    swBEIDOU.WriteLine(line);
                }
                if (line.StartsWith("G"))
                {
                    isGPS = true;
                    isGLONASS = false;
                    isBEIDOU = false;
                    isGALILEO = false;
                    swGPS.WriteLine(line);
                }
                if (line.StartsWith("E"))
                {
                    isGPS = false;
                    isGLONASS = false;
                    isBEIDOU = false;
                    isGALILEO = true;
                    swGALILEO.WriteLine(line);
                }
                if (line.StartsWith("R"))
                {
                    isGPS = false;
                    isGLONASS = true;
                    isBEIDOU = false;
                    isGALILEO = false;
                    swGLONASS.WriteLine(line);
                }
                if (line.StartsWith("C"))
                {
                    isGPS = false;
                    isBEIDOU = true;
                    isGLONASS = false;
                    isGALILEO = false;
                    swBEIDOU.WriteLine(line);
                }
            }
        }

        private static string checkRinex(string line)
        {
            if (line.Length == 80)
            {
                return line;
            }
            else
            {
                throw new Exception(string.Format("Length error at line:{0}", line));
            }
        }

        private static void SplittingDay()
        {
            dateTimePath = String.Format("RefData.{0}\\Month.{1}\\Day.{2:D2}", dateTime.Year % 100, CultureInfo.CreateSpecificCulture("en").DateTimeFormat.GetAbbreviatedMonthName(dateTime.Month), dateTime.Day);
            dateShopPath = Path.Combine(shopPath, dateTimePath);
            if (!Directory.Exists(dateShopPath))
            {
                throw new Exception("Error: ShopPath does not exist!");
            }
            stopwatchStep.Start();
            LogWriter.WriteToLog(string.Format("=> Processing Day:{0}", dateTime.ToShortDateString()));
            int countFiles = 0;
            DirectoryInfo dateShopInputDIR = new DirectoryInfo(dateShopPath);
            FileInfo[] rinexInputFiles = dateShopInputDIR.GetFiles("*MN.rnx");
            foreach (FileInfo file in rinexInputFiles)
            { 
                if (file.Length > 0)
                {
                    swGPS = File.CreateText(file.FullName.Replace("_01S_", "_").Replace("H_MN", "H_GN"));
                    swBEIDOU = File.CreateText(file.FullName.Replace("_01S_", "_").Replace("H_MN", "H_CN"));
                    swGLONASS = File.CreateText(file.FullName.Replace("_01S_", "_").Replace("H_MN", "H_RN"));
                    swGALILEO = File.CreateText(file.FullName.Replace("_01S_", "_").Replace("H_MN", "H_EN"));
                    using (StreamReader reader = file.OpenText())
                    {
                        string line;
                        bool isEndOfHeader = false;
                        while (!isEndOfHeader)
                        {
                            line = reader.ReadLine();
                            isEndOfHeader = PrepareHeader(line);
                        }
                        PrepareNavigation(reader);
                    }
                    countFiles++;
                    swGPS.Close();
                    swBEIDOU.Close();
                    swGLONASS.Close();
                    swGALILEO.Close();
                }
                if (isDelete)
                {
                    file.Delete();
                }
            }
            LogWriter.WriteToLog("Prozessed Files: " + countFiles.ToString());
        }
    }
}
