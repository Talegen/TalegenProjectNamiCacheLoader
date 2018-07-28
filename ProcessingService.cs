//-----------------------------------------------------------------------
// <copyright file="ProcessingService.cs" company="Talegen, LLC">
// Free for the world to use.
// </copyright>
//-----------------------------------------------------------------------
namespace TalegenProjectNamiCacheLoader
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using HtmlAgilityPack;
    using TalegenProjectNamiCacheLoader.Properties;

    /// <summary>
    /// This class contains logic for processing requests to a specified reference website.
    /// </summary>
    public class ProcessingService
    {
        /// <summary>
        /// Contains an instance of the Text Writer stream for logging.
        /// </summary>
        private readonly TextWriter logWriter;

        /// <summary>
        /// Contains a concurrent bag list of visited links traversed.
        /// </summary>
        private readonly ConcurrentBag<string> visitedLinks;

        /// <summary>
        /// Contains or sets the maximum depth of indexing the site.
        /// </summary>
        private readonly int maxDepth;

        /// <summary>
        /// Contains or sets the maximum parallelism count.
        /// </summary>
        private readonly int maxParallelismCount;

        /// <summary>
        /// Contains the bypass key from configuration.
        /// </summary>
        private readonly string bypassKey;

        /// <summary>
        /// Contains a value indicating whether the program runs in multi-threaded mode.
        /// </summary>
        private readonly bool runInParallel;

        /// <summary>
        /// Contains the request timeout in seconds.
        /// </summary>
        private readonly int requestTimeout;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessingService"/> class.
        /// </summary>
        /// <param name="logTextWriter">Contains a <see cref="TextWriter"/> for writing logging output.</param>
        /// <param name="maxParallelismCount">Contains the maximum parallelism threads to run at any given time.</param>
        /// <param name="maxDepth">Contains the maximum indexing depth per site.</param>
        /// <param name="runInParallel">Contains a value indicating whether page retrieval loops should run in multi-threaded mode.</param>
        /// <param name="bypassKey">Contains a bypass key value for the client agent string.</param>
        /// <param name="requestTimeout">Contains an optional request timeout in seconds.</param>
        public ProcessingService(TextWriter logTextWriter, int maxParallelismCount = 10, int maxDepth = 10, bool runInParallel = true, string bypassKey = "", int requestTimeout = 100)
        {
            this.logWriter = logTextWriter ?? Console.Out;
            this.maxParallelismCount = maxParallelismCount;
            this.runInParallel = runInParallel;
            this.maxDepth = maxDepth;
            this.visitedLinks = new ConcurrentBag<string>();
            this.bypassKey = !string.IsNullOrWhiteSpace(bypassKey) ? bypassKey : TypeExtensions.RandomAlphaString(10);
            this.requestTimeout = requestTimeout;
        }

        /// <summary>
        /// This method is called to process recursively a site and any content that is found.
        /// </summary>
        /// <param name="siteUrl">Contains the site URL to process.</param>
        /// <param name="currentDepth">Contains the current depth.</param>
        /// <returns>Contains a <see cref="Task"/> object.</returns>
        public bool ProcessSite(string siteUrl, int currentDepth = 0)
        {
            // allowed extensions
            string[] contentExtensions = new[] { ".php", ".htm", ".html", ".aspx", ".js", ".css" };
            bool result = true;
            
            try
            {
                // Add the link to our history
                HttpStatusCode statusCode = HttpStatusCode.Unused;
                this.visitedLinks.Add(siteUrl);
                Uri siteUri = new Uri(siteUrl);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = this.GetResponse(siteUri);

                // end watch and get time to retrieve the contents.
                stopwatch.Stop();
                double milliseconds = stopwatch.ElapsedMilliseconds;
                stopwatch.Reset();

                if (response != null)
                {
                    statusCode = ((HttpWebResponse)response).StatusCode;
                }
                                
                // a response was received from the web request...
                if (response != null && statusCode == HttpStatusCode.OK)
                {
                    // if we are not at our maximum indexing depth...
                    if (currentDepth < this.maxDepth)
                    {
                        // attempt to parse for internal links in content an process them as well.
                        string pageContent = string.Empty;
                        
                        using (var contentReader = new StreamReader(response.GetResponseStream()))
                        {
                            pageContent = contentReader.ReadToEnd();
                        }
                        
                        this.logWriter.WriteLine(Resources.VisitedSiteResultMessageText, siteUrl, milliseconds / 1000);

                        var document = this.ParsePage(siteUri, pageContent);

                        if (document != null)
                        {
                            // Extract links from A tags with valid HREF values that are local to the site URL and 
                            // have not been processed yet and doesn't link to wp-admin.
                            var pageLinksToParse = document.DocumentNode.SelectNodes("//a[@href]")
                                .Where(link => link.Name.Equals("a", StringComparison.OrdinalIgnoreCase) &&
                                        link.Attributes["href"] != null &&
                                        !link.Attributes["href"].Value.StartsWith("#"))
                                .Select((link) =>
                                {
                                    var linkValue = link.Attributes["href"].Value;

                                    if (linkValue.StartsWith("/"))
                                    {
                                        linkValue = siteUri.Scheme + "://" + siteUri.DnsSafeHost + linkValue;
                                    }

                                    var cleanUri = new Uri(linkValue);
                                    string lastSegmentFileName = cleanUri.Segments.LastOrDefault();
                                    
                                    // if the domain names match and
                                    // there's no last segment, or the last segment doesn't contain a .,
                                    // or the last segment has a file extension of a file that is considered cacheable
                                    // return the query section of the URI
                                    // otherwise, we don't care about this link.
                                    return cleanUri.DnsSafeHost == siteUri.DnsSafeHost &&
                                        ((string.IsNullOrEmpty(lastSegmentFileName) || !lastSegmentFileName.Contains(".")) || contentExtensions.Contains(Path.GetExtension(lastSegmentFileName)))
                                        ? cleanUri.GetLeftPart(UriPartial.Query) : string.Empty;
                                })
                                .Where(link => link != string.Empty && 
                                        this.visitedLinks.All(vl => !vl.Equals(link, StringComparison.InvariantCultureIgnoreCase)) && 
                                        !link.Contains("/wp-admin/"))
                                .Distinct()
                                .ToList();

                            // any links found...
                            if (pageLinksToParse.Any())
                            {
                                this.logWriter.WriteLine(Resources.ProcessingInnerLinksText, pageLinksToParse.Count, siteUrl);

                                if (this.runInParallel)
                                {
                                    ParallelOptions parallelOptions = new ParallelOptions
                                    {
                                        CancellationToken = CancellationToken.None,
                                        TaskScheduler = TaskScheduler.Default,
                                        MaxDegreeOfParallelism = this.maxParallelismCount
                                    };

                                    // recursively process each valid page link found...
                                    Parallel.ForEach(
                                        pageLinksToParse, 
                                        parallelOptions, 
                                        (link) => 
                                        {
                                            // double check we haven't processed this in another thread already.
                                            if (this.visitedLinks.All(visitedLink => !visitedLink.Equals(link, StringComparison.InvariantCultureIgnoreCase)))
                                            {
                                                this.ProcessSite(link, currentDepth + 1);
                                            }
                                        });
                                }
                                else
                                {
                                    // recursively process each valid page link found...
                                    foreach (var link in pageLinksToParse)
                                    {
                                        // double check we haven't processed this in another recursion already.
                                        if (this.visitedLinks.All(visitedLink => !visitedLink.Equals(link, StringComparison.InvariantCultureIgnoreCase)))
                                        {
                                            this.ProcessSite(link, currentDepth + 1);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    this.logWriter.WriteLine(Resources.PageRequestErrorMessageText, siteUrl, statusCode, response != null ? ((HttpWebResponse)response).StatusDescription : string.Empty);
                    result = false;
                }
            }
            catch (Exception ex)
            {
                this.logWriter.WriteLine(Resources.PageRequestExceptionErrorMessageText, siteUrl, ex.RecurseMessages(), ex.StackTrace);
                result = false;
            }
            
            return result;
        }

        /// <summary>
        /// This method is used to retrieve the site page contents for the specified URI.
        /// </summary>
        /// <param name="siteUri">Contains the URI to request.</param>
        /// <returns>Contains the <see cref="WebResponse"/> object of the request.</returns>
        private WebResponse GetResponse(Uri siteUri)
        {
            WebResponse result = null;

            try
            {
                if (WebRequest.Create(siteUri) is HttpWebRequest request)
                {
                    // set timeout in miliseconds
                    request.Timeout = this.requestTimeout * 1000;
                    
                    // Add the BypassKey found in App Settings to the User Agent, so we can notify the Blob Cache Front End to always let us through
                    request.UserAgent += " " + this.bypassKey;
                    result = request.GetResponse();
                }
            }
            catch (Exception ex)
            {
                this.logWriter.WriteLine(Resources.PageGetResponseExceptionErrorMessageText, siteUri, ex.RecurseMessages(), ex.StackTrace);
            }

            return result;
        }

        /// <summary>
        /// This method is used to parse the web content and return 
        /// </summary>
        /// <param name="siteUri">Contains the site Uri of the page that is being parsed.</param>
        /// <param name="content">Contains the content of the page to parse.</param>
        /// <returns>Returns a new <see cref="HtmlDocument"/> if parsed.</returns>
        private HtmlDocument ParsePage(Uri siteUri, string content)
        {
            HtmlDocument result = null;

            try
            {
                result = new HtmlDocument();
                result.LoadHtml(content);
            }
            catch (Exception ex)
            {
                this.logWriter.WriteLine(Resources.PageParseExceptionErrorMessageText, siteUri, ex.RecurseMessages(), ex.StackTrace);
                result = null;
            }

            return result;
        }
    }
}
