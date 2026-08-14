using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using System.Reflection;
using Path = System.IO.Path;

namespace Aishi;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 90100)]
public class AddBanners(
    ModHelper modHelper,
    ImageRouter imageRouter,
    LocationTable locationTable)
    : IOnLoad
{
    private const bool ReplaceExistingBanners = false;
    private const bool EnableIsbBanner = true;
    private const bool EnableIsbWtBanner = true;
    private const bool EnableRuafBanner = true;
    private const bool EnableUntarBanner = true;
    private const bool EnableBlackDivBanner = true;
    private const bool EnableWedgeBanner = true;
    private const bool EnableCultistBanner = true;
    private const bool EnableOmonBanner = true;
    private const bool EnableRemBanner = true;
    private const bool EnableRoguesBanner = true;
    private const bool EnablePillagerBanner = true;
    private const bool EnableCleanupCrewBanner = true;
    private const bool EnableBloodhoundsBanner = true;
    private const bool EnableSmugglersBanner = true;
    private const bool EnableCultGangBanner = true;

    private static readonly List<BannerDefinition> BannerDefinitions = new()
    {
        new BannerDefinition("isb_banner", "isb_banner.png", EnableIsbBanner, new[] { "laboratory", "rezervbase", "factory4_day", "factory4_night", "sandbox", "sandbox_high", "labyrinth", "lighthouse", "shoreline", "suburbs" }),
        new BannerDefinition("wt_banner", "wt_banner.png", EnableIsbWtBanner, new[] { "laboratory", "rezervbase", "factory4_day", "factory4_night", "sandbox", "sandbox_high", "labyrinth", "lighthouse", "shoreline", "suburbs" }),
        new BannerDefinition("ruaf_banner", "ruaf_banner.png", EnableRuafBanner, new[] { "bigmap", "woods", "interchange", "shoreline", "tarkovstreets", "lighthouse" }),
        new BannerDefinition("untar_banner", "untar_banner.png", EnableUntarBanner, new[] { "bigmap", "woods", "interchange", "shoreline", "tarkovstreets" }),
        new BannerDefinition("blackdiv_banner", "blackdiv_banner.png", EnableBlackDivBanner, new[] { "laboratory", "rezervbase", "factory4_day", "factory4_night", "sandbox", "sandbox_high", "labyrinth", "lighthouse", "shoreline", "interchange", "tarkovstreets", "bigmap", "suburbs" }),
        new BannerDefinition("wedge_banner", "wedge_banner.png", EnableWedgeBanner, new[] { "laboratory", "rezervbase", "factory4_day", "factory4_night", "sandbox", "sandbox_high", "labyrinth", "lighthouse", "shoreline", "interchange", "tarkovstreets", "bigmap", "suburbs" }),
        new BannerDefinition("cultist_banner", "cultist_banner.png", EnableCultistBanner, new[] { "woods", "factory4_night", "shoreline", "bigmap", "sandbox_high", "sandbox" }),
        new BannerDefinition("omon_banner", "omon_banner.png", EnableOmonBanner, new[] { "" }),
        new BannerDefinition("rem_banner", "rem_banner.png", EnableRemBanner, new[] { "bigmap", "woods", "interchange", "shoreline", "tarkovstreets", "lighthouse" }),
        new BannerDefinition("rogues_banner", "rogues_banner.png", EnableRoguesBanner, new[] { "lighthouse", "suburbs" }),
        new BannerDefinition("pillagers_banner", "pillagers_banner.png", EnablePillagerBanner, new[] { "lighthouse", "shoreline", "interchange", "bigmap", "woods" }),
        new BannerDefinition("cleanupcrew_banner", "cleanupcrew_banner.png", EnableCleanupCrewBanner, new[] { "woods", "factory4_day", "shoreline", "bigmap" }),
        new BannerDefinition("bloodhounds_banner", "bloodhounds_banner.png", EnableBloodhoundsBanner, new[] { "bigmap", "woods", "shoreline" }),
        new BannerDefinition("smugglers_banner", "smugglers_banner.png", EnableSmugglersBanner, new[] { "bigmap", "woods", "interchange", "shoreline", "tarkovstreets", "lighthouse", "rezervbase", "sandbox_high" }),
        new BannerDefinition("cultgang_banner", "cultgang_banner.png", EnableCultGangBanner, new[] { "woods", "factory4_night", "shoreline", "bigmap", "sandbox_high", "sandbox" }),
    };

    private static readonly List<MapCoverDefinition> MapCoverDefinitions = new()
    {
        new MapCoverDefinition("suburbs", "icebreaker_cover", "icebreaker_cover.png"),
    };

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        var activeBanners = BannerDefinitions
            .Where(banner => banner.Enabled)
            .ToList();

        if (activeBanners.Count == 0)
        {
            return Task.CompletedTask;
        }

        var registeredBanners = new List<BannerDefinition>();

        foreach (var banner in activeBanners)
        {
            var imagePath = Path.Combine(pathToMod, "db", "banners", banner.FileName);

            if (!File.Exists(imagePath))
            {
                continue;
            }

            imageRouter.AddRoute(banner.RouteKey, imagePath);
            registeredBanners.Add(banner);
        }

        var registeredCovers = new List<MapCoverDefinition>();

        foreach (var cover in MapCoverDefinitions)
        {
            var imagePath = Path.Combine(pathToMod, "db", "banners", cover.FileName);

            if (!File.Exists(imagePath))
            {
                continue;
            }

            imageRouter.AddRoute(cover.RouteKey, imagePath);
            registeredCovers.Add(cover);
        }

        if (registeredBanners.Count == 0 && registeredCovers.Count == 0)
        {
            return Task.CompletedTask;
        }

        var locationDictionary = locationTable.GetDictionary();

        foreach (var entry in locationDictionary)
        {
            var locationBase = entry.Value?.Base;

            if (locationBase?.Banners == null)
            {
                continue;
            }

            if (ReplaceExistingBanners)
            {
                locationBase.Banners.Clear();
            }

            var mapKey = NormalizeMapKey(entry.Key);

            var cover = registeredCovers.FirstOrDefault(c => NormalizeMapKey(c.MapKey) == mapKey);

            if (cover != null)
            {
                var coverBanner = new Banner
                {
                    Id = cover.Id,
                    Picture = cover.CreatePicture()
                };

                if (locationBase.Banners.Count == 0)
                {
                    locationBase.Banners.Add(coverBanner);
                }
                else
                {
                    locationBase.Banners[0] = coverBanner;
                }
            }

            foreach (var banner in registeredBanners)
            {
                if (!banner.AppearsOnMap(mapKey))
                {
                    continue;
                }

                var existingBanner = locationBase.Banners.FirstOrDefault(existing => existing.Id == banner.Id);

                if (existingBanner != null)
                {
                    existingBanner.Picture = banner.CreatePicture();
                    continue;
                }

                locationBase.Banners.Add(new Banner
                {
                    Id = banner.Id,
                    Picture = banner.CreatePicture()
                });
            }

            ShuffleBanners(locationBase.Banners);
        }

        return Task.CompletedTask;
    }

    private static string NormalizeMapKey(string key)
    {
        return key.Replace("_", "").ToLowerInvariant();
    }

    private static void ShuffleBanners(List<Banner> banners)
    {
        for (var i = banners.Count - 1; i > 1; i--)
        {
            var j = Random.Shared.Next(1, i + 1);
            (banners[i], banners[j]) = (banners[j], banners[i]);
        }
    }

    private sealed record BannerDefinition(string Id, string FileName, bool Enabled, string[] Maps)
    {
        private string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FileName).ToLowerInvariant();

        public string RouteKey => $"/files/banners/{FileNameWithoutExtension}";

        public string PicPath => $"banners/{FileName}";

        public string PicType => Path.GetExtension(FileName).TrimStart('.').ToLowerInvariant();

        public bool AppearsOnMap(string normalizedMapKey)
        {
            if (Maps == null || Maps.Length == 0)
            {
                return true;
            }

            return Maps.Any(map => NormalizeMapKey(map) == normalizedMapKey);
        }

        public Pic CreatePicture()
        {
            return new Pic
            {
                File = FileName,
                Path = PicPath,
                Rcid = "",
                Type = PicType
            };
        }
    }

    private sealed record MapCoverDefinition(string MapKey, string Id, string FileName)
    {
        private string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FileName).ToLowerInvariant();

        public string RouteKey => $"/files/banners/{FileNameWithoutExtension}";

        public string PicPath => $"banners/{FileName}";

        public string PicType => Path.GetExtension(FileName).TrimStart('.').ToLowerInvariant();

        public Pic CreatePicture()
        {
            return new Pic
            {
                File = FileName,
                Path = PicPath,
                Rcid = "",
                Type = PicType
            };
        }
    }
}