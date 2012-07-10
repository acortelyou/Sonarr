﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Core.Model;
using NzbDrone.Core.Providers.Core;
using NzbDrone.Core.Repository;
using TvdbLib.Data;
using TvdbLib.Data.Banner;

namespace NzbDrone.Core.Providers.Metadata
{
    public abstract class Xbmc : MetadataBase
    {
        protected readonly Logger _logger;

        public Xbmc(ConfigProvider configProvider, DiskProvider diskProvider, BannerProvider bannerProvider, EpisodeProvider episodeProvider)
            : base(configProvider, diskProvider, bannerProvider, episodeProvider)
        {
        }

        public override string Name
        {
            get { return "XBMC"; }
        }

        public override void ForSeries(Series series, TvdbSeries tvDbSeries)
        {
            //Create tvshow.nfo, fanart.jpg, folder.jpg and searon##.tbn
            var episodeGuideUrl = GetEpisodeGuideUrl(series.SeriesId);

            _logger.Debug("Generating tvshow.nfo for: {0}", series.Title);
            var sb = new StringBuilder();
            var xws = new XmlWriterSettings();
            xws.OmitXmlDeclaration = false;
            xws.Indent = false;

            using (var xw = XmlWriter.Create(sb, xws))
            {
                var tvShow = new XElement("tvshow");
                tvShow.Add(new XElement("title", tvDbSeries.SeriesName));
                tvShow.Add(new XElement("rating", tvDbSeries.Rating));
                tvShow.Add(new XElement("plot", tvDbSeries.Overview));
                tvShow.Add(new XElement("episodeguide", new XElement("url"), episodeGuideUrl));
                tvShow.Add(new XElement("episodeguideurl", episodeGuideUrl));
                tvShow.Add(new XElement("mpaa", tvDbSeries.ContentRating));
                tvShow.Add(new XElement("genre", tvDbSeries.GenreString));
                tvShow.Add(new XElement("premiered", tvDbSeries.FirstAired.ToString("yyyy-MM-dd")));
                tvShow.Add(new XElement("studio", tvDbSeries.Network));

                foreach(var actor in tvDbSeries.TvdbActors)
                {
                    tvShow.Add(new XElement("actor",
                                    new XElement("name", actor.Name),
                                    new XElement("role", actor.Role),
                                    new XElement("thumb", actor.ActorImage)
                            ));
                }

                var doc = new XDocument(tvShow);
                doc.Save(xw);
            }

            _logger.Debug("Saving tvshow.nfo for {0}", series.Title);
            _diskProvider.WriteAllText(Path.Combine(series.Path, "tvshow.nfo"), sb.ToString());
            
            _logger.Debug("Downloading fanart for: {0}", series.Title);
            _bannerProvider.Download(tvDbSeries.FanartPath, Path.Combine(series.Path, "fanart.jpg"));

            if (!_configProvider.MetadataUseBanners)
            {
                _logger.Debug("Downloading series thumbnail for: {0}", series.Title);
                _bannerProvider.Download(tvDbSeries.PosterPath, "folder.jpg");

                _logger.Debug("Downloading Season posters for {0}", series.Title);
                DownloadSeasonThumbnails(series, tvDbSeries, TvdbSeasonBanner.Type.season);
            }

            else
            {
                _logger.Debug("Downloading series banner for: {0}", series.Title);
                _bannerProvider.Download(tvDbSeries.BannerPath, "folder.jpg");

                _logger.Debug("Downloading Season banners for {0}", series.Title);
                DownloadSeasonThumbnails(series, tvDbSeries, TvdbSeasonBanner.Type.seasonwide);
            }
        }

        public override void ForEpisodeFile(EpisodeFile episodeFile, TvdbSeries tvDbSeries)
        {
            //Download filename.tbn and filename.nfo
            //Use BannerPath for Thumbnail
            var episodes = _episodeProvider.GetEpisodesByFileId(episodeFile.EpisodeFileId);

            if (!episodes.Any())
            {
                _logger.Debug("No episodes where found for this episode file: {0}", episodeFile.EpisodeFileId);
                return;
            }

            var episodeFileThumbnail = tvDbSeries.Episodes.FirstOrDefault(
                                                       e =>
                                                       e.SeasonNumber == episodeFile.SeasonNumber &&
                                                       e.EpisodeNumber == episodes.First().EpisodeNumber);

            if (episodeFileThumbnail == null || String.IsNullOrWhiteSpace(episodeFileThumbnail.BannerPath))
            {
                _logger.Debug("No thumbnail is available for this episode");
                return;
            }
            
            _logger.Debug("Downloading episode thumbnail for: {0}", episodeFile.EpisodeFileId);
            _bannerProvider.Download(episodeFileThumbnail.BannerPath, "folder.jpg");

            _logger.Debug("Generating filename.nfo for: {0}", episodeFile.EpisodeFileId);
            var sb = new StringBuilder();
            var xws = new XmlWriterSettings();
            xws.OmitXmlDeclaration = false;
            xws.Indent = false;

            using (var xw = XmlWriter.Create(sb, xws))
            {
                var doc = new XDocument();

                foreach (var episode in episodes)
                {
                    var tvdbEpisode =
                            tvDbSeries.Episodes.FirstOrDefault(
                                                               e =>
                                                               e.SeasonNumber == episode.SeasonNumber &&
                                                               e.EpisodeNumber == episode.EpisodeNumber);

                    if (tvdbEpisode == null)
                    {
                        _logger.Debug("Unable to find episode from TvDb - skipping");
                        return;
                    }

                    var details = new XElement("episodedetails");
                    details.Add(new XElement("title", tvdbEpisode.EpisodeName));
                    details.Add(new XElement("season", tvdbEpisode.SeasonNumber));
                    details.Add(new XElement("episode", tvdbEpisode.EpisodeNumber));
                    details.Add(new XElement("aired", tvdbEpisode.FirstAired));
                    details.Add(new XElement("plot", tvDbSeries.Overview));
                    details.Add(new XElement("displayseason"));
                    details.Add(new XElement("displayepisode"));
                    details.Add(new XElement("thumb", "http://www.thetvdb.com/banners/" + tvdbEpisode.BannerPath));
                    details.Add(new XElement("watched", "false"));
                    details.Add(new XElement("credits", tvdbEpisode.Writer.First()));
                    details.Add(new XElement("director", tvdbEpisode.Directors.First()));
                    details.Add(new XElement("rating", tvDbSeries.Rating));

                    foreach(var actor in tvdbEpisode.GuestStars)
                    {
                        if (!String.IsNullOrWhiteSpace(actor))
                            continue;

                        details.Add(new XElement("actor",
                                                new XElement("name", actor)
                                           ));
                    }

                    foreach(var actor in tvDbSeries.TvdbActors)
                    {
                        details.Add(new XElement("actor",
                                                new XElement("name", actor.Name),
                                                new XElement("role", actor.Role),
                                                new XElement("thumb", actor.ActorImage)
                                           ));
                    }

                    doc.Add(details);
                    doc.Save(xw);
                }
            }

            var filename = Path.GetFileNameWithoutExtension(episodeFile.Path) + ".nfo";
            _logger.Debug("Saving episodedetails to: {0}", filename);
            _diskProvider.WriteAllText(filename, sb.ToString());
        }

        private void DownloadSeasonThumbnails(Series series, TvdbSeries tvDbSeries, TvdbSeasonBanner.Type bannerType)
        {
            var seasons = tvDbSeries.SeasonBanners.Where(s => s.BannerType == bannerType).Select(s => s.Season);

            foreach (var season in seasons)
            {
                var banner = tvDbSeries.SeasonBanners.FirstOrDefault(b => b.BannerType == bannerType && b.Season == season);
                _logger.Debug("Downloading banner for Season: {0} Series: {1}", season, series.Title);
                _bannerProvider.Download(banner.BannerPath,
                                Path.Combine(series.Path, String.Format("season{0:00}.tbn", season)));
            }
        }
    }
}
