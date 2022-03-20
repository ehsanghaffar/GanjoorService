﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RMuseum.DbContext;
using RMuseum.Models.Ganjoor;
using RMuseum.Models.Ganjoor.ViewModels;
using RMuseum.Models.GanjoorAudio;
using RMuseum.Models.GanjoorAudio.ViewModels;
using RSecurityBackend.Models.Generic;
using RSecurityBackend.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using RMuseum.Models.Artifact;
using RSecurityBackend.Services.Implementation;
using DNTPersianUtils.Core;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http;
using System.Web;
using System.Text.RegularExpressions;
using RMuseum.Models.Auth.Memory;

namespace RMuseum.Services.Implementation
{
    /// <summary>
    /// IGanjoorService implementation
    /// </summary>
    public partial class GanjoorService : IGanjoorService
    {

        /// <summary>
        /// Get List of poets
        /// </summary>
        /// <param name="published"></param>
        /// <param name="includeBio"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoetViewModel[]>> GetPoets(bool published, bool includeBio = true)
        {
            var cacheKey = $"/api/ganjoor/poets?published={published}&includeBio={includeBio}";
            if (!_memoryCache.TryGetValue(cacheKey, out GanjoorPoetViewModel[] poets))
            {
                var res =
                 await
                 (from poet in _context.GanjoorPoets.Include(p => p.BirthLocation).Include(p => p.DeathLocation)
                  join cat in _context.GanjoorCategories.Where(c => c.ParentId == null)
                  on poet.Id equals cat.PoetId
                  where !published || poet.Published
                  select new GanjoorPoetViewModel()
                  {
                      Id = poet.Id,
                      Name = poet.Name,
                      Description = includeBio ? poet.Description : null,
                      FullUrl = cat.FullUrl,
                      RootCatId = cat.Id,
                      Nickname = poet.Nickname,
                      Published = poet.Published,
                      ImageUrl = poet.RImageId == null ? "" : $"/api/ganjoor/poet/image{cat.FullUrl}.gif",
                      BirthYearInLHijri = poet.BirthYearInLHijri,
                      DeathYearInLHijri = poet.DeathYearInLHijri,
                      ValidBirthDate = poet.ValidBirthDate,
                      ValidDeathDate = poet.ValidDeathDate,
                      BirthPlace = poet.BirthLocation == null ? "" : poet.BirthLocation.Name,
                      BirthPlaceLatitude = poet.BirthLocation == null ? 0 : poet.BirthLocation.Latitude,
                      BirthPlaceLongitude = poet.BirthLocation == null ? 0 : poet.BirthLocation.Longitude,
                      DeathPlace = poet.DeathLocation == null ? "" : poet.DeathLocation.Name,
                      DeathPlaceLatitude = poet.DeathLocation == null ? 0 : poet.DeathLocation.Latitude,
                      DeathPlaceLongitude = poet.DeathLocation == null ? 0 : poet.DeathLocation.Longitude,
                      PinOrder = poet.PinOrder,
                  }
                  )
                  .AsNoTracking()
                 .ToListAsync();

                StringComparer fa = StringComparer.Create(new CultureInfo("fa-IR"), true);
                res.Sort((a, b) => fa.Compare(a.Nickname, b.Nickname));
                poets = res.ToArray();
                if (AggressiveCacheEnabled)
                    _memoryCache.Set(cacheKey, poets);
            }

            return new RServiceResult<GanjoorPoetViewModel[]>
                (
                    poets
                );
        }

        /// <summary>
        /// get poet by id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="catPoems"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoetCompleteViewModel>> GetPoetById(int id, bool catPoems = false)
        {
            var cacheKey = $"/api/ganjoor/poet/{id}";

            if (!_memoryCache.TryGetValue(cacheKey, out GanjoorPoetCompleteViewModel poetCat))
            {
                var poet = await _context.GanjoorPoets.Where(p => p.Id == id).AsNoTracking().FirstOrDefaultAsync();
                if (poet == null)
                    return new RServiceResult<GanjoorPoetCompleteViewModel>(null);
                var cat = await _context.GanjoorCategories.Where(c => c.ParentId == null && c.PoetId == id).AsNoTracking().FirstOrDefaultAsync();
                poetCat = (await GetCatById(cat.Id, catPoems)).Result;
                if (poetCat != null && AggressiveCacheEnabled)
                {
                    _memoryCache.Set(cacheKey, poetCat);
                }
            }
            return new RServiceResult<GanjoorPoetCompleteViewModel>(poetCat);
        }

        /// <summary>
        /// get poet by url
        /// </summary>
        /// <param name="url"></param>
        /// <param name="catPoems"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoetCompleteViewModel>> GetPoetByUrl(string url, bool catPoems = false)
        {
            // /hafez/ => /hafez :
            if (url.LastIndexOf('/') == url.Length - 1)
            {
                url = url.Substring(0, url.Length - 1);
            }
            var cat = await _context.GanjoorCategories.Where(c => c.FullUrl == url && c.ParentId == null).AsNoTracking().SingleOrDefaultAsync();
            if (cat == null)
                return new RServiceResult<GanjoorPoetCompleteViewModel>(null);
            return await GetCatById(cat.Id, catPoems);
        }

        /// <summary>
        /// poet image id by url
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<RServiceResult<Guid>> GetPoetImageIdByUrl(string url)
        {
            // /hafez/ => /hafez :
            if (url.LastIndexOf('/') == url.Length - 1)
            {
                url = url.Substring(0, url.Length - 1);
            }
            var cat = await _context.GanjoorCategories.Where(c => c.FullUrl == url && c.ParentId == null).AsNoTracking().SingleOrDefaultAsync();
            if (cat == null)
                return new RServiceResult<Guid>(Guid.Empty);
            var poet = await _context.GanjoorPoets.Where(p => p.Id == cat.PoetId).AsNoTracking().SingleOrDefaultAsync();
            return new RServiceResult<Guid>((Guid)poet.RImageId);
        }

        /// <summary>
        /// get cat by url
        /// </summary>
        /// <param name="url"></param>
        /// <param name="poems"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoetCompleteViewModel>> GetCatByUrl(string url, bool poems = false)
        {
            // /hafez/ => /hafez :
            if (url.LastIndexOf('/') == url.Length - 1)
            {
                url = url.Substring(0, url.Length - 1);
            }
            var cat = await _context.GanjoorCategories.Where(c => c.FullUrl == url).AsNoTracking().SingleOrDefaultAsync();
            if (cat == null)
                return new RServiceResult<GanjoorPoetCompleteViewModel>(null);
            return await GetCatById(cat.Id, poems);
        }

        /// <summary>
        /// get cat by id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="poems"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoetCompleteViewModel>> GetCatById(int id, bool poems = false)
        {
            var cat = await _context.GanjoorCategories.Include(c => c.Poet).Include(c => c.Parent).Where(c => c.Id == id).AsNoTracking().FirstOrDefaultAsync();
            if (cat == null)
                return new RServiceResult<GanjoorPoetCompleteViewModel>(null);

            List<GanjoorCatViewModel> ancetors = new List<GanjoorCatViewModel>();

            var parent = cat.Parent;
            while (parent != null)
            {
                ancetors.Insert(0, new GanjoorCatViewModel()
                {
                    Id = parent.Id,
                    Title = parent.Title,
                    UrlSlug = parent.UrlSlug,
                    FullUrl = parent.FullUrl,
                    TableOfContentsStyle = parent.TableOfContentsStyle,
                    CatType = parent.CatType,
                    Description = parent.Description,
                    DescriptionHtml = parent.DescriptionHtml,
                    MixedModeOrder = parent.MixedModeOrder,
                    Published = parent.Published,
                });

                parent = await _context.GanjoorCategories.Where(c => c.Id == parent.ParentId).AsNoTracking().FirstOrDefaultAsync();
            }


            int nextCatId =
                await _context.GanjoorCategories.Where(c => c.PoetId == cat.PoetId && c.ParentId == cat.ParentId && c.Id > id).AnyAsync() ?
                await _context.GanjoorCategories.Where(c => c.PoetId == cat.PoetId && c.ParentId == cat.ParentId && c.Id > id).MinAsync(c => c.Id)
                :
                0;
            var nextCat = nextCatId == 0 ? null : await _context
                                        .GanjoorCategories
                                        .Where(c => c.Id == nextCatId)
                                        .Select
                                        (
                                            c =>
                                                new GanjoorCatViewModel()
                                                {
                                                    Id = c.Id,
                                                    Title = c.Title,
                                                    UrlSlug = c.UrlSlug,
                                                    FullUrl = c.FullUrl,
                                                    TableOfContentsStyle = c.TableOfContentsStyle,
                                                    CatType = c.CatType,
                                                    Description = c.Description,
                                                    DescriptionHtml = c.DescriptionHtml,
                                                    MixedModeOrder = c.MixedModeOrder,
                                                    Published = c.Published,
                                                    //other fields null
                                                }
                                        ).AsNoTracking().SingleOrDefaultAsync();

            int preCatId =
                 await _context.GanjoorCategories.Where(c => c.PoetId == cat.PoetId && c.ParentId == cat.ParentId && c.Id < id).AnyAsync() ?
                await _context.GanjoorCategories.Where(c => c.PoetId == cat.PoetId && c.ParentId == cat.ParentId && c.Id < id).MaxAsync(c => c.Id)
                :
                0;
            var preCat = preCatId == 0 ? null : await _context
                                        .GanjoorCategories
                                        .Where(c => c.Id == preCatId)
                                        .Select
                                        (
                                            c =>
                                                new GanjoorCatViewModel()
                                                {
                                                    Id = c.Id,
                                                    Title = c.Title,
                                                    UrlSlug = c.UrlSlug,
                                                    FullUrl = c.FullUrl,
                                                    TableOfContentsStyle = c.TableOfContentsStyle,
                                                    CatType = c.CatType,
                                                    Description = c.Description,
                                                    DescriptionHtml = c.DescriptionHtml,
                                                    MixedModeOrder = c.MixedModeOrder,
                                                    Published = c.Published,
                                                    //other fields null
                                                }
                                        ).AsNoTracking().SingleOrDefaultAsync();

            GanjoorCatViewModel catViewModel = new GanjoorCatViewModel()
            {
                Id = cat.Id,
                Title = cat.Title,
                UrlSlug = cat.UrlSlug,
                FullUrl = cat.FullUrl,
                TableOfContentsStyle = cat.TableOfContentsStyle,
                CatType = cat.CatType,
                Description = cat.Description,
                DescriptionHtml = cat.DescriptionHtml,
                MixedModeOrder = cat.MixedModeOrder,
                Published = cat.Published,
                Next = nextCat,
                Previous = preCat,
                Ancestors = ancetors,
                Children = await _context.GanjoorCategories.Where(c => c.ParentId == cat.Id).OrderBy(cat => cat.Id).Select
                 (
                 c => new GanjoorCatViewModel()
                 {
                     Id = c.Id,
                     Title = c.Title,
                     UrlSlug = c.UrlSlug,
                     FullUrl = c.FullUrl,
                     MixedModeOrder = c.MixedModeOrder,
                     Published = c.Published,
                 }
                 ).AsNoTracking().ToListAsync(),
                Poems = poems ? await _context.GanjoorPoems.Include(p => p.GanjoorMetre)
                .Where(p => p.CatId == cat.Id).OrderBy(p => p.Id).Select
                 (
                     p => new GanjoorPoemSummaryViewModel()
                     {
                         Id = p.Id,
                         Title = p.Title,
                         UrlSlug = p.UrlSlug,
                         Excerpt = _context.GanjoorVerses.Where(v => v.PoemId == p.Id && v.VOrder == 1).FirstOrDefault().Text,
                         Rhythm = p.GanjoorMetre.Rhythm,
                         RhymeLetters = p.RhymeLetters
                     }
                 ).AsNoTracking().ToListAsync()
                 :
                 null
            };

            return new RServiceResult<GanjoorPoetCompleteViewModel>
               (
               new GanjoorPoetCompleteViewModel()
               {
                   Poet = await _context.GanjoorPoets.Include(p => p.BirthLocation).Include(p => p.DeathLocation).Where(p => p.Id == cat.PoetId)
                                        .Select(poet => new GanjoorPoetViewModel()
                                        {
                                            Id = poet.Id,
                                            Name = poet.Name,
                                            Description = poet.Description,
                                            FullUrl = _context.GanjoorCategories.Where(c => c.PoetId == poet.Id && c.ParentId == null).Single().FullUrl,
                                            RootCatId = _context.GanjoorCategories.Where(c => c.PoetId == poet.Id && c.ParentId == null).Single().Id,
                                            Nickname = poet.Nickname,
                                            Published = poet.Published,
                                            ImageUrl = poet.RImageId == null ? "" : $"/api/ganjoor/poet/image{_context.GanjoorCategories.Where(c => c.PoetId == poet.Id && c.ParentId == null).Single().FullUrl}.gif",
                                            BirthYearInLHijri = poet.BirthYearInLHijri,
                                            DeathYearInLHijri = poet.DeathYearInLHijri,
                                            ValidBirthDate = poet.ValidBirthDate,
                                            ValidDeathDate = poet.ValidDeathDate,
                                            PinOrder = poet.PinOrder,
                                            BirthPlace = poet.BirthLocation == null ? "" : poet.BirthLocation.Name,
                                            BirthPlaceLatitude = poet.BirthLocation == null ? 0 : poet.BirthLocation.Latitude,
                                            BirthPlaceLongitude = poet.BirthLocation == null ? 0 : poet.BirthLocation.Longitude,
                                            DeathPlace = poet.DeathLocation == null ? "" : poet.DeathLocation.Name,
                                            DeathPlaceLatitude = poet.DeathLocation == null ? 0 : poet.DeathLocation.Latitude,
                                            DeathPlaceLongitude = poet.DeathLocation == null ? 0 : poet.DeathLocation.Longitude,
                                        }).AsNoTracking().FirstOrDefaultAsync(),
                   Cat = catViewModel
               }
               );
        }

        /// <summary>
        /// get page url by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<string>> GetPageUrlById(int id)
        {
            var dbPage = await _context.GanjoorPages.Where(p => p.Id == id).AsNoTracking().SingleOrDefaultAsync();
            if (dbPage == null)
                return new RServiceResult<string>(null); //not found
            return new RServiceResult<string>(dbPage.FullUrl);
        }

        /// <summary>
        /// clean cache for paeg by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task CacheCleanForPageById(int id)
        {
            var dbPage = await _context.GanjoorPages.Where(p => p.Id == id).AsNoTracking().SingleOrDefaultAsync();
            if (dbPage != null)
            {
                CacheCleanForPageByUrl(dbPage.FullUrl);
            }
        }

        /// <summary>
        /// clean cache for page by url
        /// </summary>
        /// <param name="url"></param>
        public void CacheCleanForPageByUrl(string url)
        {
            var cachKey = $"GanjoorService::GetPageByUrl::{url}";
            if (_memoryCache.TryGetValue(cachKey, out GanjoorPageCompleteViewModel page))
            {
                _memoryCache.Remove(cachKey);

                var poemCachKey = $"GetPoemById({page.Id}, {true}, {false}, {true}, {true}, {true}, {true}, {true}, {true}, {true})";
                if (_memoryCache.TryGetValue(poemCachKey, out GanjoorPoemCompleteViewModel p))
                {
                    _memoryCache.Remove(poemCachKey);
                }
            }
        }

        /// <summary>
        /// clean cache for page by comment
        /// </summary>
        /// <param name="commentId"></param>
        /// <returns></returns>
        public async Task CacheCleanForComment(int commentId)
        {
            var comment = await _context.GanjoorComments.Where(c => c.Id == commentId).SingleOrDefaultAsync();
            if (comment != null)
            {
                await CacheCleanForPageById(comment.PoemId);
            }
        }

        /// <summary>
        /// get redirect url for a url
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<RServiceResult<string>> GetRedirectAddressForPageUrl(string url)
        {
            if (url.IndexOf('?') != -1)
            {
                url = url.Substring(0, url.IndexOf('?'));
            }

            // /hafez/ => /hafez :
            if (url.LastIndexOf('/') == url.Length - 1)
            {
                url = url.Substring(0, url.Length - 1);
            }

            url = url.Replace("//", "/"); //duplicated slashes would be merged

            var pages = await _context.GanjoorPages.Where(p => url.StartsWith(p.RedirectFromFullUrl)).AsNoTracking().ToListAsync();
            if (pages.Count == 0)
                return new RServiceResult<string>(null); //not found

            var dbPage = pages[0];
            for (int i= 1; i < pages.Count; i++)
            {
                if(pages[i].RedirectFromFullUrl.Length > dbPage.RedirectFromFullUrl.Length)
                {
                    dbPage = pages[i];
                }
            }

            var target = dbPage.FullUrl;
            if(url != dbPage.RedirectFromFullUrl)
            {
                target = dbPage.FullUrl + url.Substring(dbPage.RedirectFromFullUrl.Length);
            }

            return new RServiceResult<string>(target);
        }

        /// <summary>
        /// get page by url
        /// </summary>
        /// <param name="url"></param>
        /// <param name="catPoems"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPageCompleteViewModel>> GetPageByUrl(string url, bool catPoems = false)
        {
            if (url.IndexOf('?') != -1)
            {
                url = url.Substring(0, url.IndexOf('?'));
            }

            // /hafez/ => /hafez :
            if (url.LastIndexOf('/') == url.Length - 1)
            {
                url = url.Substring(0, url.Length - 1);
            }

            url = url.Replace("//", "/"); //duplicated slashes would be merged

            var cachKey = $"GanjoorService::GetPageByUrl::{url}";
            if (!_memoryCache.TryGetValue(cachKey, out GanjoorPageCompleteViewModel page))
            {
                var dbPage = await _context.GanjoorPages.Where(p => p.FullUrl == url).AsNoTracking().SingleOrDefaultAsync();
                if (dbPage == null)
                    return new RServiceResult<GanjoorPageCompleteViewModel>(null); //not found
                var secondPoet = dbPage.SecondPoetId == null ? null :
                     await
                     (from poet in _context.GanjoorPoets
                      join cat in _context.GanjoorCategories.Where(c => c.ParentId == null)
                      on poet.Id equals cat.PoetId
                      where poet.Id == (int)dbPage.SecondPoetId
                      orderby poet.Name descending
                      select new GanjoorPoetViewModel()
                      {
                          Id = poet.Id,
                          Name = poet.Name,
                          FullUrl = cat.FullUrl,
                          RootCatId = cat.Id,
                          Nickname = poet.Nickname,
                          Published = poet.Published,
                          ImageUrl = poet.RImageId == null ? "" : $"/api/ganjoor/poet/image{cat.FullUrl}.gif",
                          BirthYearInLHijri = poet.BirthYearInLHijri,
                          ValidBirthDate = poet.ValidBirthDate,
                          ValidDeathDate = poet.ValidDeathDate,
                          DeathYearInLHijri = poet.DeathYearInLHijri,
                          PinOrder = poet.PinOrder,
                      }
                      )
                     .AsNoTracking().SingleAsync();
                page = new GanjoorPageCompleteViewModel()
                {
                    Id = dbPage.Id,
                    GanjoorPageType = dbPage.GanjoorPageType,
                    Title = dbPage.Title,
                    FullTitle = dbPage.FullTitle,
                    UrlSlug = dbPage.UrlSlug,
                    FullUrl = dbPage.FullUrl,
                    HtmlText = dbPage.HtmlText,
                    SecondPoet = secondPoet,
                    NoIndex = dbPage.NoIndex,
                    RedirectFromFullUrl = dbPage.RedirectFromFullUrl,

                };
                switch (page.GanjoorPageType)
                {
                    case GanjoorPageType.PoemPage:
                        {
                            var poemRes = await GetPoemById((int)dbPage.PoemId);
                            if (!string.IsNullOrEmpty(poemRes.ExceptionString))
                            {
                                return new RServiceResult<GanjoorPageCompleteViewModel>(null, poemRes.ExceptionString);
                            }
                            page.Poem = poemRes.Result;
                        }
                        break;

                    case GanjoorPageType.CatPage:
                        {
                            var catRes = await GetCatById((int)dbPage.CatId, catPoems);
                            if (!string.IsNullOrEmpty(catRes.ExceptionString))
                            {
                                return new RServiceResult<GanjoorPageCompleteViewModel>(null, catRes.ExceptionString);
                            }
                            page.PoetOrCat = catRes.Result;
                        }
                        break;
                    default:
                        {
                            if (dbPage.PoetId != null)
                            {
                                var poetRes = await GetPoetById((int)dbPage.PoetId, catPoems);
                                if (!string.IsNullOrEmpty(poetRes.ExceptionString))
                                {
                                    return new RServiceResult<GanjoorPageCompleteViewModel>(null, poetRes.ExceptionString);
                                }
                                page.PoetOrCat = poetRes.Result;

                                var pre = await _context.GanjoorPages.Where(p => p.GanjoorPageType == page.GanjoorPageType && p.ParentId == dbPage.ParentId && p.PoetId == dbPage.PoetId &&
                                    ((p.PageOrder < dbPage.PageOrder) || (p.PageOrder == dbPage.PageOrder && p.Id < dbPage.Id)))
                                    .OrderByDescending(p => p.PageOrder)
                                    .ThenByDescending(p => p.Id)
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync();
                                if (pre != null)
                                {
                                    page.Previous = new GanjoorPageSummaryViewModel()
                                    {
                                        Id = pre.Id,
                                        Title = pre.Title,
                                        FullUrl = pre.FullUrl
                                    };
                                }

                                var next = await _context.GanjoorPages.Where(p => p.GanjoorPageType == page.GanjoorPageType && p.ParentId == dbPage.ParentId && p.PoetId == dbPage.PoetId &&
                                    ((p.PageOrder > dbPage.PageOrder) || (p.PageOrder == dbPage.PageOrder && p.Id > dbPage.Id)))
                                    .OrderBy(p => p.PageOrder)
                                    .ThenBy(p => p.Id)
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync();
                                if (next != null)
                                {
                                    page.Next = new GanjoorPageSummaryViewModel()
                                    {
                                        Id = next.Id,
                                        Title = next.Title,
                                        FullUrl = next.FullUrl
                                    };
                                }
                            }
                        }
                        break;
                }
                if (AggressiveCacheEnabled)
                {
                    _memoryCache.Set(cachKey, page);
                }
            }

            return new RServiceResult<GanjoorPageCompleteViewModel>(page);
        }



        /// <summary>
        /// get poem recitations  (PlainText/HtmlText are intentionally empty)
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<PublicRecitationViewModel[]>> GetPoemRecitations(int id)
        {
            var source =
                 from audio in _context.Recitations
                 join poem in _context.GanjoorPoems
                 on audio.GanjoorPostId equals poem.Id
                 where
                 audio.ReviewStatus == AudioReviewStatus.Approved
                 &&
                 poem.Id == id
                 orderby audio.AudioOrder
                 select new PublicRecitationViewModel()
                 {
                     Id = audio.Id,
                     PoemId = audio.GanjoorPostId,
                     PoemFullTitle = poem.FullTitle,
                     PoemFullUrl = poem.FullUrl,
                     AudioTitle = audio.AudioTitle,
                     AudioArtist = audio.AudioArtist,
                     AudioArtistUrl = audio.AudioArtistUrl,
                     AudioSrc = audio.AudioSrc,
                     AudioSrcUrl = audio.AudioSrcUrl,
                     LegacyAudioGuid = audio.LegacyAudioGuid,
                     Mp3FileCheckSum = audio.Mp3FileCheckSum,
                     Mp3SizeInBytes = audio.Mp3SizeInBytes,
                     PublishDate = audio.ReviewDate,
                     FileLastUpdated = audio.FileLastUpdated,
                     Mp3Url = $"{WebServiceUrl.Url}/api/audio/file/{audio.Id}.mp3",
                     XmlText = $"{WebServiceUrl.Url}/api/audio/xml/{audio.Id}",
                     PlainText = "", //poem.PlainText 
                     HtmlText = "",//poem.HtmlText
                 };
            var recitations = await source.AsNoTracking().ToArrayAsync();
            foreach (var recitation in recitations)
            {
                recitation.Mistakes =
                    await _context.RecitationApprovedMistakes.AsNoTracking()
                          .Where(m => m.RecitationId == recitation.Id)
                          .Select(m => new RecitationMistakeViewModel()
                          {
                              Mistake = m.Mistake,
                              NumberOfLinesAffected = m.NumberOfLinesAffected,
                              CoupletIndex = m.CoupletIndex
                          }).ToArrayAsync();
            }
            return new RServiceResult<PublicRecitationViewModel[]>(recitations);
        }

        /// <summary>
        /// get user up votes for the recitations of a poem
        /// </summary>
        /// <param name="id"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<int[]>> GetUserPoemRecitationsUpVotes(int id, Guid userId)
        {
            var source =
                 from audio in _context.Recitations
                 join poem in _context.GanjoorPoems
                 on audio.GanjoorPostId equals poem.Id
                 where
                 audio.ReviewStatus == AudioReviewStatus.Approved
                 &&
                 poem.Id == id
                 orderby audio.AudioOrder
                 select audio.Id;

            List<int> upVotedRecitations = new List<int>();
            var recitationIds = await source.ToArrayAsync();
            foreach (var recitationId in recitationIds)
            {
                if (await _context.RecitationUserUpVotes.Where(v => v.RecitationId == recitationId && v.UserId == userId).AnyAsync())
                {
                    upVotedRecitations.Add(recitationId);
                }
            }

            return new RServiceResult<int[]>(upVotedRecitations.ToArray());
        }

        private async Task _FillPoemCoupletIndices(RMuseumDbContext context, int poemId)
        {
            var verses = await context.GanjoorVerses.Where(v => v.PoemId == poemId).OrderBy(v => v.VOrder).ToListAsync();
            int cIndex = -1;
            foreach (var verse in verses)
            {
                if (verse.VersePosition != VersePosition.Left && verse.VersePosition != VersePosition.CenteredVerse2 && verse.VersePosition != VersePosition.Comment)
                    cIndex++;
                if (verse.VersePosition != VersePosition.Comment)
                {
                    verse.CoupletIndex = cIndex;
                }
                else
                {
                    verse.CoupletIndex = null;
                }
            }
            context.GanjoorVerses.UpdateRange(verses);
        }


        /// <summary>
        /// get poem comments
        /// </summary>
        /// <param name="poemId"></param>
        /// <param name="userId"></param>
        /// <param name="coupletIndex"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorCommentSummaryViewModel[]>> GetPoemComments(int poemId, Guid userId, int? coupletIndex)
        {
            var source =
                  from comment in _context.GanjoorComments.Include(c => c.User)
                  where
                  (comment.Status == PublishStatus.Published || (userId != Guid.Empty && comment.Status == PublishStatus.Awaiting && comment.UserId == userId))
                  &&
                  comment.PoemId == poemId
                  &&
                  ((coupletIndex == null) || (coupletIndex != null && comment.CoupletIndex == coupletIndex))
                  orderby comment.CommentDate
                  select new GanjoorCommentSummaryViewModel()
                  {
                      Id = comment.Id,
                      AuthorName = comment.User == null ? comment.AuthorName : $"{comment.User.NickName}",
                      AuthorUrl = comment.AuthorUrl,
                      CommentDate = comment.CommentDate,
                      HtmlComment = comment.HtmlComment,
                      PublishStatus = comment.Status == PublishStatus.Awaiting ? "در انتظار تأیید" : "",
                      InReplyToId = comment.InReplyToId,
                      UserId = comment.UserId,
                      CoupletIndex = comment.CoupletIndex == null ? -1 : (int)comment.CoupletIndex,
                  };

            GanjoorCommentSummaryViewModel[] allComments = await source.AsNoTracking().ToArrayAsync();

            foreach (GanjoorCommentSummaryViewModel comment in allComments)
            {
                comment.AuthorName = comment.AuthorName.ToPersianNumbers().ApplyCorrectYeKe();
                var relatedVerses = comment.CoupletIndex == -1 ? new List<GanjoorVerse>() : await _context.GanjoorVerses.Where(v => v.PoemId == poemId && v.CoupletIndex == comment.CoupletIndex).OrderBy(v => v.VOrder).ToListAsync();
                string coupleText = relatedVerses.Count == 0 ? "" : relatedVerses[0].Text;
                for (int nVerseIndex = 1; nVerseIndex < relatedVerses.Count; nVerseIndex++)
                {
                    coupleText += $" {relatedVerses[nVerseIndex].Text}";
                }
                comment.CoupletSummary = _CutSummary(coupleText);
            }

            GanjoorCommentSummaryViewModel[] rootComments = allComments.Where(c => c.InReplyToId == null).ToArray();

            foreach (GanjoorCommentSummaryViewModel comment in rootComments)
            {
                _FindReplies(comment, allComments);
            }
            return new RServiceResult<GanjoorCommentSummaryViewModel[]>(rootComments);
        }

        private void _FindReplies(GanjoorCommentSummaryViewModel comment, GanjoorCommentSummaryViewModel[] allComments)
        {
            comment.Replies = allComments.Where(c => c.InReplyToId == comment.Id).ToArray();
            foreach (GanjoorCommentSummaryViewModel reply in comment.Replies)
            {
                _FindReplies(reply, allComments);
            }
        }

        /// <summary>
        /// new comment
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="ip"></param>
        /// <param name="poemId"></param>
        /// <param name="content"></param>
        /// <param name="inReplyTo"></param>
        /// <param name="coupletIndex"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorCommentSummaryViewModel>> NewComment(Guid userId, string ip, int poemId, string content, int? inReplyTo, int? coupletIndex)
        {
            if (string.IsNullOrEmpty(content))
            {
                return new RServiceResult<GanjoorCommentSummaryViewModel>(null, "متن حاشیه خالی است.");
            }

            var userRes = await _appUserService.GetUserInformation(userId);

            if (string.IsNullOrEmpty(userRes.Result.NickName))
            {
                return new RServiceResult<GanjoorCommentSummaryViewModel>(null, "لطفاً با مراجعه به پیشخان کاربری (دکمهٔ گوشهٔ پایین سمت چپ) «نام مستعار» خود را مشخص کنید و سپس اقدام به ارسال حاشیه بفرمایید.");
            }

            string coupletSummary = "";
            if (inReplyTo != null)
            {
                GanjoorComment refComment = await _context.GanjoorComments.Where(c => c.Id == (int)inReplyTo).SingleAsync();
                coupletIndex = refComment.CoupletIndex;
                if (refComment.CoupletIndex != null)
                {
                    var relatedVerses = refComment.CoupletIndex == -1 ? new List<GanjoorVerse>() : await _context.GanjoorVerses.Where(v => v.PoemId == poemId && v.CoupletIndex == refComment.CoupletIndex).OrderBy(v => v.VOrder).ToListAsync();
                    coupletSummary = relatedVerses.Count > 0 ? relatedVerses[0].Text : "";
                    if (relatedVerses.Count > 1)
                    {
                        coupletSummary += $" {relatedVerses[1].Text}";
                    }
                }
            }
            else
            if (coupletIndex != null)
            {
                var relatedVerses = await _context.GanjoorVerses.Where(v => v.PoemId == poemId && v.CoupletIndex == coupletIndex).OrderBy(v => v.VOrder).ToListAsync();
                coupletSummary = relatedVerses.Count > 0 ? relatedVerses[0].Text : "";
                if (relatedVerses.Count > 1)
                {
                    coupletSummary += $" {relatedVerses[1].Text}";
                }
            }

            content = content.ApplyCorrectYeKe();

            content = await _ProcessCommentHtml(content, _context);

            string commentText = Regex.Replace(content, "<.*?>", string.Empty);
            if (commentText.Split(" ", StringSplitOptions.RemoveEmptyEntries).Max(s => s.Length) > 50)
            {
                return new RServiceResult<GanjoorCommentSummaryViewModel>(null, "متن حاشیه شامل کلمات به هم پیوستهٔ طولانی است.");
            }

            if (string.IsNullOrEmpty(commentText))
            {
                return new RServiceResult<GanjoorCommentSummaryViewModel>(null, "متن حاشیه خالی است.");
            }

            PublishStatus status = PublishStatus.Published;
            var keepFirstTimeUsersComments = await _optionsService.GetValueAsync("KeepFirstTimeUsersComments", null);
            if (keepFirstTimeUsersComments.Result == true.ToString())
            {
                if((await _context.GanjoorComments.AsNoTracking().Where(c => c.UserId == userId && c.Status == PublishStatus.Published).AnyAsync()) == false)//First time commenter
                {
                    status = PublishStatus.Awaiting;
                }
            }

            GanjoorComment comment = new GanjoorComment()
            {
                UserId = userId,
                AuthorIpAddress = ip,
                CommentDate = DateTime.Now,
                HtmlComment = content,
                InReplyToId = inReplyTo,
                PoemId = poemId,
                Status = status,
                CoupletIndex = coupletIndex,
            };

            _context.GanjoorComments.Add(comment);
            await _context.SaveChangesAsync();

            if (inReplyTo != null)
            {
                GanjoorComment refComment = await _context.GanjoorComments.Where(c => c.Id == (int)inReplyTo).SingleAsync();
                if (refComment.UserId != null)
                {

                    var poem = await _context.GanjoorPoems.Where(p => p.Id == comment.PoemId).SingleAsync();

                    await _notificationService.PushNotification((Guid)refComment.UserId,
                                       "پاسخ به حاشیهٔ شما",
                                       $"{userRes.Result.NickName} برای حاشیهٔ شما روی <a href=\"{poem.FullUrl}\">{poem.FullTitle}</a> این پاسخ را نوشته است: {Environment.NewLine}" +
                                       $"{content}" +
                                       $"این متن حاشیهٔ خود شماست: {Environment.NewLine}" +
                                       $"{refComment.HtmlComment}"
                                       );
                }
            }

            await CacheCleanForPageById(poemId);


            return new RServiceResult<GanjoorCommentSummaryViewModel>
                (
                new GanjoorCommentSummaryViewModel()
                {
                    Id = comment.Id,
                    AuthorName = $"{userRes.Result.NickName}",
                    AuthorUrl = comment.AuthorUrl,
                    CommentDate = comment.CommentDate,
                    HtmlComment = comment.HtmlComment,
                    PublishStatus = comment.Status == PublishStatus.Awaiting ? "در انتظار تأیید" : "",
                    InReplyToId = comment.InReplyToId,
                    UserId = comment.UserId,
                    Replies = new GanjoorCommentSummaryViewModel[] { },
                    CoupletIndex = coupletIndex == null ? -1 : (int)coupletIndex,
                    MyComment = true,
                    CoupletSummary = _CutSummary(coupletSummary)
                }
                );
        }

        private string _CutSummary(string summary)
        {
            if (summary.Length > 50)
            {
                summary = summary.Substring(0, 30);
                int n = summary.LastIndexOf(' ');
                if (n >= 0)
                {
                    summary = summary.Substring(0, n) + " ...";
                }
                else
                {
                    summary += "...";
                }
            }
            return summary;
        }

        /// <summary>
        /// update user's own comment
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="commentId"></param>
        /// <param name="htmlComment"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> EditMyComment(Guid userId, int commentId, string htmlComment)
        {
            GanjoorComment comment = await _context.GanjoorComments.Where(c => c.Id == commentId && c.UserId == userId).SingleOrDefaultAsync();//userId is not part of key but it helps making call secure
            if (comment == null)
            {
                return new RServiceResult<bool>(false); //not found
            }

            await CacheCleanForComment(commentId);

            htmlComment = htmlComment.ApplyCorrectYeKe();

            htmlComment = await _ProcessCommentHtml(htmlComment, _context);

            comment.HtmlComment = htmlComment;

            _context.GanjoorComments.Update(comment);
            await _context.SaveChangesAsync();

            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// link or unlink user's own comment to a coupletIndex
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="commentId"></param>
        /// <param name="coupletIndex">if null then unlinks</param>
        /// <returns>couplet summary</returns>
        public async Task<RServiceResult<string>> LinkUnLinkMyComment(Guid userId, int commentId, int? coupletIndex)
        {
            var userRes = await _appUserService.GetUserInformation(userId);

            if (string.IsNullOrEmpty(userRes.Result.NickName))
            {
                return new RServiceResult<string>(null, "لطفاً با مراجعه به پیشخان کاربری (دکمهٔ گوشهٔ پایین سمت چپ) «نام مستعار» خود را مشخص کنید و سپس اقدام به ارسال حاشیه بفرمایید.");
            }

            GanjoorComment comment = await _context.GanjoorComments.Where(c => c.Id == commentId && c.UserId == userId).SingleOrDefaultAsync();//userId is not part of key but it helps making call secure
            if (comment == null)
            {
                return new RServiceResult<string>(null); //not found
            }

            await CacheCleanForComment(commentId);



            comment.CoupletIndex = coupletIndex;

            string coupletSummary = "";
            if (coupletIndex != null)
            {
                var relatedVerses = await _context.GanjoorVerses.Where(v => v.PoemId == comment.PoemId && v.CoupletIndex == coupletIndex).OrderBy(v => v.VOrder).ToListAsync();
                coupletSummary = relatedVerses.Count > 0 ? relatedVerses[0].Text : "";
                if (relatedVerses.Count > 1)
                {
                    coupletSummary += $" {relatedVerses[1].Text}";
                }
            }

            _context.GanjoorComments.Update(comment);
            await _context.SaveChangesAsync();

            return new RServiceResult<string>
                (
               coupletSummary
                );
        }

        /// <summary>
        /// delete a reported  comment
        /// </summary>
        /// <param name="repordId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> DeleteModerateComment(int repordId)
        {
            GanjoorCommentAbuseReport report = await _context.GanjoorReportedComments.Where(r => r.Id == repordId).SingleOrDefaultAsync();
            if (report == null)
            {
                return new RServiceResult<bool>(false);
            }


            GanjoorComment comment = await _context.GanjoorComments.Where(c => c.Id == report.GanjoorCommentId).SingleOrDefaultAsync();
            if (comment == null)
            {
                return new RServiceResult<bool>(false); //not found
            }

            var commentId = report.GanjoorCommentId;


            if (comment.UserId != null)
            {
                string reason = "";
                switch (report.ReasonCode)
                {
                    case "offensive":
                        reason = "توهین‌آمیز است.";
                        break;
                    case "religious":
                        reason = "بحث مذهبی کرده.";
                        break;
                    case "repeated":
                        reason = "تکراری است.";
                        break;
                    case "unrelated":
                        reason = "به این شعر ربطی ندارد.";
                        break;
                    case "brokenlink":
                        reason = "لینک شکسته است.";
                        break;
                    case "ad":
                        reason = "تبلیغاتی است.";
                        break;
                    case "bogus":
                        reason = "نامفهوم است.";
                        break;
                    case "latin":
                        reason = "فارسی ننوشته.";
                        break;
                    case "other":
                        reason = "دلیل دیگر";
                        break;
                }
                if (!string.IsNullOrEmpty(report.ReasonText))
                    reason += $" {report.ReasonText}";
                reason = reason.Trim();
                reason = string.IsNullOrEmpty(reason) ? "" : $"علت ارائه شده برای حذف یا متن گزارش کاربر شاکی: {Environment.NewLine}" +
                                       $"{reason} {Environment.NewLine}";
                await _notificationService.PushNotification((Guid)comment.UserId,
                                       "حذف حاشیهٔ شما",
                                       $"حاشیهٔ شما به دلیل ناسازگاری با قوانین حاشیه‌گذاری گنجور و طبق گزارشات دیگر کاربران حذف شده است.{Environment.NewLine}" +
                                       $"{reason}" +
                                       $"این متن حاشیهٔ حذف شدهٔ شماست: {Environment.NewLine}" +
                                       $"{comment.HtmlComment}"
                                       );
            }

            //if user has got replies, delete them and notify their owners of what happened
            var replies = await _FindReplies(comment);
            for (int i = replies.Count - 1; i >= 0; i--)
            {
                if (replies[i].UserId != null)
                {
                    await _notificationService.PushNotification((Guid)replies[i].UserId,
                                           "حذف پاسخ شما به حاشیه",
                                           $"پاسخ شما به یکی از حاشیه‌های گنجور به دلیل حذف زنجیرهٔ حاشیه توسط یکی از حاشیه‌گذاران حذف شده است.{Environment.NewLine}" +
                                           $"این متن حاشیهٔ حذف شدهٔ شماست: {Environment.NewLine}" +
                                           $"{replies[i].HtmlComment}"
                                           );
                }
                _context.GanjoorComments.Remove(replies[i]);
            }

            _context.GanjoorComments.Remove(comment);
            await _context.SaveChangesAsync();

            await CacheCleanForComment(report.GanjoorCommentId);

            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// publish awaiting comment
        /// </summary>
        /// <param name="commentId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> PublishAwaitingComment(int commentId)
        {
            GanjoorComment comment = await _context.GanjoorComments.Where(c => c.Id == commentId).SingleOrDefaultAsync();//userId is not part of key but it helps making call secure
            if (comment == null)
            {
                return new RServiceResult<bool>(false); //not found
            }
            comment.Status = PublishStatus.Published;
            _context.Update(comment);
            await _context.SaveChangesAsync();
            return new RServiceResult<bool>(true);
            
        }

        /// <summary>
        /// delete anybody's comment
        /// </summary>
        /// <param name="commentId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> DeleteAnybodyComment(int commentId)
        {
            GanjoorComment comment = await _context.GanjoorComments.AsNoTracking().Where(c => c.Id == commentId).SingleOrDefaultAsync();//userId is not part of key but it helps making call secure
            if (comment == null)
            {
                return new RServiceResult<bool>(false); //not found
            }

            return await DeleteMyComment((Guid)comment.UserId, commentId);
        }


        /// <summary>
        /// delete user own comment
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="commentId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> DeleteMyComment(Guid userId, int commentId)
        {
            GanjoorComment comment = await _context.GanjoorComments.Where(c => c.Id == commentId && c.UserId == userId).SingleOrDefaultAsync();//userId is not part of key but it helps making call secure
            if (comment == null)
            {
                return new RServiceResult<bool>(false); //not found
            }

            await CacheCleanForComment(commentId);

            //if user has got replies, delete them and notify their owners of what happened
            var replies = await _FindReplies(comment);
            for (int i = replies.Count - 1; i >= 0; i--)
            {
                if (replies[i].UserId != null && replies[i].UserId != userId)
                {
                    await _notificationService.PushNotification((Guid)replies[i].UserId,
                                           "حذف پاسخ شما به حاشیه",
                                           $"پاسخ شما به یکی از حاشیه‌های گنجور به دلیل حذف زنجیرهٔ حاشیه توسط یکی از حاشیه‌گذاران حذف شده است.{Environment.NewLine}" +
                                           $"این متن حاشیهٔ حذف شدهٔ شماست: {Environment.NewLine}" +
                                           $"{replies[i].HtmlComment}"
                                           );
                }
                _context.GanjoorComments.Remove(replies[i]);
            }

            _context.GanjoorComments.Remove(comment);
            await _context.SaveChangesAsync();

            return new RServiceResult<bool>(true);
        }

        private async Task<List<GanjoorComment>> _FindReplies(GanjoorComment comment)
        {
            List<GanjoorComment> replies = await _context.GanjoorComments.Where(c => c.InReplyToId == comment.Id).AsNoTracking().ToListAsync();
            List<GanjoorComment> replyToReplies = new List<GanjoorComment>();
            foreach (GanjoorComment reply in replies)
            {
                replyToReplies.AddRange(await _FindReplies(reply));
            }
            if (replyToReplies.Count > 0)
            {
                replies.AddRange(replyToReplies);
            }
            return replies;
        }


        /// <summary>
        /// get recent comments
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="filterUserId"></param>
        /// <param name="onlyPublished"></param>
        /// <param name="onlyAwaiting"></param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, GanjoorCommentFullViewModel[] Items)>> GetRecentComments(PagingParameterModel paging, Guid filterUserId, bool onlyPublished, bool onlyAwaiting = false)
        {
            var source =
                 from comment in _context.GanjoorComments.Include(c => c.Poem).Include(c => c.User).Include(c => c.InReplyTo).ThenInclude(r => r.User)
                 where
                  ((comment.Status == PublishStatus.Published) || !onlyPublished)
                  &&
                  ((comment.Status == PublishStatus.Awaiting) || !onlyAwaiting)
                 &&
                 ((filterUserId == Guid.Empty) || (filterUserId != Guid.Empty && comment.UserId == filterUserId))
                 orderby comment.CommentDate descending
                 select new GanjoorCommentFullViewModel()
                 {
                     Id = comment.Id,
                     AuthorName = comment.User == null ? comment.AuthorName : $"{comment.User.NickName}",
                     AuthorUrl = comment.AuthorUrl,
                     CommentDate = comment.CommentDate,
                     HtmlComment = comment.HtmlComment,
                     PublishStatus = "",//invalid!
                     UserId = comment.UserId,
                     CoupletIndex = comment.CoupletIndex == null ? -1 : (int)comment.CoupletIndex,
                     InReplyTo = comment.InReplyTo == null ? null :
                        new GanjoorCommentSummaryViewModel()
                        {
                            Id = comment.InReplyTo.Id,
                            AuthorName = comment.InReplyTo.User == null ? comment.InReplyTo.AuthorName : $"{comment.InReplyTo.User.NickName}",
                            AuthorUrl = comment.InReplyTo.AuthorUrl,
                            CommentDate = comment.InReplyTo.CommentDate,
                            HtmlComment = comment.InReplyTo.HtmlComment,
                            PublishStatus = "",
                            UserId = comment.InReplyTo.UserId,
                            CoupletIndex = comment.InReplyTo.CoupletIndex == null ? -1 : (int)comment.InReplyTo.CoupletIndex,
                        },
                     Poem = new GanjoorPoemSummaryViewModel()
                     {
                         Id = comment.Poem.Id,
                         Title = comment.Poem.FullTitle,
                         UrlSlug = comment.Poem.FullUrl,
                         Excerpt = ""
                     }
                 };

            (PaginationMetadata PagingMeta, GanjoorCommentFullViewModel[] Items) paginatedResult =
                await QueryablePaginator<GanjoorCommentFullViewModel>.Paginate(source, paging);


            foreach (GanjoorCommentFullViewModel comment in paginatedResult.Items)
            {
                comment.AuthorName = comment.AuthorName.ToPersianNumbers().ApplyCorrectYeKe();
                var relatedVerses = comment.CoupletIndex == -1 ? new List<GanjoorVerse>() : await _context.GanjoorVerses.Where(v => v.PoemId == comment.Poem.Id && v.CoupletIndex == comment.CoupletIndex).OrderBy(v => v.VOrder).ToListAsync();
                string coupleText = relatedVerses.Count == 0 ? "" : relatedVerses[0].Text;
                for (int nVerseIndex = 1; nVerseIndex < relatedVerses.Count; nVerseIndex++)
                {
                    coupleText += $" {relatedVerses[nVerseIndex].Text}";
                }


                comment.CoupletSummary = _CutSummary(coupleText);
                if (comment.InReplyTo != null)
                {

                    var replyRelatedVerses = comment.InReplyTo.CoupletIndex == -1 ? new List<GanjoorVerse>() : await _context.GanjoorVerses.Where(v => v.PoemId == comment.Poem.Id && v.CoupletIndex == comment.InReplyTo.CoupletIndex).OrderBy(v => v.VOrder).ToListAsync();
                    string replyCoupleText = relatedVerses.Count == 0 ? "" : relatedVerses[0].Text;
                    for (int nVerseIndex = 1; nVerseIndex < replyRelatedVerses.Count; nVerseIndex++)
                    {
                        replyCoupleText += $" {replyRelatedVerses[nVerseIndex].Text}";
                    }
                    comment.InReplyTo.CoupletSummary = _CutSummary(replyCoupleText);
                }
            }

            return new RServiceResult<(PaginationMetadata PagingMeta, GanjoorCommentFullViewModel[] Items)>(paginatedResult);
        }

        /// <summary>
        /// report a comment
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="report"></param>
        /// <returns>id of report record</returns>
        public async Task<RServiceResult<int>> ReportComment(Guid userId, GanjoorPostReportCommentViewModel report)
        {
            GanjoorCommentAbuseReport r = new GanjoorCommentAbuseReport()
            {
                GanjoorCommentId = report.CommentId,
                ReportedById = userId,
                ReasonCode = report.ReasonCode,
                ReasonText = report.ReasonText,
            };
            _context.GanjoorReportedComments.Add(r);
            await _context.SaveChangesAsync();
            var moderators = await _appUserService.GetUsersHavingPermission(RMuseumSecurableItem.GanjoorEntityShortName, RMuseumSecurableItem.ModerateOperationShortName);
            if (string.IsNullOrEmpty(moderators.ExceptionString)) //if not, do nothing!
            {
                foreach (var moderator in moderators.Result)
                {
                    await _notificationService.PushNotification
                                    (
                                        (Guid)moderator.Id,
                                        "گزارش حاشیه",
                                        $"گزارشی برای یک حاشیه ثبت شده است. لطفاً بخش حاشیه‌های گزارش شده را بررسی فرمایید.{ Environment.NewLine}" +
                                        $"توجه فرمایید که اگر کاربر دیگری که دارای مجوز بررسی حاشیه‌هاست پیش از شما به آن رسیدگی کرده باشد آن را در صف نخواهید دید."
                                    );
                }
            }
            return new RServiceResult<int>(r.GanjoorCommentId);
        }

        /// <summary>
        /// delete a report
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> DeleteReport(int id)
        {
            GanjoorCommentAbuseReport report = await _context.GanjoorReportedComments.Where(r => r.Id == id).SingleOrDefaultAsync();
            if (report == null)
            {
                return new RServiceResult<bool>(false);
            }
            _context.GanjoorReportedComments.Remove(report);
            await _context.SaveChangesAsync();
            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// Get list of reported comments
        /// </summary>
        /// <param name="paging"></param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, GanjoorCommentAbuseReportViewModel[] Items)>> GetReportedComments(PagingParameterModel paging)
        {
            var source =
                 from report in _context.GanjoorReportedComments
                 join comment in _context.GanjoorComments.Include(c => c.Poem).Include(c => c.User).Include(c => c.InReplyTo).ThenInclude(r => r.User)
                 on report.GanjoorCommentId equals comment.Id
                 orderby report.Id descending
                 select
                 new GanjoorCommentAbuseReportViewModel()
                 {
                     Id = report.Id,
                     ReasonCode = report.ReasonCode,
                     ReasonText = report.ReasonText,
                     Comment = new GanjoorCommentFullViewModel()
                     {
                         Id = comment.Id,
                         AuthorName = comment.User == null ? comment.AuthorName : $"{comment.User.NickName}",
                         AuthorUrl = comment.AuthorUrl,
                         CommentDate = comment.CommentDate,
                         HtmlComment = comment.HtmlComment,
                         PublishStatus = "",//invalid!
                         UserId = comment.UserId,
                         InReplyTo = comment.InReplyTo == null ? null :
                        new GanjoorCommentSummaryViewModel()
                        {
                            Id = comment.InReplyTo.Id,
                            AuthorName = comment.InReplyTo.User == null ? comment.InReplyTo.AuthorName : $"{comment.InReplyTo.User.NickName}",
                            AuthorUrl = comment.InReplyTo.AuthorUrl,
                            CommentDate = comment.InReplyTo.CommentDate,
                            HtmlComment = comment.InReplyTo.HtmlComment,
                            PublishStatus = "",
                            UserId = comment.InReplyTo.UserId
                        },
                         Poem = new GanjoorPoemSummaryViewModel()
                         {
                             Id = comment.Poem.Id,
                             Title = comment.Poem.FullTitle,
                             UrlSlug = comment.Poem.FullUrl,
                             Excerpt = ""
                         }
                     }
                 };

            (PaginationMetadata PagingMeta, GanjoorCommentAbuseReportViewModel[] Items) paginatedResult =
                await QueryablePaginator<GanjoorCommentAbuseReportViewModel>.Paginate(source, paging);


            foreach (GanjoorCommentAbuseReportViewModel report in paginatedResult.Items)
            {
                report.Comment.AuthorName = report.Comment.AuthorName.ToPersianNumbers().ApplyCorrectYeKe();
            }

            return new RServiceResult<(PaginationMetadata PagingMeta, GanjoorCommentAbuseReportViewModel[] Items)>(paginatedResult);
        }


        /// <summary>
        /// get poem images by id (some fields are intentionally field with blank or null),
        /// EntityImageId : the most important data field, image url is {WebServiceUrl.Url}/api/images/thumb/{EntityImageId}.jpg or {WebServiceUrl.Url}/api/images/norm/{EntityImageId}.jpg
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<PoemRelatedImage[]>> GetPoemImages(int id)
        {
            var museumSrc =
                 from link in _context.GanjoorLinks.Include(l => l.Artifact).Include(l => l.Item).ThenInclude(i => i.Images)
                 join poem in _context.GanjoorPoems
                 on link.GanjoorPostId equals poem.Id
                 where
                 link.DisplayOnPage == true
                 &&
                 link.ReviewResult == Models.GanjoorIntegration.ReviewResult.Approved
                 &&
                 poem.Id == id
                 orderby link.ReviewDate
                 select new PoemRelatedImage()
                 {
                     PoemRelatedImageType = PoemRelatedImageType.MuseumLink,
                     ThumbnailImageUrl = $"{WebServiceUrl.Url}/api/images/thumb/{link.Item.Images.First().Id}.jpg",
                     TargetPageUrl = link.LinkToOriginalSource ? link.OriginalSourceUrl : $"https://museum.ganjoor.net/items/{link.Artifact.FriendlyUrl}/{link.Item.FriendlyUrl}",
                     AltText = $"{link.Artifact.Name} » {link.Item.Name}",
                 };
            List<PoemRelatedImage> museumImages = await museumSrc.ToListAsync();

            var externalSrc =
                 from link in _context.PinterestLinks
                 join poem in _context.GanjoorPoems
                 on link.GanjoorPostId equals poem.Id
                 where
                 link.ReviewResult == Models.GanjoorIntegration.ReviewResult.Approved
                 &&
                 poem.Id == id
                 orderby link.ReviewDate
                 select new PoemRelatedImage()
                 {
                     PoemRelatedImageType = PoemRelatedImageType.ExternalLink,
                     ThumbnailImageUrl = $"{WebServiceUrl.Url}/api/images/thumb/{link.Item.Images.First().Id}.jpg",
                     TargetPageUrl = link.PinterestUrl,
                     AltText = link.AltText,
                 };

            museumImages.AddRange(await externalSrc.AsNoTracking().ToListAsync());

            for (int i = 0; i < museumImages.Count; i++)
            {
                museumImages[i].ImageOrder = 0;
            }
            return new RServiceResult<PoemRelatedImage[]>(museumImages.ToArray());
        }

        /// <summary>
        /// Get Poem By Url
        /// </summary>
        /// <param name="url"></param>
        /// <param name="catInfo"></param>
        /// <param name="catPoems"></param>
        /// <param name="rhymes"></param>
        /// <param name="recitations"></param>
        /// <param name="images"></param>
        /// <param name="songs"></param>
        /// <param name="comments"></param>
        /// <param name="verseDetails"></param>
        /// <param name="navigation"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoemCompleteViewModel>> GetPoemByUrl(string url, bool catInfo = true, bool catPoems = false, bool rhymes = true, bool recitations = true, bool images = true, bool songs = true, bool comments = true, bool verseDetails = true, bool navigation = true)
        {
            // /hafez/ => /hafez :
            if (url.LastIndexOf('/') == url.Length - 1)
            {
                url = url.Substring(0, url.Length - 1);
            }
            var poem = await _context.GanjoorPoems.Where(p => p.FullUrl == url).SingleOrDefaultAsync();
            if (poem == null)
            {
                return new RServiceResult<GanjoorPoemCompleteViewModel>(null); //not found
            }
            return await GetPoemById(poem.Id, catInfo, catPoems, rhymes, recitations, images, songs, comments, verseDetails, navigation);
        }

        /// <summary>
        /// Get Poem By Id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="catInfo"></param>
        /// <param name="catPoems"></param>
        /// <param name="rhymes"></param>
        /// <param name="recitations"></param>
        /// <param name="images"></param>
        /// <param name="songs"></param>
        /// <param name="comments"></param>
        /// <param name="verseDetails"></param>
        /// <param name="navigation"></param>
        /// <param name="relatedpoems"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoemCompleteViewModel>> GetPoemById(int id, bool catInfo = true, bool catPoems = false, bool rhymes = true, bool recitations = true, bool images = true, bool songs = true, bool comments = true, bool verseDetails = true, bool navigation = true, bool relatedpoems = true)
        {
            var cachKey = $"GetPoemById({id}, {catInfo}, {catPoems}, {rhymes}, {recitations}, {images}, {songs}, {comments}, {verseDetails}, {navigation})";
            if (!_memoryCache.TryGetValue(cachKey, out GanjoorPoemCompleteViewModel poemViewModel))
            {
                var poem = await _context.GanjoorPoems.Include(p => p.GanjoorMetre).Where(p => p.Id == id).AsNoTracking().SingleOrDefaultAsync();
                if (poem == null)
                {
                    return new RServiceResult<GanjoorPoemCompleteViewModel>(null); //not found
                }
                GanjoorPoetCompleteViewModel cat = null;
                if (catInfo)
                {
                    var catRes = await GetCatById(poem.CatId, catPoems);
                    if (!string.IsNullOrEmpty(catRes.ExceptionString))
                    {
                        return new RServiceResult<GanjoorPoemCompleteViewModel>(null, catRes.ExceptionString);
                    }
                    cat = catRes.Result;
                }

                GanjoorPoemSummaryViewModel next = null;
                if (navigation)
                {
                    int nextId =
                        await _context.GanjoorPoems
                                                       .Where(p => p.CatId == poem.CatId && p.Id > poem.Id)
                                                       .AnyAsync()
                                                       ?
                        await _context.GanjoorPoems
                                                       .Where(p => p.CatId == poem.CatId && p.Id > poem.Id)
                                                       .MinAsync(p => p.Id)
                                                       :
                                                       0;
                    if (nextId != 0)
                    {
                        next = await _context.GanjoorPoems.Where(p => p.Id == nextId).Select
                            (
                            p =>
                            new GanjoorPoemSummaryViewModel()
                            {
                                Id = p.Id,
                                Title = p.Title,
                                UrlSlug = p.UrlSlug,
                                Excerpt = _context.GanjoorVerses.Where(v => v.PoemId == p.Id && v.VOrder == 1).FirstOrDefault().Text
                            }
                            ).AsNoTracking().SingleAsync();
                    }

                }

                GanjoorPoemSummaryViewModel previous = null;
                if (navigation)
                {
                    int preId =
                        await _context.GanjoorPoems
                                                       .Where(p => p.CatId == poem.CatId && p.Id < poem.Id)
                                                       .AnyAsync()
                                                       ?
                        await _context.GanjoorPoems
                                                       .Where(p => p.CatId == poem.CatId && p.Id < poem.Id)
                                                       .MaxAsync(p => p.Id)
                                                       :
                                                       0;
                    if (preId != 0)
                    {
                        previous = await _context.GanjoorPoems.Where(p => p.Id == preId).Select
                            (
                            p =>
                            new GanjoorPoemSummaryViewModel()
                            {
                                Id = p.Id,
                                Title = p.Title,
                                UrlSlug = p.UrlSlug,
                                Excerpt = _context.GanjoorVerses.Where(v => v.PoemId == p.Id && v.VOrder == 1).FirstOrDefault().Text
                            }
                            ).AsNoTracking().SingleAsync();
                    }

                }

                PublicRecitationViewModel[] rc = null;
                if (recitations)
                {
                    var rcRes = await GetPoemRecitations(id);
                    if (!string.IsNullOrEmpty(rcRes.ExceptionString))
                        return new RServiceResult<GanjoorPoemCompleteViewModel>(null, rcRes.ExceptionString);
                    rc = rcRes.Result;
                }

                PoemRelatedImage[] imgs = null;
                if (images)
                {
                    var imgsRes = await GetPoemImages(id);
                    if (!string.IsNullOrEmpty(imgsRes.ExceptionString))
                        return new RServiceResult<GanjoorPoemCompleteViewModel>(null, imgsRes.ExceptionString);
                    imgs = imgsRes.Result;
                }

                GanjoorVerseViewModel[] verses = null;
                if (verseDetails)
                {
                    verses = await _context.GanjoorVerses
                                                    .Where(v => v.PoemId == id)
                                                    .OrderBy(v => v.VOrder)
                                                    .Select
                                                    (
                                                        v => new GanjoorVerseViewModel()
                                                        {
                                                            Id = v.Id,
                                                            VOrder = v.VOrder,
                                                            CoupletIndex = v.CoupletIndex,
                                                            VersePosition = v.VersePosition,
                                                            Text = v.Text,
                                                        }
                                                    ).AsNoTracking().ToArrayAsync();
                };


                PoemMusicTrackViewModel[] tracks = null;
                if (songs)
                {
                    var songsRes = await GetPoemSongs(id, true, PoemMusicTrackType.All);
                    if (!string.IsNullOrEmpty(songsRes.ExceptionString))
                        return new RServiceResult<GanjoorPoemCompleteViewModel>(null, songsRes.ExceptionString);
                    tracks = songsRes.Result;
                }

                GanjoorCommentSummaryViewModel[] poemComments = null;

                if (comments)
                {
                    var commentsRes = await GetPoemComments(id, Guid.Empty, null);
                    if (!string.IsNullOrEmpty(commentsRes.ExceptionString))
                        return new RServiceResult<GanjoorPoemCompleteViewModel>(null, commentsRes.ExceptionString);
                    poemComments = commentsRes.Result;
                }

                GanjoorCachedRelatedPoem[] top6relatedPoems = null;
                if (relatedpoems)
                {
                    var relatedPoemsRes = await GetRelatedPoems(id, 0, 6);
                    if (!string.IsNullOrEmpty(relatedPoemsRes.ExceptionString))
                        return new RServiceResult<GanjoorPoemCompleteViewModel>(null, relatedPoemsRes.ExceptionString);
                    top6relatedPoems = relatedPoemsRes.Result;
                }


                poemViewModel = new GanjoorPoemCompleteViewModel()
                {
                    Id = poem.Id,
                    Title = poem.Title,
                    FullTitle = poem.FullTitle,
                    FullUrl = poem.FullUrl,
                    UrlSlug = poem.UrlSlug,
                    HtmlText = poem.HtmlText,
                    PlainText = poem.PlainText,
                    GanjoorMetre = poem.GanjoorMetre,
                    RhymeLetters = poem.RhymeLetters,
                    SourceName = poem.SourceName,
                    SourceUrlSlug = poem.SourceUrlSlug,
                    OldTag = poem.OldTag,
                    OldTagPageUrl = poem.OldTagPageUrl,
                    MixedModeOrder = poem.MixedModeOrder,
                    Published = poem.Published,
                    Language = poem.Language,
                    Category = cat,
                    Next = next,
                    Previous = previous,
                    Recitations = rc,
                    Images = imgs,
                    Verses = verses,
                    Songs = tracks,
                    Comments = poemComments,
                    Top6RelatedPoems = top6relatedPoems
                };

                if (AggressiveCacheEnabled)
                {
                    _memoryCache.Set(cachKey, poemViewModel);
                }
            }
            return new RServiceResult<GanjoorPoemCompleteViewModel>
                (
                poemViewModel
                );
        }

        /// <summary>
        /// delete unreviewed user corrections for a poem
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="poemId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> DeletePoemCorrections(Guid userId, int poemId)
        {
            var preCorrections = await _context.GanjoorPoemCorrections.Include(c => c.VerseOrderText)
                .Where(c => c.UserId == userId && c.PoemId == poemId && c.Reviewed == false)
                .ToListAsync();
            if (preCorrections.Count > 0)
            {
                foreach (var preCorrection in preCorrections)
                {
                    preCorrection.VerseOrderText.Clear();
                }
                _context.GanjoorPoemCorrections.RemoveRange(preCorrections);
                await _context.SaveChangesAsync();
            }
            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// send poem correction
        /// </summary>
        /// <param name="correction"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoemCorrectionViewModel>> SuggestPoemCorrection(GanjoorPoemCorrectionViewModel correction)
        {
            var preCorrections = await _context.GanjoorPoemCorrections.Include(c => c.VerseOrderText)
                .Where(c => c.UserId == correction.UserId && c.PoemId == correction.PoemId && c.Reviewed == false)
                .ToListAsync();

            var poem = (await GetPoemById(correction.PoemId, false, false, true, false, false, false, false, true, false)).Result;
            foreach (var verse in correction.VerseOrderText)
            {
                verse.OriginalText = poem.Verses.Where(v => v.VOrder == verse.VORder).Single().Text;
            }
            GanjoorPoemCorrection dbCorrection = new GanjoorPoemCorrection()
            {
                PoemId = correction.PoemId,
                UserId = correction.UserId,
                VerseOrderText = correction.VerseOrderText,
                Title = correction.Title,
                OriginalTitle = poem.Title,
                Rhythm = correction.Rhythm,
                OriginalRhythm = poem.GanjoorMetre == null ? null : poem.GanjoorMetre.Rhythm,
                Note = correction.Note,
                Date = DateTime.Now,
                Result = CorrectionReviewResult.NotReviewed,
                Reviewed = false,
                AffectedThePoem = false
            };
            _context.GanjoorPoemCorrections.Add(dbCorrection);
            await _context.SaveChangesAsync();
            correction.Id = dbCorrection.Id;

            if (preCorrections.Count > 0)
            {
                foreach (var preCorrection in preCorrections)
                {
                    preCorrection.VerseOrderText.Clear();
                }
                _context.GanjoorPoemCorrections.RemoveRange(preCorrections);
                await _context.SaveChangesAsync();
            }

            return new RServiceResult<GanjoorPoemCorrectionViewModel>(correction);
        }

        /// <summary>
        /// last unreviewed user correction for a poem
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="poemId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoemCorrectionViewModel>> GetLastUnreviewedUserCorrectionForPoem(Guid userId, int poemId)
        {
            var dbCorrection = await _context.GanjoorPoemCorrections.AsNoTracking().Include(c => c.VerseOrderText).Include(c => c.User)
                .Where(c => c.UserId == userId && c.PoemId == poemId && c.Reviewed == false)
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync();

            if (dbCorrection == null)
                return new RServiceResult<GanjoorPoemCorrectionViewModel>(null);

            return new RServiceResult<GanjoorPoemCorrectionViewModel>
                (
                new GanjoorPoemCorrectionViewModel()
                {
                    Id = dbCorrection.Id,
                    PoemId = dbCorrection.PoemId,
                    UserId = dbCorrection.UserId,
                    VerseOrderText = dbCorrection.VerseOrderText == null ? null : dbCorrection.VerseOrderText.ToArray(),
                    Title = dbCorrection.Title,
                    OriginalTitle = dbCorrection.OriginalTitle,
                    Rhythm = dbCorrection.Rhythm,
                    OriginalRhythm = dbCorrection.OriginalRhythm,
                    Note = dbCorrection.Note,
                    Date = dbCorrection.Date,
                    Reviewed = dbCorrection.Reviewed,
                    Result = dbCorrection.Result,
                    RhythmResult = dbCorrection.RhythmResult,
                    ReviewNote = dbCorrection.ReviewNote,
                    ReviewDate = dbCorrection.ReviewDate,
                    UserNickname = string.IsNullOrEmpty(dbCorrection.User.NickName) ? dbCorrection.User.Id.ToString() : dbCorrection.User.NickName
                }
                );
        }

        /// <summary>
        /// get user or all corrections
        /// </summary>
        /// <param name="userId">if sent empty returns all corrections</param>
        /// <param name="paging"></param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, GanjoorPoemCorrectionViewModel[] Items)>> GetUserCorrections(Guid userId, PagingParameterModel paging)
        {
            var source = from dbCorrection in
                             _context.GanjoorPoemCorrections.AsNoTracking().Include(c => c.VerseOrderText).Include(c => c.User)
                         where userId == Guid.Empty || dbCorrection.UserId == userId
                         orderby dbCorrection.Id descending
                         select
                          dbCorrection;

            (PaginationMetadata PagingMeta, GanjoorPoemCorrection[] Items) dbPaginatedResult =
                await QueryablePaginator<GanjoorPoemCorrection>.Paginate(source, paging);

            List<GanjoorPoemCorrectionViewModel> list = new List<GanjoorPoemCorrectionViewModel>();
            foreach (var dbCorrection in dbPaginatedResult.Items)
            {
                list.Add
                    (
                new GanjoorPoemCorrectionViewModel()
                {
                    Id = dbCorrection.Id,
                    PoemId = dbCorrection.PoemId,
                    UserId = dbCorrection.UserId,
                    VerseOrderText = dbCorrection.VerseOrderText == null ? null : dbCorrection.VerseOrderText.ToArray(),
                    Title = dbCorrection.Title,
                    OriginalTitle = dbCorrection.OriginalTitle,
                    Rhythm = dbCorrection.Rhythm,
                    OriginalRhythm = dbCorrection.OriginalRhythm,
                    Note = dbCorrection.Note,
                    Date = dbCorrection.Date,
                    Reviewed = dbCorrection.Reviewed,
                    Result = dbCorrection.Result,
                    RhythmResult = dbCorrection.RhythmResult,
                    ReviewNote = dbCorrection.ReviewNote,
                    ReviewDate = dbCorrection.ReviewDate,
                    UserNickname = string.IsNullOrEmpty(dbCorrection.User.NickName) ? dbCorrection.User.Id.ToString() : dbCorrection.User.NickName
                }
                );
            }

            return new RServiceResult<(PaginationMetadata, GanjoorPoemCorrectionViewModel[])>
                ((dbPaginatedResult.PagingMeta, list.ToArray()));
        }

        /// <summary>
        /// effective corrections for poem
        /// </summary>
        /// <param name="poemId"></param>
        /// <param name="paging"></param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, GanjoorPoemCorrectionViewModel[] Items)>> GetPoemEffectiveCorrections(int poemId, PagingParameterModel paging)
        {
            var source = from dbCorrection in
                             _context.GanjoorPoemCorrections.AsNoTracking().Include(c => c.VerseOrderText)
                         where
                         dbCorrection.PoemId == poemId
                         &&
                         dbCorrection.Reviewed == true
                         &&
                         (
                         dbCorrection.Result == CorrectionReviewResult.Approved || dbCorrection.RhythmResult == CorrectionReviewResult.Approved
                         ||
                         dbCorrection.VerseOrderText.Any(v => v.Result == CorrectionReviewResult.Approved)
                         )
                         orderby dbCorrection.Id descending
                         select
                         dbCorrection;

            (PaginationMetadata PagingMeta, GanjoorPoemCorrection[] Items) dbPaginatedResult =
                await QueryablePaginator<GanjoorPoemCorrection>.Paginate(source, paging);

            List<GanjoorPoemCorrectionViewModel> list = new List<GanjoorPoemCorrectionViewModel>();
            foreach (var dbCorrection in dbPaginatedResult.Items)
            {
                list.Add
                    (
                new GanjoorPoemCorrectionViewModel()
                {
                    Id = dbCorrection.Id,
                    PoemId = dbCorrection.PoemId,
                    UserId = dbCorrection.UserId,
                    VerseOrderText = dbCorrection.VerseOrderText == null ? null : dbCorrection.VerseOrderText.ToArray(),
                    Title = dbCorrection.Title,
                    OriginalTitle = dbCorrection.OriginalTitle,
                    Rhythm = dbCorrection.Rhythm,
                    OriginalRhythm = dbCorrection.OriginalRhythm,
                    Note = dbCorrection.Note,
                    Date = dbCorrection.Date,
                    Reviewed = dbCorrection.Reviewed,
                    Result = dbCorrection.Result,
                    RhythmResult = dbCorrection.RhythmResult,
                    ReviewNote = dbCorrection.ReviewNote,
                    ReviewDate = dbCorrection.ReviewDate,
                }
                );
            }

            return new RServiceResult<(PaginationMetadata, GanjoorPoemCorrectionViewModel[])>
                ((dbPaginatedResult.PagingMeta, list.ToArray()));
        }

        /// <summary>
        /// get correction by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoemCorrectionViewModel>> GetCorrectionById(int id)
        {
            var dbCorrection = await _context.GanjoorPoemCorrections.AsNoTracking().Include(c => c.VerseOrderText).Include(c => c.User)
                .Where(c => c.Id == id)
                .FirstOrDefaultAsync();

            if (dbCorrection == null)
                return new RServiceResult<GanjoorPoemCorrectionViewModel>(null);

            return new RServiceResult<GanjoorPoemCorrectionViewModel>
                (
                new GanjoorPoemCorrectionViewModel()
                {
                    Id = dbCorrection.Id,
                    PoemId = dbCorrection.PoemId,
                    UserId = dbCorrection.UserId,
                    VerseOrderText = dbCorrection.VerseOrderText == null ? null : dbCorrection.VerseOrderText.ToArray(),
                    Title = dbCorrection.Title,
                    OriginalTitle = dbCorrection.OriginalTitle,
                    Rhythm = dbCorrection.Rhythm,
                    OriginalRhythm = dbCorrection.OriginalRhythm,
                    Note = dbCorrection.Note,
                    Date = dbCorrection.Date,
                    Reviewed = dbCorrection.Reviewed,
                    Result = dbCorrection.Result,
                    RhythmResult = dbCorrection.RhythmResult,
                    ReviewNote = dbCorrection.ReviewNote,
                    ReviewDate = dbCorrection.ReviewDate,
                    UserNickname = string.IsNullOrEmpty(dbCorrection.User.NickName) ? dbCorrection.User.Id.ToString() : dbCorrection.User.NickName
                }
                );
        }

        /// <summary>
        /// get next unreviewed correction
        /// </summary>
        /// <param name="skip"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoemCorrectionViewModel>> GetNextUnreviewedCorrection(int skip)
        {
            var dbCorrection = await _context.GanjoorPoemCorrections.AsNoTracking().Include(c => c.VerseOrderText).Include(c => c.User)
                .Where(c => c.Reviewed == false)
                .OrderBy(c => c.Id)
                .Skip(skip)
                .FirstOrDefaultAsync();

            if (dbCorrection == null)
                return new RServiceResult<GanjoorPoemCorrectionViewModel>(null);

            return new RServiceResult<GanjoorPoemCorrectionViewModel>
                (
                new GanjoorPoemCorrectionViewModel()
                {
                    Id = dbCorrection.Id,
                    PoemId = dbCorrection.PoemId,
                    UserId = dbCorrection.UserId,
                    VerseOrderText = dbCorrection.VerseOrderText == null ? null : dbCorrection.VerseOrderText.ToArray(),
                    Title = dbCorrection.Title,
                    OriginalTitle = dbCorrection.OriginalTitle,
                    Rhythm = dbCorrection.Rhythm,
                    OriginalRhythm = dbCorrection.OriginalRhythm,
                    Note = dbCorrection.Note,
                    Date = dbCorrection.Date,
                    Reviewed = dbCorrection.Reviewed,
                    Result = dbCorrection.Result,
                    RhythmResult = dbCorrection.RhythmResult,
                    ReviewNote = dbCorrection.ReviewNote,
                    ReviewDate = dbCorrection.ReviewDate,
                    UserNickname = string.IsNullOrEmpty(dbCorrection.User.NickName) ? dbCorrection.User.Id.ToString() : dbCorrection.User.NickName
                }
                );
        }

        /// <summary>
        /// unreview correction count
        /// </summary>
        /// <returns></returns>
        public async Task<RServiceResult<int>> GetUnreviewedCorrectionCount()
        {
            return new RServiceResult<int>(await _context.GanjoorPoemCorrections.AsNoTracking().Include(c => c.VerseOrderText)
                .Where(c => c.Reviewed == false)
                .CountAsync());
        }


        /// <summary>
        /// moderate poem correction
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="moderation"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoemCorrectionViewModel>> ModeratePoemCorrection(Guid userId,
            GanjoorPoemCorrectionViewModel moderation)
        {
            var dbCorrection = await _context.GanjoorPoemCorrections.Include(c => c.VerseOrderText).Include(c => c.User)
                .Where(c => c.Id == moderation.Id)
                .FirstOrDefaultAsync();

            dbCorrection.ReviewerUserId = userId;
            dbCorrection.ReviewDate = DateTime.Now;
            dbCorrection.ApplicationOrder = await _context.GanjoorPoemCorrections.Where(c => c.Reviewed).AnyAsync() ? 1 + await _context.GanjoorPoemCorrections.Where(c => c.Reviewed).MaxAsync(c => c.ApplicationOrder) : 1;
            dbCorrection.Reviewed = true;
            dbCorrection.AffectedThePoem = false;

            if (dbCorrection == null)
                return new RServiceResult<GanjoorPoemCorrectionViewModel>(null);

            var dbPoem = await _context.GanjoorPoems.Include(p => p.GanjoorMetre).Where(p => p.Id == moderation.PoemId).SingleOrDefaultAsync();
            var dbPage = await _context.GanjoorPages.AsNoTracking().Where(p => p.Id == moderation.PoemId).SingleOrDefaultAsync();

            GanjoorModifyPageViewModel pageViewModel = new GanjoorModifyPageViewModel()
            {
                Title = dbPoem.Title,
                UrlSlug = dbPoem.UrlSlug,
                OldTag = dbPoem.OldTag,
                OldTagPageUrl = dbPoem.OldTagPageUrl,
                RhymeLetters = dbPoem.RhymeLetters,
                Rhythm = dbPoem.GanjoorMetre == null ? null : dbPoem.GanjoorMetre.Rhythm,
                HtmlText = dbPoem.HtmlText,
                SourceName = dbPoem.SourceName,
                SourceUrlSlug = dbPoem.SourceUrlSlug,
                MixedModeOrder = dbPoem.MixedModeOrder,
                Published = dbPoem.Published,
                Language = dbPoem.Language,
                NoIndex = dbPage.NoIndex,
                RedirectFromFullUrl = dbPage.RedirectFromFullUrl,
                Note = dbCorrection.Note
            };

            if (dbCorrection.Title != null)
            {
                if (moderation.Result == CorrectionReviewResult.NotReviewed)
                    return new RServiceResult<GanjoorPoemCorrectionViewModel>(null, "تغییرات عنوان بررسی نشده است.");
                dbCorrection.Result = moderation.Result;
                dbCorrection.ReviewNote = moderation.ReviewNote;
                if (dbCorrection.Result == CorrectionReviewResult.Approved)
                {
                    dbCorrection.AffectedThePoem = true;
                    pageViewModel.Title = moderation.Title.Replace("ۀ", "هٔ").Replace("ك", "ک");
                }
            }

            if (dbCorrection.Rhythm != null)
            {
                if (moderation.RhythmResult == CorrectionReviewResult.NotReviewed)
                    return new RServiceResult<GanjoorPoemCorrectionViewModel>(null, "تغییرات وزن بررسی نشده است.");
                dbCorrection.RhythmResult = moderation.RhythmResult;
                dbCorrection.ReviewNote = moderation.ReviewNote;
                if (dbCorrection.RhythmResult == CorrectionReviewResult.Approved)
                {
                    dbCorrection.AffectedThePoem = true;
                    pageViewModel.Rhythm = moderation.Rhythm;
                }
            }

            if (moderation.VerseOrderText.Length != dbCorrection.VerseOrderText.Count)
                return new RServiceResult<GanjoorPoemCorrectionViewModel>(null, "moderation.VerseOrderText.Length != dbCorrection.VerseOrderText.Count");

            var poemVerses = await _context.GanjoorVerses.AsNoTracking().Where(p => p.PoemId == dbCorrection.PoemId).OrderBy(v => v.VOrder).ToListAsync();

            foreach (var moderatedVerse in moderation.VerseOrderText)
            {
                if (moderatedVerse.Result == CorrectionReviewResult.NotReviewed)
                    return new RServiceResult<GanjoorPoemCorrectionViewModel>(null, $"تغییرات مصرع {moderatedVerse.VORder} بررسی نشده است.");
                var dbVerse = dbCorrection.VerseOrderText.Where(c => c.VORder == moderatedVerse.VORder).Single();
                dbVerse.Result = moderatedVerse.Result;
                dbVerse.ReviewNote = moderatedVerse.ReviewNote;
                if (dbVerse.Result == CorrectionReviewResult.Approved)
                {
                    dbCorrection.AffectedThePoem = true;
                    poemVerses.Where(v => v.VOrder == moderatedVerse.VORder).Single().Text = moderatedVerse.Text.Replace("ۀ", "هٔ").Replace("ك", "ک");
                }
            }

            pageViewModel.HtmlText = PrepareHtmlText(poemVerses);
            try
            {
                var newRhyme = LanguageUtils.FindRhyme(poemVerses);
                if (!string.IsNullOrEmpty(newRhyme.Rhyme) &&
                    (string.IsNullOrEmpty(dbPoem.RhymeLetters) || (!string.IsNullOrEmpty(dbPoem.RhymeLetters) && newRhyme.Rhyme.Length > dbPoem.RhymeLetters.Length))
                    )
                {
                    pageViewModel.RhymeLetters = newRhyme.Rhyme;
                }
            }
            catch
            {

            }

            var res = await UpdatePageAsync(moderation.PoemId, userId, pageViewModel);
            if (!string.IsNullOrEmpty(res.ExceptionString))
                return new RServiceResult<GanjoorPoemCorrectionViewModel>(null, res.ExceptionString);


            _context.GanjoorPoemCorrections.Update(dbCorrection);
            await _context.SaveChangesAsync();

            await _notificationService.PushNotification(dbCorrection.UserId,
                               "بررسی ویرایش پیشنهادی شما",
                               $"با سپاس از زحمت و همت شما ویرایش پیشنهادیتان برای <a href=\"{dbPoem.FullUrl}\" target=\"_blank\">{dbPoem.FullTitle}</a> بررسی شد.{Environment.NewLine}" +
                               $"جهت مشاهدهٔ نتیجهٔ بررسی در میز کاربری خود بخش «ویرایش‌های من» را مشاهده بفرمایید.{Environment.NewLine}"
                               );

            return new RServiceResult<GanjoorPoemCorrectionViewModel>(moderation);
        }

        /// <summary>
        /// random poem id from hafez sonnets and old c.ganjoor.net service
        /// </summary>
        /// <returns></returns>
        private int _GetRandomPoemId(int poetId, int loopBreaker = 0)
        {
            if (loopBreaker > 10)
                return 0;
            Random r = new Random(DateTime.Now.Millisecond);

            switch (poetId)
            {
                case 2://حافظ
                    {
                        //this is magic number based method!
                        int startPoemId = 2130;
                        int endPoemId = 2624 + 1; //one is added for مژده ای دل که مسیحا نفسی می‌آید
                        int poemId = r.Next(startPoemId, endPoemId);
                        if (poemId == endPoemId)
                        {
                            poemId = 33179;//مژده ای دل که مسیحا نفسی می‌آید
                        }
                        return poemId;
                    }
                case 3://خیام
                    return r.Next(1119, 1296);
                case 26://ابوسعید
                    return r.Next(20509, 21232);
                case 22://صائب
                    return r.Next(52198, 59193);
                case 7://سعدی
                    return r.Next(9323, 9959);
                case 28://بابا طاهر
                    return r.Next(21309, 21674);
                case 5://مولانا
                    return r.Next(2625, 5853);
                case 19://اوحدی
                    return r.Next(16955, 17839);
                case 35://شهریار
                    return r.Next(27065, 27224);
                case 20://خواجو
                    return r.Next(18288, 19219);
                case 32://فروغی
                    return r.Next(22996, 23511);
                case 21://عراقی
                    return r.Next(19222, 19526);
                case 40://سلمان
                    return r.Next(38411, 39320);
                case 29://محتشم
                    return r.Next(21744, 22338);
                case 34://امیرخسرو
                    return r.Next(60582, 62578);
                case 31://سیف
                    return r.Next(62837, 63418);
                case 33://عبید
                    return r.Next(23551, 23656);
                case 25://هاتف
                    return r.Next(20275, 20364);
                case 41://رهی
                    return r.Next(39441, 39546);
            }

            int[] poetIdArray = new int[]
            {
                2,
                3,
                26,
                22,
                7,
                28,
                5,
                19,
                35,
                20,
                23,
                21,
                40,
                29,
                34,
                31,
                33,
                25,
                41
            };

            return _GetRandomPoemId(poetIdArray[r.Next(0, poetIdArray.Length - 1)], loopBreaker++);

        }



        /// <summary>
        /// get a random poem from hafez
        /// </summary>
        /// <param name="poetId"></param>
        /// <param name="recitation"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoemCompleteViewModel>> Faal(int poetId = 2, bool recitation = true)
        {
            int poemId = _GetRandomPoemId(poetId);
            var poem = await _context.GanjoorPoems.Where(p => p.Id == poemId).AsNoTracking().SingleOrDefaultAsync();
            PublicRecitationViewModel[] recitations = poem == null || !recitation ? new PublicRecitationViewModel[] { } : (await GetPoemRecitations(poemId)).Result;
            int loopPreventer = 0;
            while (poem == null || (recitation && recitations.Length == 0))
            {
                poem = await _context.GanjoorPoems.Where(p => p.Id == poemId).AsNoTracking().SingleOrDefaultAsync();
                recitations = poem == null ? new PublicRecitationViewModel[] { } : (await GetPoemRecitations(poemId)).Result;
                loopPreventer++;
                if (loopPreventer > 5)
                {
                    return new RServiceResult<GanjoorPoemCompleteViewModel>(null);
                }
            }

            return await GetPoemById(poemId, false, false, false, recitation, false, false, false, true /*verse details*/, false);
        }

        /// <summary>
        /// Get Similar Poems accroding to prosody and rhyme informations
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="metre"></param>
        /// <param name="rhyme"></param>
        /// <param name="poetId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, GanjoorPoemCompleteViewModel[] Items)>> GetSimilarPoems(PagingParameterModel paging, string metre, string rhyme, int? poetId)
        {
            if (string.IsNullOrEmpty(rhyme))
                rhyme = "";
            var source =
                _context.GanjoorPoems.Include(p => p.Cat).ThenInclude(c => c.Poet).Include(p => p.GanjoorMetre)
                .Where(p =>
                        (poetId == null || p.Cat.PoetId == poetId)
                        &&
                        (p.GanjoorMetre.Rhythm == metre)
                        &&
                        (rhyme == "" || p.RhymeLetters == rhyme)
                        )
                .OrderBy(p => p.CatId).ThenBy(p => p.Id)
                .Select
                (
                    poem =>
                    new GanjoorPoemCompleteViewModel()
                    {
                        Id = poem.Id,
                        Title = poem.Title,
                        FullTitle = poem.FullTitle,
                        FullUrl = poem.FullUrl,
                        UrlSlug = poem.UrlSlug,
                        HtmlText = poem.HtmlText,
                        PlainText = poem.PlainText,
                        GanjoorMetre = poem.GanjoorMetre,
                        RhymeLetters = poem.RhymeLetters,
                        MixedModeOrder = poem.MixedModeOrder,
                        Published = poem.Published,
                        Language = poem.Language,
                        Category = new GanjoorPoetCompleteViewModel()
                        {
                            Poet = new GanjoorPoetViewModel()
                            {
                                Id = poem.Cat.Poet.Id,
                            }
                        },

                    }
                ).AsNoTracking();


            (PaginationMetadata PagingMeta, GanjoorPoemCompleteViewModel[] Items) paginatedResult =
               await QueryablePaginator<GanjoorPoemCompleteViewModel>.Paginate(source, paging);


            Dictionary<int, GanjoorPoetCompleteViewModel> cachedPoets = new Dictionary<int, GanjoorPoetCompleteViewModel>();

            foreach (var item in paginatedResult.Items)
            {
                if (cachedPoets.TryGetValue(item.Category.Poet.Id, out GanjoorPoetCompleteViewModel poet))
                {
                    item.Category = poet;
                }
                else
                {
                    poet = (await GetPoetById(item.Category.Poet.Id)).Result;

                    cachedPoets.Add(item.Category.Poet.Id, poet);

                    item.Category = poet;
                }

            }

            return new RServiceResult<(PaginationMetadata PagingMeta, GanjoorPoemCompleteViewModel[] Items)>(paginatedResult);
        }
        private async Task _populateCategoryChildren(int catId, List<int> catListId)
        {
            var catRes = await GetCatById(catId, false);
            foreach (var c in catRes.Result.Cat.Children)
            {
                catListId.Add(c.Id);
                await _populateCategoryChildren(c.Id, catListId);
            }
        }



        /// <summary>
        /// Search
        /// You need to run this scripts manually on the database before using this method:
        /// 
        /// CREATE FULLTEXT CATALOG [GanjoorPoemPlainTextCatalog] WITH ACCENT_SENSITIVITY = OFF AS DEFAULT
        /// 
        /// CREATE FULLTEXT INDEX ON [dbo].[GanjoorPoems](
        /// [PlainText] LANGUAGE 'English')
        /// KEY INDEX [PK_GanjoorPoems]ON ([GanjoorPoemPlainTextCatalog], FILEGROUP [PRIMARY])
        /// WITH (CHANGE_TRACKING = AUTO, STOPLIST = SYSTEM)
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="term"></param>
        /// <param name="poetId"></param>
        /// <param name="catId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, GanjoorPoemCompleteViewModel[] Items)>> Search(PagingParameterModel paging, string term, int? poetId, int? catId)
        {
            term = term.Trim().ApplyCorrectYeKe();

            if (string.IsNullOrEmpty(term))
            {
                return new RServiceResult<(PaginationMetadata PagingMeta, GanjoorPoemCompleteViewModel[] Items)>((null, null), "خطای جستجوی عبارت خالی");
            }

            term = term.Replace("‌", " ");//replace zwnj with space


            string searchConditions;
            if (term.IndexOf('"') == 0 && term.LastIndexOf('"') == (term.Length - 1))
            {
                searchConditions = term.Replace("\"", "").Replace("'", "");
                searchConditions = $"\"{searchConditions}\"";
            }
            else
            {
                string[] words = term.Replace("\"", "").Replace("'", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

                searchConditions = "";
                string emptyOrAnd = "";
                foreach (string word in words)
                {
                    searchConditions += $" {emptyOrAnd} \"*{word}*\" ";
                    emptyOrAnd = " AND ";
                }
            }
            if (poetId == null)
            {
                catId = null;
            }
            if (poetId != null && catId == null)
            {
                var poetRes = await GetPoetById((int)poetId);
                if (!string.IsNullOrEmpty(poetRes.ExceptionString))
                    return new RServiceResult<(PaginationMetadata PagingMeta, GanjoorPoemCompleteViewModel[] Items)>((null, null), poetRes.ExceptionString);
                catId = poetRes.Result.Cat.Id;
            }
            List<int> catIdList = new List<int>();
            if (catId != null)
            {
                catIdList.Add((int)catId);
                await _populateCategoryChildren((int)catId, catIdList);
            }

            var source =
                _context.GanjoorPoems
                .Where(p =>
                        (catId == null || catIdList.Contains(p.CatId))
                        &&
                       EF.Functions.Contains(p.PlainText, searchConditions)
                        )
                .Include(p => p.Cat)
                .OrderBy(p => p.CatId).ThenBy(p => p.Id)
                .Select
                (
                    poem =>
                    new GanjoorPoemCompleteViewModel()
                    {
                        Id = poem.Id,
                        Title = poem.Title,
                        FullTitle = poem.FullTitle,
                        FullUrl = poem.FullUrl,
                        UrlSlug = poem.UrlSlug,
                        HtmlText = poem.HtmlText,
                        PlainText = poem.PlainText,
                        GanjoorMetre = poem.GanjoorMetre,
                        RhymeLetters = poem.RhymeLetters,
                        MixedModeOrder = poem.MixedModeOrder,
                        Published = poem.Published,
                        Language = poem.Language,
                        Category = new GanjoorPoetCompleteViewModel()
                        {
                            Poet = new GanjoorPoetViewModel()
                            {
                                Id = poem.Cat.Poet.Id,
                            }
                        },
                    }
                ).AsNoTracking();



            (PaginationMetadata PagingMeta, GanjoorPoemCompleteViewModel[] Items) paginatedResult =
               await QueryablePaginator<GanjoorPoemCompleteViewModel>.Paginate(source, paging);


            Dictionary<int, GanjoorPoetCompleteViewModel> cachedPoets = new Dictionary<int, GanjoorPoetCompleteViewModel>();

            foreach (var item in paginatedResult.Items)
            {
                if (cachedPoets.TryGetValue(item.Category.Poet.Id, out GanjoorPoetCompleteViewModel poet))
                {
                    item.Category = poet;
                }
                else
                {
                    poet = (await GetPoetById(item.Category.Poet.Id)).Result;

                    cachedPoets.Add(item.Category.Poet.Id, poet);

                    item.Category = poet;
                }

            }
            return new RServiceResult<(PaginationMetadata PagingMeta, GanjoorPoemCompleteViewModel[] Items)>(paginatedResult);
        }

        private async Task _UpdatePageChildrenTitleAndUrl(RMuseumDbContext context, GanjoorPage dbPage, bool messWithTitles, bool messWithUrls)
        {
            var children = await context.GanjoorPages.Where(p => p.ParentId == dbPage.Id).ToListAsync();
            foreach (var child in children)
            {
                child.FullUrl = dbPage.FullUrl + "/" + child.UrlSlug;
                child.FullTitle = dbPage.FullTitle + " » " + child.Title;

                switch (child.GanjoorPageType)
                {
                    case GanjoorPageType.PoemPage:
                        {
                            GanjoorPoem poem = await context.GanjoorPoems.Where(p => p.Id == child.Id).SingleAsync();
                            if (messWithTitles)
                                poem.FullTitle = child.FullTitle;
                            if (messWithUrls)
                                poem.FullUrl = child.FullUrl;

                            context.GanjoorPoems.Update(poem);
                        }
                        break;
                    case GanjoorPageType.CatPage:
                        {
                            if (messWithUrls)
                            {
                                GanjoorCat cat = await context.GanjoorCategories.Where(c => c.Id == child.CatId).SingleAsync();
                                cat.FullUrl = child.FullUrl;
                                context.GanjoorCategories.Update(cat);
                            }

                        }
                        break;
                }

                await _UpdatePageChildrenTitleAndUrl(context, child, messWithTitles, messWithUrls);

                CacheCleanForPageByUrl(child.FullUrl);
            }
            context.GanjoorPages.UpdateRange(children);
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// modify page
        /// </summary>
        /// <param name="id"></param>
        /// <param name="editingUserId"></param>
        /// <param name="pageData"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPageCompleteViewModel>> UpdatePageAsync(int id, Guid editingUserId, GanjoorModifyPageViewModel pageData)
        {
            return await _UpdatePageAsync(_context, id, editingUserId, pageData, true);
        }

        /// <summary>
        /// return page modifications history
        /// </summary>
        /// <param name="pageId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPageSnapshotSummaryViewModel[]>> GetOlderVersionsOfPage(int pageId)
        {
            return
                new RServiceResult<GanjoorPageSnapshotSummaryViewModel[]>
                (
                    await _context.GanjoorPageSnapshots.AsNoTracking()
                                    .Where(s => s.GanjoorPageId == pageId)
                                    .OrderByDescending(s => s.RecordDate)
                                    .Select
                                    (
                                        s =>
                                            new GanjoorPageSnapshotSummaryViewModel()
                                            {
                                                Id = s.Id,
                                                RecordDate = s.RecordDate,
                                                Note = s.Note
                                            }
                                    )
                                    .ToArrayAsync()
                );
        }

        /// <summary>
        /// get old version
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorModifyPageViewModel>> GetOldVersionOfPage(int id)
        {
            return new RServiceResult<GanjoorModifyPageViewModel>
                (
                await _context.GanjoorPageSnapshots.AsNoTracking()
                              .Where(s => s.Id == id)
                              .Select
                              (
                                s =>
                                    new GanjoorModifyPageViewModel()
                                    {
                                        HtmlText = s.HtmlText,
                                        Note = s.Note,
                                        OldTag = s.OldTag,
                                        OldTagPageUrl = s.OldTagPageUrl,
                                        RhymeLetters = s.RhymeLetters,
                                        Rhythm = s.Rhythm,
                                        SourceName = s.SourceName,
                                        SourceUrlSlug = s.SourceUrlSlug,
                                        Title = s.Title,
                                        UrlSlug = s.UrlSlug
                                    }
                              )
                              .SingleOrDefaultAsync()
                );
        }

        /// <summary>
        /// returns metre list (ordered by Rhythm)
        /// </summary>
        /// <param name="sortOnVerseCount"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorMetre[]>> GetGanjoorMetres(bool sortOnVerseCount = false)
        {
            return new RServiceResult<GanjoorMetre[]>(
                sortOnVerseCount ?
                await _context.GanjoorMetres.OrderByDescending(m => m.VerseCount).AsNoTracking().ToArrayAsync()
                :
                await _context.GanjoorMetres.OrderBy(m => m.Rhythm).AsNoTracking().ToArrayAsync()
                );
        }

        private void CleanPoetCache(int poetId)
        {
            //cache clean:
            _memoryCache.Remove($"/api/ganjoor/poets?published={true}&includeBio={false}");
            _memoryCache.Remove($"/api/ganjoor/poets?published={false}&includeBio={true}");
            _memoryCache.Remove("ganjoor/poets");
            _memoryCache.Remove($"/api/ganjoor/poet/{poetId}");
            _memoryCache.Remove($"poet/byid/{poetId}");
        }

        /// <summary>
        /// modify poet
        /// </summary>
        /// <param name="poet"></param>
        /// <param name="editingUserId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> UpdatePoetAsync(GanjoorPoetViewModel poet, Guid editingUserId)
        {
            var dbPoet = await _context.GanjoorPoets.Where(p => p.Id == poet.Id).SingleAsync();
            var dbPoetPage = await _context.GanjoorPages.Where(page => page.PoetId == poet.Id && page.GanjoorPageType == GanjoorPageType.PoetPage).SingleAsync();
            if (string.IsNullOrEmpty(poet.Nickname))
            {
                poet.Nickname = dbPoet.Nickname;
                poet.Published = dbPoet.Published;
            }
            if (string.IsNullOrEmpty(poet.FullUrl))
                poet.FullUrl = dbPoetPage.FullUrl;
            if (string.IsNullOrEmpty(poet.Name))
                poet.Name = dbPoet.Name;
            if (string.IsNullOrEmpty(poet.Description))
                poet.Description = dbPoet.Description;

            if (dbPoet.Nickname != poet.Nickname || dbPoetPage.FullUrl != poet.FullUrl)
            {
                var resPageEdit =
                    await UpdatePageAsync
                    (
                    dbPoetPage.Id,
                    editingUserId,
                    new GanjoorModifyPageViewModel()
                    {
                        Title = poet.Nickname,
                        HtmlText = dbPoetPage.HtmlText,
                        Note = "ویرایش مستقیم مشخصات شاعر",
                        UrlSlug = poet.FullUrl.Substring(1),
                    }
                    );
                if (!string.IsNullOrEmpty(resPageEdit.ExceptionString))
                    new RServiceResult<bool>(false, resPageEdit.ExceptionString);

                dbPoet.Nickname = poet.Nickname;
            }
            dbPoet.Name = poet.Name;
            dbPoet.Description = poet.Description;
            bool publishedChange = dbPoet.Published != poet.Published;
            dbPoet.Published = poet.Published;
            dbPoet.BirthYearInLHijri = poet.BirthYearInLHijri;
            dbPoet.ValidBirthDate = poet.ValidBirthDate;
            dbPoet.DeathYearInLHijri = poet.DeathYearInLHijri;
            dbPoet.ValidDeathDate = poet.ValidDeathDate;
            dbPoet.PinOrder = poet.PinOrder;
            dbPoet.BirthLocationId = string.IsNullOrEmpty(poet.BirthPlace) ? null
                : (await _context.GanjoorGeoLocations.Where(l => l.Name == poet.BirthPlace).SingleAsync()).Id;
            dbPoet.DeathLocationId = string.IsNullOrEmpty(poet.DeathPlace) ? null
               : (await _context.GanjoorGeoLocations.Where(l => l.Name == poet.DeathPlace).SingleAsync()).Id;
            _context.GanjoorPoets.Update(dbPoet);
            await _context.SaveChangesAsync();

            if (publishedChange)
            {
                var pages = await _context.GanjoorPages.Where(p => p.PoemId == poet.Id).ToListAsync();
                foreach (var page in pages)
                {
                    page.Published = poet.Published;
                }

                _context.GanjoorPages.UpdateRange(pages);

                await _context.SaveChangesAsync();
            }

            CleanPoetCache(poet.Id);

            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// create new poet
        /// </summary>
        /// <param name="poet"></param>
        /// <param name="editingUserId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjoorPoetCompleteViewModel>> AddPoetAsync(GanjoorPoetViewModel poet, Guid editingUserId)
        {
            if (await _context.GanjoorPoets.Where(p => p.Nickname == poet.Nickname || p.Name == poet.Name).AnyAsync())
            {
                return new RServiceResult<GanjoorPoetCompleteViewModel>(null, "conflicting poet");
            }

            if (await _context.GanjoorCategories.Where(c => c.FullUrl == poet.FullUrl).AnyAsync())
            {
                return new RServiceResult<GanjoorPoetCompleteViewModel>(null, "conflicting cat");
            }

            if (await _context.GanjoorPages.Where(p => p.FullUrl == poet.FullUrl).AnyAsync())
            {
                return new RServiceResult<GanjoorPoetCompleteViewModel>(null, "conflicting page");
            }

            if (poet.FullUrl.IndexOf('/') != 0)
            {
                return new RServiceResult<GanjoorPoetCompleteViewModel>(null, "Invalid FullUrl, it must start with /");
            }

            if (poet.FullUrl.Substring(1).IndexOf('/') >= 0)
            {
                return new RServiceResult<GanjoorPoetCompleteViewModel>(null, "Invalid FullUrl, it must contain only one /");
            }

            var id = 1 + await _context.GanjoorPoets.MaxAsync(p => p.Id);

            for (int i = 2; i < id; i++)
            {
                if (!(await _context.GanjoorPoets.Where(p => p.Id == i).AnyAsync()))
                {
                    id = i;
                    break;
                }
            }

            if (string.IsNullOrEmpty(poet.Description))
                poet.Description = "";

            GanjoorPoet dbPoet = new GanjoorPoet()
            {
                Id = id,
                Name = poet.Name,
                Nickname = poet.Nickname,
                Description = poet.Description,
                Published = poet.Published,
                BirthYearInLHijri = poet.BirthYearInLHijri,
                ValidBirthDate = poet.ValidBirthDate,
                DeathYearInLHijri = poet.DeathYearInLHijri,
                ValidDeathDate = poet.ValidDeathDate,
                PinOrder = poet.PinOrder,
                BirthLocationId = string.IsNullOrEmpty(poet.BirthPlace) ? null
                : (await _context.GanjoorGeoLocations.Where(l => l.Name == poet.BirthPlace).SingleAsync()).Id,
                DeathLocationId = string.IsNullOrEmpty(poet.DeathPlace) ? null
               : (await _context.GanjoorGeoLocations.Where(l => l.Name == poet.DeathPlace).SingleAsync()).Id

            };

            _context.GanjoorPoets.Add(dbPoet);

            var poetCatId = 1 + await _context.GanjoorCategories.MaxAsync(c => c.Id);

            GanjoorCat dbCat = new GanjoorCat()
            {
                Id = poetCatId,
                PoetId = id,
                Title = poet.Nickname,
                UrlSlug = poet.FullUrl.Substring(1),
                FullUrl = poet.FullUrl,
                TableOfContentsStyle = GanjoorTOC.Analyse,
                Published = true,
            };
            _context.GanjoorCategories.Add(dbCat);

            var poetPageId = 1 + await _context.GanjoorPages.MaxAsync(p => p.Id);
            while (await _context.GanjoorPoems.Where(p => p.Id == poetPageId).AnyAsync())
                poetPageId++;

            var pageText = "";
            foreach (var line in poet.Description.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                pageText += $"<p>{line}</p>{Environment.NewLine}";
            }

            GanjoorPage dbPage = new GanjoorPage()
            {
                Id = poetPageId,
                GanjoorPageType = GanjoorPageType.PoetPage,
                Published = poet.Published,
                PageOrder = -1,
                Title = poet.Nickname,
                FullTitle = poet.Nickname,
                UrlSlug = poet.FullUrl.Substring(1),
                FullUrl = poet.FullUrl,
                HtmlText = pageText,
                PoetId = id,
                CatId = poetCatId,
                PostDate = DateTime.Now
            };

            _context.GanjoorPages.Add(dbPage);

            GanjoorPageSnapshot snapshot = new GanjoorPageSnapshot()
            {
                GanjoorPageId = poetPageId,
                MadeObsoleteByUserId = editingUserId,
                RecordDate = DateTime.Now,
                Note = "ایجاد شاعر",
                Title = dbPage.Title,
                UrlSlug = dbPage.UrlSlug,
                HtmlText = dbPage.HtmlText,
            };

            _context.GanjoorPageSnapshots.Add(snapshot);

            await _context.SaveChangesAsync();

            CleanPoetCache(0);

            return await GetPoetById(id);
        }

        /// <summary>
        /// delete poet
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public RServiceResult<bool> StartDeletePoet(int id)
        {
            _backgroundTaskQueue.QueueBackgroundWorkItem
                        (
                        async token =>
                        {
                            using (RMuseumDbContext context = new RMuseumDbContext(new DbContextOptions<RMuseumDbContext>())) //this is long running job, so _context might be already been freed/collected by GC
                            {
                                LongRunningJobProgressServiceEF jobProgressServiceEF = new LongRunningJobProgressServiceEF(context);
                                var job = (await jobProgressServiceEF.NewJob($"Deleting Poet {id}", "Query data")).Result;
                                try
                                {
                                    var pages = await context.GanjoorPages.Where(p => p.PoetId == id).ToListAsync();
                                    context.GanjoorPages.RemoveRange(pages);
                                    await jobProgressServiceEF.UpdateJob(job.Id, 50, "Deleting page and Querying the poet - if no progress unplublish and regen group by centuries");
                                    var poet = await context.GanjoorPoets.Where(p => p.Id == id).SingleAsync();
                                    context.GanjoorPoets.Remove(poet);
                                    await jobProgressServiceEF.UpdateJob(job.Id, 99, "Deleting poet");
                                    await context.SaveChangesAsync();
                                    await jobProgressServiceEF.UpdateJob(job.Id, 100, "", true);
                                }
                                catch (Exception exp)
                                {
                                    await jobProgressServiceEF.UpdateJob(job.Id, 100, "", false, exp.ToString());
                                }

                            }
                        });

            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// delete page
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> DeletePageAsync(int id)
        {
            var firstChild = await _context.GanjoorPages.Where(p => p.ParentId == id).FirstOrDefaultAsync();
            if (firstChild != null)
            {
                return new RServiceResult<bool>(false, "Please delete children of the page first.");
            }
            var page = await _context.GanjoorPages.Where(p => p.Id == id).SingleAsync();
            if (page.PoemId != null)
            {
                return new RServiceResult<bool>(false, "Poem related pages can not be deleted.");
            }
            var cat = await _context.GanjoorCategories.Where(c => c.FullUrl == page.FullUrl).FirstOrDefaultAsync();
            if (cat != null)
            {
                return new RServiceResult<bool>(false, "Category related pages can not be deleted.");
            }
            _context.GanjoorPages.Remove(page);
            await _context.SaveChangesAsync();
            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// chaneg poet image
        /// </summary>
        /// <param name="poetId"></param>
        /// <param name="imageId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> ChangePoetImageAsync(int poetId, Guid imageId)
        {
            var dbPoet = await _context.GanjoorPoets.Where(p => p.Id == poetId).SingleAsync();
            dbPoet.RImageId = imageId;
            _context.GanjoorPoets.Update(dbPoet);
            await _context.SaveChangesAsync();
            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// batch rename
        /// </summary>
        /// <param name="catId"></param>
        /// <param name="model"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<string[]>> BatchRenameCatPoemTitles(int catId, GanjoorBatchNamingModel model, Guid userId)
        {
            var poems = await _context.GanjoorPoems.Where(p => p.CatId == catId).OrderBy(p => p.Id).ToListAsync();
            if (poems.Count == 0)
                return new RServiceResult<string[]>(new string[] { });

            var catPage = await _context.GanjoorPages.Where(p => p.GanjoorPageType == GanjoorPageType.CatPage && p.CatId == catId).SingleAsync();

            if (model.RemovePreviousPattern)
            {
                char[] numbers = "0123456789۰۱۲۳۴۵۶۷۸۹".ToArray();
                foreach (var poem in poems)
                {
                    _context.GanjoorPageSnapshots.Add
                        (
                        new GanjoorPageSnapshot()
                        {
                            GanjoorPageId = poem.Id,
                            MadeObsoleteByUserId = userId,
                            Title = poem.Title,
                            Note = "تغییر نام گروهی اشعار بخش",
                            RecordDate = DateTime.Now
                        }
                        );
                    int index = poem.Title.IndexOfAny(numbers);
                    if (index != 0)
                    {
                        while ((index + 1) < poem.Title.Length)
                        {
                            if (numbers.Contains(poem.Title[index + 1]))
                                index++;
                            else
                                break;
                        }
                        poem.Title = poem.Title[(index + 1)..].Trim();
                        if (poem.Title.IndexOf('-') == 0)
                        {
                            poem.Title = poem.Title[1..].Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(model.RemoveSetOfCharacters))
                    {
                        foreach (var c in model.RemoveSetOfCharacters)
                        {
                            poem.Title = poem.Title.Replace($"{c}", "");
                        }
                    }
                }
            }

            for (int i = 0; i < poems.Count; i++)
            {
                if (poems[i].Title.Length > 0)
                {
                    poems[i].Title = $"{model.StartWithNotIncludingSpaces} {(i + 1).ToPersianNumbers()} - {poems[i].Title}";
                }
                else
                {
                    poems[i].Title = $"{model.StartWithNotIncludingSpaces} {(i + 1).ToPersianNumbers()}";
                }

                poems[i].FullTitle = $"{catPage.FullTitle} » {poems[i].Title}";

            }

            if (!model.Simulate)
            {
                foreach (var poem in poems)
                {
                    var page = await _context.GanjoorPages.Where(p => p.Id == poem.Id).SingleAsync();
                    page.Title = poem.Title;
                    page.FullTitle = poem.FullTitle;
                    _context.GanjoorPages.Update(page);
                }

                _context.GanjoorPoems.UpdateRange(poems);

                await _context.SaveChangesAsync();
            }

            return new RServiceResult<string[]>(poems.Select(p => p.Title).ToArray());
        }

        private async Task<RServiceResult<GanjooRhymeAnalysisResult>> _FindPoemRhyme(int id, RMuseumDbContext context)
        {
            return new RServiceResult<GanjooRhymeAnalysisResult>(
                LanguageUtils.FindRhyme(await context.GanjoorVerses.Where(v => v.PoemId == id).OrderBy(v => v.VOrder).ToListAsync())
                );
        }

        /// <summary>
        /// find poem rhyme
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<GanjooRhymeAnalysisResult>> FindPoemRhyme(int id)
        {
            return await _FindPoemRhyme(id, _context);
        }

        private async Task _FindCategoryPoemsRhymesInternal(int catId, bool retag)
        {
            using (RMuseumDbContext context = new RMuseumDbContext(new DbContextOptions<RMuseumDbContext>())) //this is long running job, so _context might be already been freed/collected by GC
            {
                LongRunningJobProgressServiceEF jobProgressServiceEF = new LongRunningJobProgressServiceEF(context);
                var job = (await jobProgressServiceEF.NewJob($"FindCategoryPoemsRhymes Cat {catId}", "Query data")).Result;
                try
                {
                    var poems = await context.GanjoorPoems.Where(p => p.CatId == catId).ToListAsync();

                    int i = 0;
                    foreach (var poem in poems)
                    {
                        if (retag || string.IsNullOrEmpty(poem.RhymeLetters))
                        {
                            await jobProgressServiceEF.UpdateJob(job.Id, i++);
                            var res = await _FindPoemRhyme(poem.Id, context);
                            if (res.Result != null && !string.IsNullOrEmpty(res.Result.Rhyme))
                            {
                                poem.RhymeLetters = res.Result.Rhyme;
                                context.GanjoorPoems.Update(poem);
                            }

                        }
                    }
                    await jobProgressServiceEF.UpdateJob(job.Id, 99);
                    await context.SaveChangesAsync();
                    await jobProgressServiceEF.UpdateJob(job.Id, 100, "", true);
                }
                catch (Exception exp)
                {
                    await jobProgressServiceEF.UpdateJob(job.Id, 100, "", false, exp.ToString());
                }
            }
        }

        /// <summary>
        /// find category poem rhymes
        /// </summary>
        /// <param name="catId"></param>
        /// <param name="retag"></param>
        /// <returns></returns>
        public RServiceResult<bool> FindCategoryPoemsRhymes(int catId, bool retag)
        {
            _backgroundTaskQueue.QueueBackgroundWorkItem
                        (
                        async token =>
                        {
                            await _FindCategoryPoemsRhymesInternal(catId, retag);
                        });

            return new RServiceResult<bool>(true);
        }

        /// <summary>
        /// find poem rhythm
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<string>> FindPoemRhythm(int id)
        {
            var metres = (await GetGanjoorMetres()).Result.Select(m => m.Rhythm).ToArray();
            return await _FindPoemRhythm(id, _context, _httpClient, metres);
        }
        private async Task<RServiceResult<string>> _FindPoemRhythm(int id, RMuseumDbContext context, HttpClient httpClient, string[] metres, bool alwaysReturnaAResult = false)
        {
            try
            {
                var verses = await context.GanjoorVerses.AsNoTracking().Where(v => v.PoemId == id).OrderBy(v => v.VOrder).ToListAsync();

                Dictionary<string, int> rhytmCounter = new Dictionary<string, int>();

                for (int i = 0; i < verses.Count; i++)
                {
                    var verse = verses[i];

                    try
                    {
                        var response = await httpClient.GetAsync($"http://sorud.info/?Text={HttpUtility.UrlEncode(LanguageUtils.MakeTextSearchable(verse.Text))}");
                        response.EnsureSuccessStatusCode();
                        string result = await response.Content.ReadAsStringAsync();
                        if (result.IndexOf("آهنگِ همه‌ی بندها شناسایی نشد.") != -1)
                        {
                            continue;
                        }
                        int nVaznIndex = result.IndexOf("ctl00_MainContent_lblMetricBottom");
                        if (nVaznIndex == -1)
                        {
                            continue;
                        }
                        nVaznIndex += 2;
                        int nQuote1Index = result.IndexOf('\'', nVaznIndex);
                        if (nQuote1Index == -1)
                        {
                            continue;
                        }

                        int nQuote2Index = result.IndexOf('\'', nQuote1Index + 1);
                        if (nQuote2Index == -1)
                        {
                            continue;
                        }

                        string strRokn = result.Substring(nQuote1Index + 1, nQuote2Index - nQuote1Index - 1);

                        int nSpanClose = result.IndexOf("</span>", nQuote2Index + 1);

                        if (nSpanClose == -1)
                        {
                            continue;
                        }

                        int nFQ1 = result.IndexOf('«', nQuote2Index + 1);

                        if (nFQ1 == -1)
                        {
                            continue;
                        }


                        string rhythm = metres.Where(m => m.IndexOf(strRokn) == 0).SingleOrDefault();

                        if (string.IsNullOrEmpty(rhythm))
                            continue;

                        if (rhythm == "فاعلاتن فاعلن فاعلاتن فاعلن")
                            rhythm = "فاعلاتن فاعلاتن فاعلاتن فاعلن (رمل مثمن محذوف)";

                        if (rhythm == "مفاعلتن مفاعلتن مفاعلتن مفاعلتن")
                            rhythm = "مفاعیلن مفاعیلن مفاعیلن مفاعیلن (هزج مثمن سالم)";

                        if (rhytmCounter.TryGetValue(rhythm, out int count))
                        {
                            count++;
                            if (count > 10 || (count * 100.0 / verses.Count > 60))
                            {
                                return new RServiceResult<string>(rhythm);
                            }

                        }
                        else
                        {
                            count = 1;
                        }
                        rhytmCounter[rhythm] = count;
                    }
                    catch
                    {
                        //continue
                    }
                }

                if(alwaysReturnaAResult)
                {
                    int maxCount = -1;
                    string rhytm = "";
                    foreach (var r in rhytmCounter)
                    {
                        if(r.Value > maxCount)
                        {
                            maxCount = r.Value;
                            rhytm = r.Key;
                        }
                    }

                    if (!string.IsNullOrEmpty(rhytm))
                        return new RServiceResult<string>(rhytm);

                    if (verses.Count < 3)
                        return new RServiceResult<string>("dismissed");
                }

                return new RServiceResult<string>("");
            }
            catch (Exception exp)
            {
                return new RServiceResult<string>(null, exp.ToString());
            }
        }

        /// <summary>
        /// aggressive cache
        /// </summary>
        public bool AggressiveCacheEnabled
        {
            get
            {
                try
                {
                    return bool.Parse(Configuration["AggressiveCacheEnabled"]);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Database Context
        /// </summary>
        protected readonly RMuseumDbContext _context;

        /// <summary>
        /// Configuration
        /// </summary>
        protected IConfiguration Configuration { get; }

        /// <summary>
        /// Background Task Queue Instance
        /// </summary>
        protected readonly IBackgroundTaskQueue _backgroundTaskQueue;

        /// <summary>
        /// IAppUserService instance
        /// </summary>
        protected IAppUserService _appUserService;


        /// <summary>
        /// Messaging service
        /// </summary>
        protected readonly IRNotificationService _notificationService;

        /// <summary>
        /// Image File Service
        /// </summary>
        protected readonly IImageFileService _imageFileService;

        /// <summary>
        /// IMemoryCache
        /// </summary>
        protected readonly IMemoryCache _memoryCache;

        /// <summary>
        /// http client
        /// </summary>
        protected readonly HttpClient _httpClient;

        /// <summary>
        /// options service
        /// </summary>

        protected readonly IRGenericOptionsService _optionsService;

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="context"></param>
        /// <param name="configuration"></param>
        /// <param name="backgroundTaskQueue"></param>
        /// <param name="appUserService"></param>
        /// <param name="notificationService"></param>
        /// <param name="imageFileService"></param>
        /// <param name="memoryCache"></param>
        /// <param name="httpClient"></param>
        /// <param name="optionsService"></param>
        public GanjoorService(RMuseumDbContext context, IConfiguration configuration, IBackgroundTaskQueue backgroundTaskQueue, IAppUserService appUserService, IRNotificationService notificationService, IImageFileService imageFileService, IMemoryCache memoryCache, HttpClient httpClient, IRGenericOptionsService optionsService)
        {
            _context = context;
            _backgroundTaskQueue = backgroundTaskQueue;
            _appUserService = appUserService;
            _notificationService = notificationService;
            _imageFileService = imageFileService;
            _memoryCache = memoryCache;
            Configuration = configuration;
            _httpClient = httpClient;
            _optionsService = optionsService;
        }
    }
}
