using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

using Jackett.Common.Models;
using Jackett.Common.Models.IndexerConfig;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Newtonsoft.Json.Linq;
using NLog;
using WebClient = Jackett.Common.Utils.Clients.WebClient;
using static Jackett.Common.Models.IndexerConfig.ConfigurationData;
using Jackett.Common.Utils.Clients;
using Jackett.Common.Models.IndexerConfig.Bespoke;

namespace Jackett.Common.Indexers
{
    public class MuleApi : BaseWebIndexer
    {
        
        private string SearchUrl { get { return ConfigDataMule.SiteLink.Value + "search"; } }
        private ConfigurationDataMuleAPI ConfigDataMule => (ConfigurationDataMuleAPI)configData;
        public MuleApi(IIndexerConfigurationService configService, WebClient wc, Logger l, IProtectionService ps)
    : base(name: "E2DK Searcher",
           description: "E2DK Searcher",
           link: "http://192.168.0.15/",
           caps: new TorznabCapabilities(),
           configService: configService,
           client: wc,
           logger: l,
           p: ps,
           configData: new ConfigurationDataMuleAPI())
        {
            Encoding = Encoding.UTF8;
            Language = "es-es";            
            Type = "public";
            AddCategoryMapping(1, TorznabCatType.TVHD, "TV HD");
            AddCategoryMapping(2, TorznabCatType.TVHD, "TV HD No Season");
            AddCategoryMapping(0, TorznabCatType.MoviesHD, "Movies HD");
            AddCategoryMapping(255, TorznabCatType.Other, "Video Others");
        }


        public override async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            configData.LoadValuesFromJson(configJson);
            var releases = await PerformQuery(new TorznabQuery());

            await ConfigureIfOK(string.Empty, releases.Any(), () =>
                throw new Exception("Could not find releases from this URL"));

            return IndexerConfigurationStatus.Completed;
        }

        protected override async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            Boolean filterTVshow = false;
            List<string> trackerCategories = new List<String>();
            if (query.Categories.Length > 0)
            {
                trackerCategories = MapTorznabCapsToTrackers(query);
                if(trackerCategories.Contains("TV HD"))
                {
                    filterTVshow = true;
                }

                /* foreach (int category in query.Categories)
                 {
                     categoryMapping.
                 }*/
            }
            else
            {
                filterTVshow = true;
            }
            List<ReleaseInfo> releases = new List<ReleaseInfo>();
            
            var searchString = query.SearchTerm;
            if(searchString == null)
            {
                searchString = "";
            }
            Regex qariRegex = new Regex("(?<tvshow>(.*)) S(eason)?( *)?(?<season>([0-9]{1,3}))(E(?<episode>([0-9]{1,4})))? ?(?<suffix>(.*))", RegexOptions.IgnoreCase);
            MatchCollection mc = qariRegex.Matches(searchString);
            //We are finding tv shows
            if (mc.Count > 0 && mc[0].Success)
            {
                string substring = searchString.Substring(mc[0].Index, mc[0].Length);
                string tvshow = mc[0].Groups["tvshow"].Value;
                int season = int.Parse(mc[0].Groups["season"].Value);
                string suffix = mc[0].Groups["suffix"].Value;
                if (mc[0].Groups["episode"].Value.Equals(""))
                {
                    releases.AddRange(await performSimpleSearch(string.Format("{0} {1}x {2}", tvshow, season,suffix)));
                }
                else
                {
                    int episode = int.Parse(mc[0].Groups["episode"].Value);
                    releases.AddRange(await performSimpleSearch(string.Format("{0} {1}x{2:00} {3}", tvshow, season, episode, suffix)));
                }
            }
            else
            {
                releases.AddRange(await performSimpleSearch(searchString));
            }            
            return releases;
        }
        public async Task<IEnumerable<ReleaseInfo>> performSimpleSearch(String searchString)
        {
                var releases = new List<ReleaseInfo>();     //List of releases initialization
                                                            //get search string from query
            String searchFullURL = null;
            Boolean filterLanguages = true;
            if (searchString.Length==0)
            {
                searchFullURL = SearchUrl +  "?token=" + ConfigDataMule.Password.Value;
            }
            else
            {
                if (searchString.ToLower().Contains("#nofilterlang"))
                {
                    searchString = searchString.ToLower().Replace("#nofilterlang", "");
                    filterLanguages = false;
                }
                searchFullURL = SearchUrl + "?q=" + searchString.Trim().Replace(" ", "%20") + "&token=" + ConfigDataMule.Password.Value;
            }
            
            WebClientStringResult results = null;
            var queryCollection = new NameValueCollection();

            //concatenate base search url with query            
            var heder = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Accept", "application/json" },
            };

            // log search URL
            //logger.Info(string.Format("Searh URL MuleAPI: {0}", searchFullURL));

            //get results and follow redirect
            results = await RequestStringWithCookies(searchFullURL, null, SearchUrl, heder);                        

            // parse results
            try
            {
                var ResultParser = new HtmlParser();
                var SearchResultDocument = ResultParser.ParseDocument(results.Content);                
                var Rows = SearchResultDocument.QuerySelectorAll("Ed2kFileLinkJSON");
                if (Rows != null) { 
                    foreach (var Row in Rows)
                    {
                        try
                        {
                            // initialize REleaseInfo
                            var release = new ReleaseInfo
                            {
                                MinimumRatio = 1,
                                MinimumSeedTime = 0
                            };
                            //release.PublishDate = DateTime.ParseExact(Row.QuerySelector("Added").TextContent, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                            // Get Category      
                            
                            release.Category = MapTrackerCatToNewznab(Row.QuerySelector("Type").TextContent);
                            // Title and torrent link                            
                            String title = Row.QuerySelector("Name").TextContent;
                            title = title.ToLower().Replace("ª", "").Replace("°", "").Replace("[pack]", "").Replace("."," ").Replace("â"," ").Trim();
                            StringBuilder sb = new StringBuilder(title);
                            sb[title.Length - 4] = '.';
                            title = sb.ToString();


                            if (filterLanguages && !checkRegularExpressionMatching(title, "(dua(l)?|tri?audio|multi)|((es(p)?|spa(nish)?|cast?|cat)( *)[/|\\-|,|\\.|\\+| ]( *)(eng(lish)?|ja?p|v( *)(\\.?)( *)o)( *))|((ja?p|eng?|v( *)(\\.?)( *)o)( *)[/|\\-|\\.|,]( *)(es|spa|cast?|cat))"))
                            {
                                Console.WriteLine(String.Format("{0} not match . DISCARTED", title.ToLower()));
                                continue;
                            }
                            

                            Regex qariRegex = new Regex("((temporada( *)|temp( *)|t)(?<season>[0-9]{1,3})|(?<season>[0-9]{1,3})( *)(temporada|temp))(( *)[x e]( *)(?<episode>[0-9]{1,4}))?");
                            MatchCollection mc = qariRegex.Matches(title);
                            //We are finding tv shows
                            if (mc.Count > 0 && mc[0].Success)
                            {
                                string prefix = title.Substring(0, mc[0].Index);
                                string suffix = title.Substring(mc[0].Index + mc[0].Length);
                                int season = int.Parse(mc[0].Groups["season"].Value);
                                //Find only season
                                if (mc[0].Groups["episode"].Value.Equals(""))
                                {
                                    title = string.Format("{0} S{1:00} {2}", prefix, season, suffix);
                                }
                                else
                                {
                                    int episode = int.Parse(mc[0].Groups["episode"].Value);
                                    title = string.Format("{0} S{1:00}E{2:00} {3}", prefix, season, episode, suffix);
                                }
                            }
                            release.Title = title;
                            //release.Comments = new Uri("http://nocomments");
                            //release.Guid = new Uri("http://nocomments");
                            DateTime date = new DateTime();
                            date = DateTime.Now - TimeSpan.FromHours(12);
                            release.PublishDate = date;
                            var qDownloadLink = Row.QuerySelector("DownloadLink").TextContent;
                            //var qDownloadLink = Row.QuerySelector("e2DK").TextContent;
                            release.Link = new Uri(SiteLink+qDownloadLink);
                            
                            release.Size = long.Parse(Row.QuerySelector("Size").TextContent);
                            release.Guid = release.Link;
                            release.Seeders = Convert.ToInt32(Row.QuerySelector("Avail").TextContent)+2;
                            release.Peers = Convert.ToInt32(Row.QuerySelector("Avail").TextContent)+2;
                            //release.Grabs = ParseUtil.CoerceLong(sel[3].TextContent);

                            // Set download/upload factor
                            release.DownloadVolumeFactor = 1;
                            release.UploadVolumeFactor = 1;

                            // Add current release to List
                            releases.Add(release);
                        }
                        catch (Exception ex)
                        {
                            logger.Error(string.Format("{0}: Error while parsing row '{1}':\n\n{2}", ID, Row.OuterHtml, ex));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnParseError(results.Content, ex);
            }

            return releases;
        }
        private bool checkRegularExpressionMatching(string stringToCheck, string regularExpression)
        {
            Regex qariRegex = new Regex(regularExpression, RegexOptions.IgnoreCase);
            MatchCollection mc = qariRegex.Matches(stringToCheck.ToLower());
            return mc.Count > 0 && mc[0].Success;
        }
    }
   

}
