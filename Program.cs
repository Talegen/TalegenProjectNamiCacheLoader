//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Talegen, LLC">
// Free for the world to use.
// </copyright>
//-----------------------------------------------------------------------
namespace TalegenProjectNamiCacheLoader
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TalegenProjectNamiCacheLoader.Properties;

    /// <summary>
    /// This is the main entry point of the web job routine.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Gets a value indicating whether the the console program should wait for key press to end.
        /// </summary>
        private static bool DeveloperKeyPause => System.Configuration.ConfigurationManager.AppSettings["DeveloperPause"].ToBoolean(false);

        /// <summary>
        /// This is the main entry point of the web job. It will pull configuration values from the system configuration manager.
        /// </summary>
        public static void Main()
        {
            int maxDepth = System.Configuration.ConfigurationManager.AppSettings["ProjectNamiCacheLoader.MaxDepth"].ToInt(10);
            var siteList = System.Configuration.ConfigurationManager.AppSettings["ProjectNamiCacheLoader.SiteList"].ConvertToString().Split(',').ToList();
            bool runInParallel = System.Configuration.ConfigurationManager.AppSettings["ProjectNamiCacheLoader.Parallel"].ToBoolean(true);
            string bypassKey = System.Configuration.ConfigurationManager.AppSettings["ProjectNamiBlobCache.BypassKey"].ConvertToString(); 

            ThreadPool.GetMinThreads(out int minimumThreads, out int minimumPortThreads);
            
            if (runInParallel)
            {
                ParallelOptions parallelOptions = new ParallelOptions
                {
                    CancellationToken = CancellationToken.None,
                    TaskScheduler = TaskScheduler.Default,
                    MaxDegreeOfParallelism = minimumThreads * 2
                };

                Parallel.ForEach(
                    siteList, 
                    parallelOptions, 
                    (site) =>
                    {
                        string siteToProcess = site;

                        if (!siteToProcess.StartsWith("http://") && !siteToProcess.StartsWith("https://"))
                        {
                            siteToProcess = "http://" + siteToProcess;
                        }

                        Console.WriteLine(Resources.ProcessingSiteStartText, siteToProcess);
                        ProcessingService processingService = new ProcessingService(Console.Out, minimumThreads * 2, maxDepth, runInParallel, bypassKey);
                        processingService.ProcessSite(siteToProcess);
                        Console.WriteLine(Resources.ProcessingSiteEndText, siteToProcess);
                    });
            }
            else
            {
                foreach (var site in siteList)
                {
                    string siteToProcess = site;

                    if (!siteToProcess.StartsWith("http://") && !siteToProcess.StartsWith("https://"))
                    {
                        siteToProcess = "http://" + siteToProcess;
                    }

                    Console.WriteLine(Resources.ProcessingSiteStartText, siteToProcess);
                    ProcessingService processingService = new ProcessingService(Console.Out, minimumThreads * 2, maxDepth, runInParallel, bypassKey);
                    processingService.ProcessSite(siteToProcess);
                    Console.WriteLine(Resources.ProcessingSiteEndText, siteToProcess);
                }
            }
            
            if (DeveloperKeyPause)
            {
                Console.WriteLine(Resources.PressAnyKeyText);
                Console.ReadKey();
            }
        }
    }
}
