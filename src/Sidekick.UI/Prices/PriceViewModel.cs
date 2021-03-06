using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using PropertyChanged;
using Sidekick.Business.Apis.Poe.Models;
using Sidekick.Business.Apis.Poe.Parser;
using Sidekick.Business.Apis.Poe.Trade;
using Sidekick.Business.Apis.Poe.Trade.Data.Static;
using Sidekick.Business.Apis.Poe.Trade.Data.Stats;
using Sidekick.Business.Apis.Poe.Trade.Search;
using Sidekick.Business.Apis.Poe.Trade.Search.Filters;
using Sidekick.Business.Apis.PoeNinja;
using Sidekick.Business.Apis.PoePriceInfo.Models;
using Sidekick.Business.Languages;
using Sidekick.Core.Natives;
using Sidekick.Core.Settings;
using Sidekick.Localization.Prices;
using Sidekick.UI.Items;

namespace Sidekick.UI.Prices
{
    [AddINotifyPropertyChangedInterface]
    public class PriceViewModel : IPriceViewModel
    {
        private readonly ITradeSearchService tradeSearchService;
        private readonly IPoeNinjaCache poeNinjaCache;
        private readonly IStaticDataService staticDataService;
        private readonly ILanguageProvider languageProvider;
        private readonly IPoePriceInfoClient poePriceInfoClient;
        private readonly INativeClipboard nativeClipboard;
        private readonly IParserService parserService;
        private readonly SidekickSettings settings;
        private readonly IStatDataService statDataService;

        public PriceViewModel(
            ITradeSearchService tradeSearchService,
            IPoeNinjaCache poeNinjaCache,
            IStaticDataService staticDataService,
            ILanguageProvider languageProvider,
            IPoePriceInfoClient poePriceInfoClient,
            INativeClipboard nativeClipboard,
            IParserService parserService,
            SidekickSettings settings,
            IStatDataService statDataService)
        {
            this.tradeSearchService = tradeSearchService;
            this.poeNinjaCache = poeNinjaCache;
            this.staticDataService = staticDataService;
            this.languageProvider = languageProvider;
            this.poePriceInfoClient = poePriceInfoClient;
            this.nativeClipboard = nativeClipboard;
            this.parserService = parserService;
            this.settings = settings;
            this.statDataService = statDataService;
            Task.Run(Initialize);
        }

        public ParsedItem Item { get; private set; }

        public string ItemColor => Item?.GetColor();

        public ObservableCollection<PriceItem> Results { get; private set; }

        public Uri Uri => QueryResult?.Uri;

        public bool IsError { get; private set; }
        public bool IsNotError => !IsError;

        public bool IsFetching { get; private set; }
        public bool IsFetched => !IsFetching;

        public bool IsCurrency { get; private set; }

        public ObservableCollection<PriceFilterCategory> Filters { get; set; }

        private async Task Initialize()
        {
            Item = await parserService.ParseItem(nativeClipboard.LastCopiedText);

            if (Item == null)
            {
                IsError = true;
                return;
            }

            IsCurrency = Item.Rarity == Rarity.Currency;

            InitializeFilters();

            await UpdateQuery();

            GetPoeNinjaPrice();

            if (settings.EnablePricePrediction)
            {
                _ = GetPredictionPrice();
            }
        }

        private void InitializeFilters()
        {
            Filters = new ObservableCollection<PriceFilterCategory>();

            var propertyCategory = new PriceFilterCategory()
            {
                Label = PriceResources.Filters_Properties
            };
            InitializeFilter(propertyCategory, nameof(SearchFilters.ArmourFilters), nameof(ArmorFilter.Armor), languageProvider.Language.DescriptionArmour, Item.Armor);
            InitializeFilter(propertyCategory, nameof(SearchFilters.ArmourFilters), nameof(ArmorFilter.EnergyShield), languageProvider.Language.DescriptionEnergyShield, Item.EnergyShield);
            InitializeFilter(propertyCategory, nameof(SearchFilters.ArmourFilters), nameof(ArmorFilter.Evasion), languageProvider.Language.DescriptionEvasion, Item.Evasion);
            InitializeFilter(propertyCategory, nameof(SearchFilters.ArmourFilters), nameof(ArmorFilter.Block), languageProvider.Language.DescriptionChanceToBlock, Item.ChanceToBlock,
                delta: 1);

            InitializeFilter(propertyCategory, nameof(SearchFilters.MapFilters), nameof(MapFilter.ItemQuantity), languageProvider.Language.DescriptionItemQuantity, Item.ItemQuantity);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MapFilters), nameof(MapFilter.ItemRarity), languageProvider.Language.DescriptionItemRarity, Item.ItemRarity);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MapFilters), nameof(MapFilter.MonsterPackSize), languageProvider.Language.DescriptionMonsterPackSize, Item.MonsterPackSize);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MapFilters), nameof(MapFilter.Blighted), languageProvider.Language.PrefixBlighted, Item.Blighted,
                enabled: Item.Blighted);

            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.Quality), languageProvider.Language.DescriptionQuality, Item.Quality,
                enabled: Item.Rarity == Rarity.Gem && Item.Quality >= 20,
                min: Item.Rarity == Rarity.Gem && Item.Quality > 20 ? (double?)Item.Quality : null);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.GemLevel), languageProvider.Language.DescriptionLevel, Item.Level,
                enabled: Item.Level >= 21);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.ItemLevel), languageProvider.Language.DescriptionItemLevel, Item.ItemLevel,
                enabled: Item.ItemLevel >= 80,
                min: Item.ItemLevel >= 80 ? (double?)Item.ItemLevel : null);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.Corrupted), languageProvider.Language.DescriptionCorrupted, Item.Corrupted,
                alwaysIncluded: Item.Rarity == Rarity.Gem || Item.Rarity == Rarity.Unique,
                enabled: (Item.Rarity == Rarity.Gem || Item.Rarity == Rarity.Unique) && Item.Corrupted);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.CrusaderItem), languageProvider.Language.InfluenceCrusader, Item.Influences.Crusader,
                enabled: Item.Influences.Crusader);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.ElderItem), languageProvider.Language.InfluenceElder, Item.Influences.Elder,
                enabled: Item.Influences.Elder);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.HunterItem), languageProvider.Language.InfluenceHunter, Item.Influences.Hunter,
                enabled: Item.Influences.Hunter);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.RedeemerItem), languageProvider.Language.InfluenceRedeemer, Item.Influences.Redeemer,
                enabled: Item.Influences.Redeemer);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.ShaperItem), languageProvider.Language.InfluenceShaper, Item.Influences.Shaper,
                enabled: Item.Influences.Shaper);
            InitializeFilter(propertyCategory, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.WarlordItem), languageProvider.Language.InfluenceWarlord, Item.Influences.Warlord,
                enabled: Item.Influences.Warlord);

            InitializeFilter(propertyCategory, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.PhysicalDps), PriceResources.Filters_PDps, Item.Extended.PhysicalDps);
            InitializeFilter(propertyCategory, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.ElementalDps), PriceResources.Filters_EDps, Item.Extended.ElementalDps);
            InitializeFilter(propertyCategory, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.DamagePerSecond), PriceResources.Filters_Dps, Item.Extended.DamagePerSecond);
            InitializeFilter(propertyCategory, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.AttacksPerSecond), languageProvider.Language.DescriptionAttacksPerSecond, Item.AttacksPerSecond,
                delta: 0.1);
            InitializeFilter(propertyCategory, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.CriticalStrikeChance), languageProvider.Language.DescriptionCriticalStrikeChance, Item.CriticalStrikeChance,
                delta: 1);

            if (propertyCategory.Filters.Any())
            {
                Filters.Add(propertyCategory);
            }

            InitializeMods(Item.Extended.Mods.Pseudo);
            InitializeMods(Item.Extended.Mods.Explicit);
            InitializeMods(Item.Extended.Mods.Implicit);
            InitializeMods(Item.Extended.Mods.Crafted);
            InitializeMods(Item.Extended.Mods.Enchant);

            if (Filters.Count == 0)
            {
                Filters = null;
            }
        }

        private void InitializeMods(List<Mod> mods)
        {
            if (mods.Count == 0)
            {
                return;
            }

            PriceFilterCategory category = null;

            var magnitudes = mods
                .SelectMany(x => x.Magnitudes)
                .GroupBy(x => x.Hash)
                .Select(x => new
                {
                    Definition = statDataService.GetById(x.First().Hash),
                    Magnitudes = x
                })
                .ToList();

            foreach (var magnitude in magnitudes)
            {
                if (category == null)
                {
                    category = new PriceFilterCategory()
                    {
                        Label = magnitude.Definition.Category
                    };
                }

                InitializeFilter(category, nameof(StatFilter), magnitude.Definition.Id, magnitude.Definition.Text, magnitude.Magnitudes,
                    enabled: settings.Modifiers.Contains(magnitude.Definition.Id)
                );
            }

            if (category != null)
            {
                Filters.Add(category);
            }
        }

        private void InitializeFilter<T>(PriceFilterCategory category, string type, string id, string label, T value, double delta = 5, bool enabled = false, double? min = null, bool alwaysIncluded = false)
        {
            if (value is bool boolValue)
            {
                if (!boolValue && !alwaysIncluded)
                {
                    return;
                }
            }
            else if (value is int intValue)
            {
                if (intValue == 0 && !alwaysIncluded)
                {
                    return;
                }
                if (min == null)
                {
                    min = (int)Math.Min(intValue - delta, intValue * 0.9);
                }
            }
            else if (value is double doubleValue)
            {
                if (doubleValue == 0 && !alwaysIncluded)
                {
                    return;
                }
                if (min == null)
                {
                    min = (int)Math.Min(doubleValue - delta, doubleValue * 0.9);
                }
            }
            else if (value is IGrouping<string, Magnitude> groupValue)
            {
                min = groupValue.Select(x => x.Min).OrderBy(x => x).FirstOrDefault();
                if (min.HasValue)
                {
                    min = (int)Math.Min(min.Value - delta, min.Value * 0.9);
                }
            }

            var priceFilter = new PriceFilter()
            {
                Enabled = enabled,
                Type = type,
                Id = id,
                Text = label,
                Min = min,
                Max = null,
                HasRange = min.HasValue
            };

            priceFilter.PropertyChanged += async (object sender, PropertyChangedEventArgs e) => { await UpdateQuery(); };

            category.Filters.Add(priceFilter);
        }

        private FetchResult<string> QueryResult { get; set; }

        public async Task UpdateQuery()
        {
            Results = null;

            if (Filters != null)
            {
                var saveSettings = false;
                foreach (var filter in Filters.SelectMany(x => x.Filters).Where(x => x.Type == nameof(StatFilter)))
                {
                    if (settings.Modifiers.Contains(filter.Id))
                    {
                        if (!filter.Enabled)
                        {
                            saveSettings = true;
                            settings.Modifiers.Remove(filter.Id);
                        }
                    }
                    else
                    {
                        if (filter.Enabled)
                        {
                            saveSettings = true;
                            settings.Modifiers.Add(filter.Id);
                        }
                    }
                }
                if (saveSettings)
                {
                    settings.Save();
                }
            }

            IsFetching = true;
            if (Item.Rarity == Rarity.Currency)
            {
                QueryResult = await tradeSearchService.SearchBulk(Item);
            }
            else
            {
                QueryResult = await tradeSearchService.Search(Item, GetFilters(), GetStats());
            }
            IsFetching = false;
            if (QueryResult == null)
            {
                IsError = true;
            }
            else if (QueryResult.Result.Any())
            {
                await LoadMoreData();
            }

            UpdateCountString();
        }

        private List<StatFilter> GetStats()
        {
            return Filters?
                .SelectMany(x => x.Filters)
                .Where(x => x.Type == nameof(StatFilter))
                .Where(x => x.Enabled)
                .Select(x => new StatFilter()
                {
                    Disabled = !x.Enabled,
                    Id = x.Id,
                    Value = new SearchFilterValue()
                    {
                        Max = x.Max,
                        Min = x.Min
                    }
                })
                .ToList();
        }

        private SearchFilters GetFilters()
        {
            var filters = Filters?
                .SelectMany(x => x.Filters);

            if (filters == null)
            {
                return null;
            }

            var searchFilters = new SearchFilters();

            foreach (var filter in filters)
            {
                var property = searchFilters.GetType().GetProperty(filter.Type);
                if (property == null)
                {
                    continue;
                }

                var typeObject = property.GetValue(searchFilters);
                property = typeObject.GetType().GetProperty("Filters");
                if (property == null)
                {
                    continue;
                }

                var filtersObject = property.GetValue(typeObject);
                property = filtersObject.GetType().GetProperty(filter.Id);
                if (property == null)
                {
                    continue;
                }

                if (property.PropertyType == typeof(SearchFilterOption))
                {
                    property.SetValue(filtersObject, new SearchFilterOption(filter.Enabled ? "true" : "false"));
                }
                else if (property.PropertyType == typeof(SearchFilterValue))
                {
                    if (!filter.Enabled)
                    {
                        continue;
                    }
                    var valueObject = new SearchFilterValue
                    {
                        Max = filter.Max,
                        Min = filter.Min
                    };
                    property.SetValue(filtersObject, valueObject);
                }
            }

            return searchFilters;
        }

        public async Task LoadMoreData()
        {
            var ids = QueryResult.Result.Skip(Results?.Count ?? 0).Take(10).ToList();
            if (IsFetching || ids.Count == 0)
            {
                return;
            }

            IsFetching = true;
            var getResult = await tradeSearchService.GetResults(QueryResult.Id, ids);
            IsFetching = false;
            if (getResult.Result.Any())
            {
                var items = new List<PriceItem>();

                foreach (var result in getResult.Result)
                {
                    items.Add(new PriceItem(result)
                    {
                        ImageUrl = new Uri(
                            languageProvider.Language.PoeCdnBaseUrl,
                            staticDataService.GetImage(result.Listing.Price.Currency)
                        ).AbsoluteUri,
                    });
                }

                if (Results == null)
                {
                    Results = new ObservableCollection<PriceItem>(items);
                }
                else
                {
                    items.ForEach(x => Results.Add(x));
                }
            }

            UpdateCountString();
        }

        public bool IsPredicted => !string.IsNullOrEmpty(PredictionText);
        public string PredictionText { get; private set; }
        private async Task GetPredictionPrice()
        {
            PredictionText = string.Empty;

            if (!IsPoeNinja)
            {
                var result = await poePriceInfoClient.GetItemPricePrediction(Item.ItemText);
                if (result?.ErrorCode != 0)
                {
                    return;
                }

                PredictionText = string.Format(
                    PriceResources.PredictionString,
                    $"{result.Min?.ToString("N1")}-{result.Max?.ToString("N1")} {result.Currency}",
                    result.ConfidenceScore.ToString("N1"));
            }
        }

        public bool IsPoeNinja => !string.IsNullOrEmpty(PoeNinjaText);
        public string PoeNinjaText { get; private set; }
        private void GetPoeNinjaPrice()
        {
            PoeNinjaText = string.Empty;

            var poeNinjaItem = poeNinjaCache.GetItem(Item);
            if (poeNinjaItem != null)
            {
                PoeNinjaText = string.Format(
                    PriceResources.PoeNinjaString,
                    poeNinjaItem.ChaosValue,
                    poeNinjaCache.LastRefreshTimestamp.Value.ToString("HH:mm"));
            }
        }

        public string CountString { get; private set; }
        private void UpdateCountString()
        {
            CountString = string.Format(PriceResources.CountString, Results?.Count ?? 0, QueryResult?.Total.ToString() ?? "?");
        }

        public bool HasPreviewItem { get; private set; }
        public PriceItem PreviewItem { get; private set; }
        public void Preview(PriceItem selectedItem)
        {
            PreviewItem = selectedItem;
            HasPreviewItem = PreviewItem != null;
        }
    }
}
