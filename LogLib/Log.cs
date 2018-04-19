using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Windows.Storage;

namespace LogLib
{
    public static class Log
    {
        public enum LogFlag
        {
            Debug,
            Information,
            Error
        }

        //public static readonly Logger logger;

        //private static readonly string LogPath = Path.Combine(Environment.ExpandEnvironmentVariables("%userprofile"), "Documents");

        private static readonly string LogPath = ApplicationData.Current.LocalFolder.Path;
        private static Logger logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(LogPath + "\\logs\\Roboshell.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

        public static void Trace(string s)
        {
            logger.Information(s);
            System.Diagnostics.Debug.WriteLine(s);
        }

        public static void Trace(string s, LogFlag flag)
        {
            switch (flag)
            {
                case LogFlag.Debug:
                    logger.Debug(s);
                    break;
                case LogFlag.Information:
                    logger.Information(s);
                    break;
                case LogFlag.Error:
                    logger.Error(s);
                    break;
                default:
                    logger.Error("Bad flag in Trace function");
                    break;
            }
            System.Diagnostics.Debug.WriteLine(s);
        }
    }
}
